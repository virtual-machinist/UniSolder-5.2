using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniSolder.Core;
using UniSolder.Desktop.Models;
using UniSolder.Desktop.Services;

namespace UniSolder.Desktop.ViewModels
{
    public partial class TelemetryChartViewModel : ViewModelBase, IDisposable
    {
        public const int BufferSize = 256;

        private readonly IDeviceConnectionService _connectionService;
        private readonly object _dataLock = new object();

        public double[] TargetTempData { get; } = new double[BufferSize];
        public double[] CurrentTempData { get; } = new double[BufferSize];
        public double[] AdcTempData { get; } = new double[BufferSize];
        public double[] AvgFilterTempData { get; } = new double[BufferSize];
        public double[] AvgPowerData { get; } = new double[BufferSize];
        public double[] HeaterResData { get; } = new double[BufferSize];
        public double[] DutyData { get; } = new double[BufferSize];
        public double[] PowerData { get; } = new double[BufferSize];
        public double[] DestReachedData { get; } = new double[BufferSize];
        public double[] WsDeltaData { get; } = new double[BufferSize];

        public ObservableCollection<ChartSeriesConfig> SeriesList { get; } = new ObservableCollection<ChartSeriesConfig>();

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private int _sampleCount;

        public int CurrentIndex { get; private set; }

        public event EventHandler? ChartRenderRequested;

        public TelemetryChartViewModel(IDeviceConnectionService connectionService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));

            InitializeSeries();

            _connectionService.TelemetryReceived += OnTelemetryReceived;
        }

        private void InitializeSeries()
        {
            SeriesList.Add(new ChartSeriesConfig("CTemp", "Current Tip Temp", "#22C55E", "°C", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("CTTemp", "Cold Junction Temp", "#EF4444", "°C", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("ADCTemp", "Raw ADC Temp", "#38BDF8", "°C", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("TAvgF", "Filtered Avg Temp", "#EC4899", "°C", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("TAvgP", "Target Avg Power", "#F97316", "W", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("CHRes", "Heater Resistance", "#94A3B8", "mΩ", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("Duty", "Duty Cycle", "#DC2626", "%", isVisible: true));
            SeriesList.Add(new ChartSeriesConfig("Power", "Power On/Off", "#3B82F6", "", isVisible: false));
            SeriesList.Add(new ChartSeriesConfig("DestReached", "Target Reached", "#15803D", "%", isVisible: false));
            SeriesList.Add(new ChartSeriesConfig("WsDelta", "Waveform Delta", "#F8FAFC", "", isVisible: false));
        }

        private void OnTelemetryReceived(object? sender, TelemetryData data)
        {
            if (IsPaused || data == null) return;

            AddTelemetryData(data);
        }

        public void AddTelemetryData(TelemetryData data)
        {
            lock (_dataLock)
            {
                CurrentIndex = (CurrentIndex + 1) % BufferSize;

                CurrentTempData[CurrentIndex] = data.TipTemp;
                TargetTempData[CurrentIndex] = data.ColdJunctionTemp;
                AdcTempData[CurrentIndex] = data.AdcTemp;
                AvgFilterTempData[CurrentIndex] = data.TargetAvgFiltered;
                AvgPowerData[CurrentIndex] = data.TargetAvgPower;
                HeaterResData[CurrentIndex] = data.HeaterResistance;
                DutyData[CurrentIndex] = data.Duty;
                PowerData[CurrentIndex] = data.IsPowerOn ? 100.0 : 0.0;
                DestReachedData[CurrentIndex] = data.DestinationReached;
                WsDeltaData[CurrentIndex] = data.WsDelta.Length > 0 ? data.WsDelta[0] : 0.0;

                SampleCount++;

                UpdateSeriesLiveValues(data);
            }

            ChartRenderRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateSeriesLiveValues(TelemetryData data)
        {
            foreach (var series in SeriesList)
            {
                series.CurrentValue = series.Key switch
                {
                    "CTemp" => data.TipTemp,
                    "CTTemp" => data.ColdJunctionTemp,
                    "ADCTemp" => data.AdcTemp,
                    "TAvgF" => data.TargetAvgFiltered,
                    "TAvgP" => data.TargetAvgPower,
                    "CHRes" => data.HeaterResistance,
                    "Duty" => data.Duty,
                    "Power" => data.IsPowerOn ? 100.0 : 0.0,
                    "DestReached" => data.DestinationReached,
                    "WsDelta" => data.WsDelta.Length > 0 ? data.WsDelta[0] : 0.0,
                    _ => 0.0
                };
            }
        }

        [RelayCommand]
        public void TogglePause()
        {
            IsPaused = !IsPaused;
        }

        [RelayCommand]
        public void ClearData()
        {
            lock (_dataLock)
            {
                Array.Clear(TargetTempData, 0, BufferSize);
                Array.Clear(CurrentTempData, 0, BufferSize);
                Array.Clear(AdcTempData, 0, BufferSize);
                Array.Clear(AvgFilterTempData, 0, BufferSize);
                Array.Clear(AvgPowerData, 0, BufferSize);
                Array.Clear(HeaterResData, 0, BufferSize);
                Array.Clear(DutyData, 0, BufferSize);
                Array.Clear(PowerData, 0, BufferSize);
                Array.Clear(DestReachedData, 0, BufferSize);
                Array.Clear(WsDeltaData, 0, BufferSize);

                CurrentIndex = 0;
                SampleCount = 0;

                foreach (var series in SeriesList)
                {
                    series.CurrentValue = 0;
                }
            }

            ChartRenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _connectionService.TelemetryReceived -= OnTelemetryReceived;
        }
    }
}
