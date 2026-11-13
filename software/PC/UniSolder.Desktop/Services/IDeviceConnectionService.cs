using System;
using System.Threading;
using System.Threading.Tasks;
using UniSolder.Core;

namespace UniSolder.Desktop.Services
{
    public enum DeviceOperatingMode
    {
        Unknown = 0,
        Application = 1,
        Bootloader = 2
    }

    public interface IDeviceConnectionService : IDisposable
    {
        bool IsConnected { get; }
        DeviceOperatingMode OperatingMode { get; }
        byte FirmwareVersion { get; }
        byte BootloaderVersion { get; }
        int BytesSent { get; }
        int BytesReceived { get; }
        UniSolderComm Comm { get; }
        IUniComm Transport { get; }

        event EventHandler<bool>? ConnectionChanged;
        event EventHandler<TelemetryData>? TelemetryReceived;
        event EventHandler<DeviceOperatingMode>? OperatingModeChanged;

        bool TryConnect();
        void Disconnect();
        Task StartMonitoringAsync(CancellationToken cancellationToken = default);
        void StopMonitoring();
        Task<bool> RefreshDeviceInfoAsync();
    }
}
