﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IBALib;
using IBALib.Interfaces;
using IBALib.Types;
using Processing;
using Processing.ImageProcessing;
using Processing.ImageProcessing.Commands;
using SixLabors.ImageSharp;
using SourceProvider.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopUI
{
    public class MainWindow : Window
    {
        private bool __isStared;
        private bool _isStarted
        {
            get
            {
                return __isStared;
            }
            set
            {
                __isStared = value;
                SwitchUIEnable();
            }
        }

        private int __currentTasksCount = 0;
        private int __currentTasksDone = 0;

        private int _currentTasksCount
        {
            get
            {
                return __currentTasksCount;
            }
            set
            {
                Interlocked.Add(ref __currentTasksCount, value);
                UpdateProgress();
            }
        }

        private int _CurrentTasksDone
        {
            get
            {
                return __currentTasksDone;
            }
            set
            {
                Interlocked.Add(ref __currentTasksDone, value);
                UpdateProgress();
            }
        }

        private Grid _grid;
        private Button _button;
        private TextBox _heightTB;
        private TextBox _widthTB;
        private TextBox _resHeightTB;
        private TextBox _resWidthTB;
        private TextBox _imagesCountTB;
        private TextBox _cyclesCountTB;
        private CheckBox _roundTripCb;
        private ProgressBar _progressBar;
        private ScrollViewer _scrollViewer;
        private List<CheckBox> _algorithmsCheckboxes;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private static ConcurrentDictionary<string, IBlendAlgorithm> _algorithms = new ConcurrentDictionary<string, IBlendAlgorithm>();

        public MainWindow()
        {
            this.InitializeComponent();
            this.AttachDevTools();
            _grid = this.FindControl<Grid>("grid");
            _algorithmsCheckboxes = new List<CheckBox>();
            BuildComponents();
            _button = this.FindControl<Button>("startBtn");
            _button.Click += delegate { StartButtonHandle(); };
            _widthTB = this.FindControl<TextBox>("widthTB");
            _heightTB = this.FindControl<TextBox>("heightTB");
            _resWidthTB = this.FindControl<TextBox>("resWidthTB");
            _resHeightTB = this.FindControl<TextBox>("resHeightTB");
            _imagesCountTB = this.FindControl<TextBox>("imagesCountTB");
            _cyclesCountTB = this.FindControl<TextBox>("cyclesCountTB");
            _progressBar = this.FindControl<ProgressBar>("progressBar");
            _scrollViewer = this.FindControl<ScrollViewer>("outputScroll");
            _roundTripCb = this.FindControl<CheckBox>("rndTripCb");
            _roundTripCb.Click += (sender, args) =>
            {
                _algorithmsCheckboxes.ForEach(cb => cb.IsEnabled = !(sender as CheckBox).IsChecked.Value);
            };
            var outputTB = this.FindControl<TextBlock>("output");
            Log.RegisterCallback((message) =>
            {
                Dispatcher.UIThread.InvokeAsync(() => {
                    outputTB.Text += message + "\r\n";
                    _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, _scrollViewer.Offset.Y + 100);
                });
            });
            Log.Debug("=== Desktop UI has been initialized ===");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoaderPortableXaml.Load(this);
        }

        private void BuildComponents()
        {
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                int row = 0;
                foreach (var type in assembly.GetTypes())
                {
                    if(type.GetCustomAttributes(typeof(ImageBlendingAlgorithmAttribute), false).Length > 0)
                    {
                        var cb = new CheckBox
                        {
                            Content = type.Name,
                            Height = 25,
                            Name = type.Name
                        };
                        _algorithms.TryAdd(type.Name, Activator.CreateInstance(type) as IBlendAlgorithm);
                        _algorithmsCheckboxes.Add(cb);
                        _grid.Children.Add(cb);
                        if (row > 3)
                            _grid.RowDefinitions.Add(new RowDefinition());
                        Grid.SetColumn(cb, 3);
                        Grid.SetRow(cb, row++);
                    }
                }
            }
        }

        private async void StartButtonHandle()
        {
            _progressBar.Value = 0;
            var heigth = _heightTB.Text;
            var width = _widthTB.Text;
            var imagesCount = int.Parse(_imagesCountTB.Text ?? "0");
            var cyclesCount = int.Parse(_cyclesCountTB.Text ?? "0");
            _currentTasksCount += cyclesCount;
            _isStarted = !_isStarted;
            _button.Content = _isStarted ? "Stop" : "Start";
            if (_isStarted)
            {
                Log.Debug("Begin execution");
                var processTasks = new Task[cyclesCount];
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
                for (int i = 0; i < cyclesCount; i++)
                {
                    processTasks[i] = Process(heigth, width, imagesCount);
                }
                await Task.WhenAll(processTasks);
            }
            else if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;
                GC.Collect();
            }
        }

        private void SwitchUIEnable()
        {
            _heightTB.IsEnabled = 
                _widthTB.IsEnabled =
                _resWidthTB.IsEnabled =
                _resHeightTB.IsEnabled =
                _roundTripCb.IsEnabled =
                _cyclesCountTB.IsEnabled =
                !_isStarted;
            _algorithmsCheckboxes.ForEach(c => c.IsEnabled = !_isStarted);
            if(_isStarted)
            {
                _currentTasksCount = 0;
                _progressBar.Value = 0;
            }
        }

        private async Task Process(string heigth, string width, int imagesCount)
        {
            var tasks = new Task<Image<Rgba32>>[imagesCount];
            try
            {
                var uHeigth = uint.Parse(heigth);
                var uWidth = uint.Parse(width);
                Log.Debug("Begin loading images");
                _currentTasksCount += imagesCount;
                for (int i = 0; i < imagesCount; i++)
                {
                    tasks[i] = SrcLoader.DownloadImageAsync(uWidth, uHeigth, _cancellationToken);
                }
                await Task.WhenAll(tasks);
                _CurrentTasksDone += imagesCount;
                Log.Debug("Finish loading images");
                var ts = TaskScheduler.FromCurrentSynchronizationContext();
                var checkedCb = _algorithmsCheckboxes.Where(c => _roundTripCb.IsChecked.Value || c.IsChecked.Value).ToList();
                var tasksForGenerating = new Task[checkedCb.Count];
                _currentTasksCount += checkedCb.Count;
                Log.Debug("Begin image generating");
                for (int i = 0; i < checkedCb.Count; i++)
                {
                    int n = i;
                    tasksForGenerating[i] = new Task(() =>
                    {
                        var proc = new ImageProcessor<Rgba32>();
                        proc.AddCommand(new ApplyAlgorithmCommand(_algorithms[checkedCb.ElementAt(n).Name], typeof(Image<>)));

                        var resHeight = uint.Parse(_resHeightTB.Text ?? "0");
                        var resWidth = uint.Parse(_resWidthTB.Text ?? "0");

                        if(resHeight != uHeigth || resWidth != uWidth)
                            proc.AddCommand(new ScaleImageCommand(AlgorithmFactory.Instance.ScalingAlgorithmsDictionary[AlgorithmFactory.ALGORITHM.NearestNeighborDownscale], (int)resWidth, (int)resHeight, typeof(Image<>)));
                        proc.AddImage(tasks.Select(t => new ImageWrapper<Rgba32>(t.Result)));
                        Log.Debug("Begin processing image");
                        proc.Process();
                        Log.Debug("End processing image");
                        using (var fs = File.Create($"./{Guid.NewGuid().ToString()}.jpg"))
                        {
                            ((proc.Result as dynamic).GetSource as Image<Rgba32>).SaveAsJpeg(fs);
                        }
                        _CurrentTasksDone++;
                    }, _cancellationToken);
                    tasksForGenerating[i].Start();
                }
                await Task.WhenAll(tasksForGenerating);
                Log.Debug("End image generating");
                if (_isStarted) StartButtonHandle();
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    //TODO: log it and don't throw
                }
                else
                    throw;
            }
            finally
            {
                tasks.ToList().ForEach(x =>
                {
                    if (x.Status == TaskStatus.RanToCompletion) x.Result.Dispose();
                    x = null;
                });
            }
            _CurrentTasksDone++;
        }
        
        private void UpdateProgress()
        {
            var value = 100.0 * ((double) __currentTasksDone / __currentTasksCount);
            Dispatcher.UIThread.Post(() =>
            {
                _progressBar.Value = value;
            });
        }
    }
}
