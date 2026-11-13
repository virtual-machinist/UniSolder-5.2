using System;
using System.Threading;
using System.Threading.Tasks;
using UniSolder.Core;

namespace UniSolder.Desktop.Services
{
    public class DeviceConnectionService : IDeviceConnectionService
    {
        private readonly IUniComm _transport;
        private readonly UniSolderComm _comm;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;
        private bool _isConnected;
        private DeviceOperatingMode _operatingMode = DeviceOperatingMode.Unknown;
        private byte _firmwareVersion;
        private byte _bootloaderVersion;

        public bool IsConnected => _isConnected && _transport.Connected;
        public DeviceOperatingMode OperatingMode => _operatingMode;
        public byte FirmwareVersion => _firmwareVersion;
        public byte BootloaderVersion => _bootloaderVersion;
        public int BytesSent => _transport.BytesSent();
        public int BytesReceived => _transport.BytesReceived();
        public UniSolderComm Comm => _comm;
        public IUniComm Transport => _transport;

        public event EventHandler<bool>? ConnectionChanged;
        public event EventHandler<TelemetryData>? TelemetryReceived;
        public event EventHandler<DeviceOperatingMode>? OperatingModeChanged;

        public DeviceConnectionService(IUniComm? transport = null, UniSolderComm? comm = null)
        {
            _transport = transport ?? new UsbHidTransport();
            _comm = comm ?? new UniSolderComm();
            _comm.Transport = _transport;
            _comm.LiveDataReceived += OnLiveDataReceived;
        }

        private void OnLiveDataReceived(object? sender, UniSolderComm.LiveDataReceivedEventData e)
        {
            var telemetry = e.Telemetry;
            if (telemetry != null)
            {
                TelemetryReceived?.Invoke(this, telemetry);
            }
        }

        public bool TryConnect()
        {
            if (_transport.Connected)
            {
                UpdateConnectionState(true);
                return true;
            }

            bool connected = _transport.Connect();
            if (connected)
            {
                _comm.Init();
                UpdateConnectionState(true);
                _ = RefreshDeviceInfoAsync();
            }
            else
            {
                UpdateConnectionState(false);
            }

            return connected;
        }

        public void Disconnect()
        {
            if (_transport.Connected)
            {
                _transport.Disconnect();
            }
            UpdateConnectionState(false);
        }

        public async Task<bool> RefreshDeviceInfoAsync()
        {
            if (!_transport.Connected) return false;

            return await Task.Run(() =>
            {
                uint devId = 0;
                byte verMaj = 0, verMin = 0;
                int infoResult = _comm.DevGetInfo(ref devId, ref verMaj, ref verMin);
                if (infoResult == 0)
                {
                    _firmwareVersion = (byte)(verMaj * 10 + verMin);

                    byte opMode = 0;
                    if (_comm.DevGetOpMode(ref opMode) == 0)
                    {
                        var mode = opMode switch
                        {
                            1 => DeviceOperatingMode.Application,
                            2 => DeviceOperatingMode.Bootloader,
                            _ => DeviceOperatingMode.Unknown
                        };

                        if (_operatingMode != mode)
                        {
                            _operatingMode = mode;
                            OperatingModeChanged?.Invoke(this, mode);
                        }
                    }
                    return true;
                }
                return false;
            });
        }

        public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
        {
            StopMonitoring();

            _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _monitoringCts.Token;

            _monitoringTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!_transport.Connected)
                        {
                            TryConnect();
                        }
                        else
                        {
                            // If transport was disconnected under the hood
                            if (_isConnected && !_transport.Connected)
                            {
                                UpdateConnectionState(false);
                            }
                        }

                        await Task.Delay(1000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Ignore periodic polling errors
                    }
                }
            }, token);

            return Task.CompletedTask;
        }

        public void StopMonitoring()
        {
            if (_monitoringCts != null)
            {
                _monitoringCts.Cancel();
                _monitoringCts.Dispose();
                _monitoringCts = null;
            }
            _monitoringTask = null;
        }

        private void UpdateConnectionState(bool connected)
        {
            if (_isConnected != connected)
            {
                _isConnected = connected;
                if (!connected)
                {
                    _operatingMode = DeviceOperatingMode.Unknown;
                    _firmwareVersion = 0;
                    _bootloaderVersion = 0;
                }
                ConnectionChanged?.Invoke(this, connected);
            }
        }

        public void Dispose()
        {
            StopMonitoring();
            _comm.LiveDataReceived -= OnLiveDataReceived;
            _transport.Dispose();
        }
    }
}
