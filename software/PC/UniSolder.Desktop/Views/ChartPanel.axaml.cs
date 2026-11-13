using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Plottables;
using UniSolder.Desktop.Models;
using UniSolder.Desktop.ViewModels;

namespace UniSolder.Desktop.Views
{
    public partial class ChartPanel : UserControl
    {
        private readonly DispatcherTimer _renderTimer;
        private readonly List<(Signal SignalPlot, ChartSeriesConfig Config)> _signalBindings = new List<(Signal, ChartSeriesConfig)>();
        private TelemetryChartViewModel? _currentVm;

        public ChartPanel()
        {
            InitializeComponent();

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
            };
            _renderTimer.Tick += OnRenderTick;

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_currentVm != null)
            {
                _renderTimer.Stop();
            }

            _currentVm = DataContext as TelemetryChartViewModel;
            if (_currentVm != null)
            {
                SetupPlot(_currentVm);
                _renderTimer.Start();
            }
        }

        private void SetupPlot(TelemetryChartViewModel vm)
        {
            var plot = PlotControl.Plot;
            plot.Clear();
            _signalBindings.Clear();

            // Dark Theme styling
            plot.FigureBackground.Color = ScottPlot.Color.FromHex("#0F172A");
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#0F172A");
            plot.Axes.Color(ScottPlot.Color.FromHex("#94A3B8"));
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#1E293B");

            plot.Axes.Left.Label.Text = "Temperature (°C) / Duty (%)";
            plot.Axes.Bottom.Label.Text = "Rolling Time Window (256 Samples)";
            plot.Axes.SetLimits(0, TelemetryChartViewModel.BufferSize, 0, 500);

            // Add Signal plots mapped to buffers
            AddSignalBinding(plot, vm.CurrentTempData, FindSeries(vm, "CTemp"));
            AddSignalBinding(plot, vm.TargetTempData, FindSeries(vm, "CTTemp"));
            AddSignalBinding(plot, vm.AdcTempData, FindSeries(vm, "ADCTemp"));
            AddSignalBinding(plot, vm.AvgFilterTempData, FindSeries(vm, "TAvgF"));
            AddSignalBinding(plot, vm.AvgPowerData, FindSeries(vm, "TAvgP"));
            AddSignalBinding(plot, vm.HeaterResData, FindSeries(vm, "CHRes"));
            AddSignalBinding(plot, vm.DutyData, FindSeries(vm, "Duty"));
            AddSignalBinding(plot, vm.PowerData, FindSeries(vm, "Power"));
            AddSignalBinding(plot, vm.DestReachedData, FindSeries(vm, "DestReached"));
            AddSignalBinding(plot, vm.WsDeltaData, FindSeries(vm, "WsDelta"));

            PlotControl.Refresh();
        }

        private ChartSeriesConfig? FindSeries(TelemetryChartViewModel vm, string key)
        {
            foreach (var series in vm.SeriesList)
            {
                if (series.Key == key) return series;
            }
            return null;
        }

        private void AddSignalBinding(Plot plot, double[] buffer, ChartSeriesConfig? config)
        {
            if (config == null) return;

            var signal = plot.Add.Signal(buffer);
            signal.Color = ScottPlot.Color.FromHex(config.HexColor);
            signal.LineWidth = 2;
            signal.IsVisible = config.IsVisible;

            _signalBindings.Add((signal, config));
        }

        private void OnRenderTick(object? sender, EventArgs e)
        {
            if (_currentVm == null) return;

            bool needsRefresh = false;

            foreach (var (signal, config) in _signalBindings)
            {
                if (signal.IsVisible != config.IsVisible)
                {
                    signal.IsVisible = config.IsVisible;
                    needsRefresh = true;
                }
            }

            if (!_currentVm.IsPaused || needsRefresh)
            {
                PlotControl.Refresh();
            }
        }
    }
}
