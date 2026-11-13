using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniSolder.Desktop.Services;

namespace UniSolder.Desktop.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IDeviceConnectionService _connectionService;

        public IDeviceConnectionService ConnectionService => _connectionService;
        public TelemetryChartViewModel ChartViewModel { get; }
        public PidTuningViewModel PidViewModel { get; }
        public FirmwareFlashingViewModel FlashViewModel { get; }

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _connectionStatusText = "Disconnected";

        [ObservableProperty]
        private string _operatingModeText = "Mode: Unknown";

        [ObservableProperty]
        private string _deviceInfoText = "VID: 0x04D8 | PID: 0x003C";

        [ObservableProperty]
        private string _packetStatsText = "TX: 0 B | RX: 0 B";

        [ObservableProperty]
        private bool _isLiveStreamActive = true;

        [ObservableProperty]
        private int _selectedTabIndex;

        public MainWindowViewModel(
            IDeviceConnectionService connectionService,
            TelemetryChartViewModel chartViewModel,
            PidTuningViewModel pidViewModel,
            FirmwareFlashingViewModel flashViewModel)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            ChartViewModel = chartViewModel ?? throw new ArgumentNullException(nameof(chartViewModel));
            PidViewModel = pidViewModel ?? throw new ArgumentNullException(nameof(pidViewModel));
            FlashViewModel = flashViewModel ?? throw new ArgumentNullException(nameof(flashViewModel));

            _connectionService.ConnectionChanged += OnConnectionChanged;
            _connectionService.OperatingModeChanged += OnOperatingModeChanged;

            UpdateStatus();
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            IsConnected = connected;
            UpdateStatus();
        }

        private void OnOperatingModeChanged(object? sender, DeviceOperatingMode mode)
        {
            UpdateStatus();
        }

        public void UpdateStatus()
        {
            IsConnected = _connectionService.IsConnected;
            ConnectionStatusText = IsConnected ? "Connected" : "Disconnected";
            OperatingModeText = $"Mode: {_connectionService.OperatingMode}";

            if (IsConnected && (_connectionService.FirmwareVersion > 0 || _connectionService.BootloaderVersion > 0))
            {
                DeviceInfoText = $"VID: 0x04D8 | PID: 0x003C | FW: v{_connectionService.FirmwareVersion / 10}.{_connectionService.FirmwareVersion % 10} | BL: v{_connectionService.BootloaderVersion / 10}.{_connectionService.BootloaderVersion % 10}";
            }
            else
            {
                DeviceInfoText = "VID: 0x04D8 | PID: 0x003C";
            }

            PacketStatsText = $"TX: {_connectionService.BytesSent} B | RX: {_connectionService.BytesReceived} B";
        }

        [RelayCommand]
        public void ToggleConnection()
        {
            if (_connectionService.IsConnected)
            {
                _connectionService.Disconnect();
            }
            else
            {
                _connectionService.TryConnect();
            }
            UpdateStatus();
        }

        [RelayCommand]
        public void ToggleLiveStream()
        {
            IsLiveStreamActive = !IsLiveStreamActive;
        }

        [RelayCommand]
        public async Task RefreshDeviceInfoAsync()
        {
            await _connectionService.RefreshDeviceInfoAsync();
            UpdateStatus();
        }
    }
}
