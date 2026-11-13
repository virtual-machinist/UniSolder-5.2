using System;
using Moq;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.ViewModels;
using Xunit;

namespace UniSolder.Desktop.Tests.ViewModels
{
    public class MainWindowViewModelTests
    {
        [Fact]
        public void InitialState_ReflectsDisconnectedService()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.IsConnected).Returns(false);
            mockService.Setup(s => s.OperatingMode).Returns(DeviceOperatingMode.Unknown);

            var chartVm = new TelemetryChartViewModel(mockService.Object);
            var pidVm = new PidTuningViewModel(mockService.Object);
            var flashService = new Mock<IFirmwareUpdateService>();
            var flashVm = new FirmwareFlashingViewModel(flashService.Object, mockService.Object);

            var mainVm = new MainWindowViewModel(mockService.Object, chartVm, pidVm, flashVm);

            Assert.False(mainVm.IsConnected);
            Assert.Equal("Disconnected", mainVm.ConnectionStatusText);
            Assert.Equal("Mode: Unknown", mainVm.OperatingModeText);
            Assert.True(mainVm.IsLiveStreamActive);
        }

        [Fact]
        public void ToggleConnectionCommand_WhenDisconnected_CallsTryConnect()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.IsConnected).Returns(false);

            var chartVm = new TelemetryChartViewModel(mockService.Object);
            var pidVm = new PidTuningViewModel(mockService.Object);
            var flashService = new Mock<IFirmwareUpdateService>();
            var flashVm = new FirmwareFlashingViewModel(flashService.Object, mockService.Object);

            var mainVm = new MainWindowViewModel(mockService.Object, chartVm, pidVm, flashVm);

            mainVm.ToggleConnectionCommand.Execute(null);

            mockService.Verify(s => s.TryConnect(), Times.Once);
        }

        [Fact]
        public void ToggleConnectionCommand_WhenConnected_CallsDisconnect()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.IsConnected).Returns(true);

            var chartVm = new TelemetryChartViewModel(mockService.Object);
            var pidVm = new PidTuningViewModel(mockService.Object);
            var flashService = new Mock<IFirmwareUpdateService>();
            var flashVm = new FirmwareFlashingViewModel(flashService.Object, mockService.Object);

            var mainVm = new MainWindowViewModel(mockService.Object, chartVm, pidVm, flashVm);

            mainVm.ToggleConnectionCommand.Execute(null);

            mockService.Verify(s => s.Disconnect(), Times.Once);
        }

        [Fact]
        public void ToggleLiveStreamCommand_InvertsLiveStreamState()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            var chartVm = new TelemetryChartViewModel(mockService.Object);
            var pidVm = new PidTuningViewModel(mockService.Object);
            var flashService = new Mock<IFirmwareUpdateService>();
            var flashVm = new FirmwareFlashingViewModel(flashService.Object, mockService.Object);

            var mainVm = new MainWindowViewModel(mockService.Object, chartVm, pidVm, flashVm);

            Assert.True(mainVm.IsLiveStreamActive);
            mainVm.ToggleLiveStreamCommand.Execute(null);
            Assert.False(mainVm.IsLiveStreamActive);
            mainVm.ToggleLiveStreamCommand.Execute(null);
            Assert.True(mainVm.IsLiveStreamActive);
        }

        [Fact]
        public void ConnectionChangedEvent_UpdatesViewModelProperties()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            bool isConnected = false;
            mockService.Setup(s => s.IsConnected).Returns(() => isConnected);
            mockService.Setup(s => s.OperatingMode).Returns(DeviceOperatingMode.Application);
            mockService.Setup(s => s.FirmwareVersion).Returns(52); // v5.2
            mockService.Setup(s => s.BootloaderVersion).Returns(20); // v2.0

            var chartVm = new TelemetryChartViewModel(mockService.Object);
            var pidVm = new PidTuningViewModel(mockService.Object);
            var flashService = new Mock<IFirmwareUpdateService>();
            var flashVm = new FirmwareFlashingViewModel(flashService.Object, mockService.Object);

            var mainVm = new MainWindowViewModel(mockService.Object, chartVm, pidVm, flashVm);

            // Simulate connection event
            isConnected = true;
            mockService.Raise(s => s.ConnectionChanged += null, mockService.Object, true);

            Assert.True(mainVm.IsConnected);
            Assert.Equal("Connected", mainVm.ConnectionStatusText);
            Assert.Equal("Mode: Application", mainVm.OperatingModeText);
            Assert.Contains("FW: v5.2", mainVm.DeviceInfoText);
            Assert.Contains("BL: v2.0", mainVm.DeviceInfoText);
        }
    }
}
