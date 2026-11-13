using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniSolder.Core;
using UniSolder.Desktop.Services;

namespace UniSolder.Desktop.ViewModels
{
    public partial class PidTuningViewModel : ViewModelBase, IDisposable
    {
        private readonly IDeviceConnectionService _connectionService;
        private CancellationTokenSource? _debounceCts;
        private bool _isUpdatingFromDevice;

        [ObservableProperty]
        private ushort _rawKp = 16384; // ~0.50

        [ObservableProperty]
        private ushort _rawKi = 3276;  // ~0.100

        [ObservableProperty]
        private byte _dGain = 10;

        [ObservableProperty]
        private byte _ovsGain = 5;

        [ObservableProperty]
        private ushort _gain = 128;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isAutoSyncEnabled = true;

        public double ScaledKp
        {
            get => Math.Round(RawKp / 32767.0, 3);
            set
            {
                var clamped = Math.Clamp(value, 0.0, 1.0);
                RawKp = (ushort)Math.Round(clamped * 32767.0);
            }
        }

        public double ScaledKi
        {
            get => Math.Round(RawKi / 32767.0, 4);
            set
            {
                var clamped = Math.Clamp(value, 0.0, 1.0);
                RawKi = (ushort)Math.Round(clamped * 32767.0);
            }
        }

        public PidTuningViewModel(IDeviceConnectionService connectionService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));

            _connectionService.ConnectionChanged += OnConnectionChanged;
            if (_connectionService.Comm != null)
            {
                _connectionService.Comm.InstrumentChange += OnInstrumentChange;
            }

            if (_connectionService.IsConnected)
            {
                ReadParameters();
            }
        }

        private void OnConnectionChanged(object? sender, bool connected)
        {
            if (connected)
            {
                ReadParameters();
            }
            else
            {
                StatusMessage = "Device disconnected";
            }
        }

        private void OnInstrumentChange(object? sender, EventArgs e)
        {
            ReadParameters();
        }

        [RelayCommand]
        public void ReadParameters()
        {
            if (!_connectionService.IsConnected)
            {
                StatusMessage = "Cannot read: Device disconnected";
                return;
            }

            try
            {
                _isUpdatingFromDevice = true;
                var pid = _connectionService.Comm.AppGetPIDParameters();
                if (pid != null)
                {
                    RawKp = pid.KP;
                    RawKi = pid.KI;
                    DGain = pid.DGain;
                    OvsGain = pid.OVSGain;
                    Gain = pid.Gain;

                    OnPropertyChanged(nameof(ScaledKp));
                    OnPropertyChanged(nameof(ScaledKi));

                    StatusMessage = $"Parameters read at {DateTime.Now:HH:mm:ss}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Read error: {ex.Message}";
            }
            finally
            {
                _isUpdatingFromDevice = false;
            }
        }

        [RelayCommand]
        public void WriteParameters()
        {
            if (!_connectionService.IsConnected)
            {
                StatusMessage = "Cannot write: Device disconnected";
                return;
            }

            try
            {
                var pid = new UniSolderComm.PID
                {
                    KP = RawKp,
                    KI = RawKi,
                    DGain = DGain,
                    OVSGain = OvsGain,
                    Gain = Gain
                };

                _connectionService.Comm.AppSetPIDParameters(ref pid);
                StatusMessage = $"Parameters written at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Write error: {ex.Message}";
            }
        }

        partial void OnRawKpChanged(ushort value)
        {
            OnPropertyChanged(nameof(ScaledKp));
            ScheduleDebouncedWrite();
        }

        partial void OnRawKiChanged(ushort value)
        {
            OnPropertyChanged(nameof(ScaledKi));
            ScheduleDebouncedWrite();
        }

        partial void OnDGainChanged(byte value)
        {
            ScheduleDebouncedWrite();
        }

        partial void OnOvsGainChanged(byte value)
        {
            ScheduleDebouncedWrite();
        }

        partial void OnGainChanged(ushort value)
        {
            ScheduleDebouncedWrite();
        }

        private void ScheduleDebouncedWrite()
        {
            if (_isUpdatingFromDevice || !IsAutoSyncEnabled || !_connectionService.IsConnected) return;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150, token);
                    if (!token.IsCancellationRequested)
                    {
                        WriteParameters();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounce superseded by newer slider change
                }
            }, token);
        }

        public void Dispose()
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _connectionService.ConnectionChanged -= OnConnectionChanged;
            if (_connectionService.Comm != null)
            {
                _connectionService.Comm.InstrumentChange -= OnInstrumentChange;
            }
        }
    }
}
