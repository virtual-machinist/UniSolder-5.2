using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using UniSolder.Core;
using UniSolder.Desktop.Services;
using Xunit;

namespace UniSolder.Desktop.Tests.Services
{
    public class DeviceConnectionServiceTests
    {
        [Fact]
        public void InitialState_IsDisconnected()
        {
            var mockTransport = new Mock<IUniComm>();
            mockTransport.SetupGet(t => t.Connected).Returns(false);

            using var service = new DeviceConnectionService(mockTransport.Object);

            Assert.False(service.IsConnected);
            Assert.Equal(DeviceOperatingMode.Unknown, service.OperatingMode);
            Assert.Equal(0, service.FirmwareVersion);
            Assert.Equal(0, service.BootloaderVersion);
        }

        [Fact]
        public void TryConnect_WhenTransportFails_ReturnsFalseAndStaysDisconnected()
        {
            var mockTransport = new Mock<IUniComm>();
            mockTransport.Setup(t => t.Connect()).Returns(false);
            mockTransport.SetupGet(t => t.Connected).Returns(false);

            using var service = new DeviceConnectionService(mockTransport.Object);

            bool connectionChangedFired = false;
            service.ConnectionChanged += (s, e) => connectionChangedFired = true;

            bool result = service.TryConnect();

            Assert.False(result);
            Assert.False(service.IsConnected);
            Assert.False(connectionChangedFired);
        }

        [Fact]
        public void TryConnect_WhenTransportSucceeds_UpdatesConnectedStateAndFiresEvent()
        {
            var mockTransport = new Mock<IUniComm>();
            mockTransport.Setup(t => t.Connect()).Returns(true);
            mockTransport.SetupGet(t => t.Connected).Returns(true);

            using var service = new DeviceConnectionService(mockTransport.Object);

            bool? connectedArg = null;
            service.ConnectionChanged += (s, e) => connectedArg = e;

            bool result = service.TryConnect();

            Assert.True(result);
            Assert.True(service.IsConnected);
            Assert.True(connectedArg);
        }

        [Fact]
        public void Disconnect_CallsTransportDisconnectAndUpdatesState()
        {
            var mockTransport = new Mock<IUniComm>();
            bool connected = true;
            mockTransport.Setup(t => t.Connect()).Returns(() => { connected = true; return true; });
            mockTransport.Setup(t => t.Disconnect()).Callback(() => connected = false);
            mockTransport.SetupGet(t => t.Connected).Returns(() => connected);

            using var service = new DeviceConnectionService(mockTransport.Object);
            service.TryConnect();
            Assert.True(service.IsConnected);

            bool? lastState = null;
            service.ConnectionChanged += (s, e) => lastState = e;

            service.Disconnect();

            mockTransport.Verify(t => t.Disconnect(), Times.Once);
            Assert.False(service.IsConnected);
            Assert.False(lastState);
        }

        [Fact]
        public void TelemetryReceived_EventFiresWhenCommReceivesLiveData()
        {
            var transport = new UsbHidTransport();
            var comm = new UniSolderComm { Transport = transport };
            using var service = new DeviceConnectionService(transport, comm);

            TelemetryData? received = null;
            service.TelemetryReceived += (s, data) => received = data;

            // Prepare a mock telemetry payload of 34 bytes (data[0] == 3)
            byte[] packet = new byte[34];
            packet[0] = 3;
            packet[3] = 12; // ColdJunctionTemp = 24.0f
            packet[4] = 100;
            packet[5] = 2; // TipTemp = (100 * 0.5) + (2 * 128) = 50 + 256 = 306.0f
            packet[32] = 128; // Duty = (128 + 0) / 128.0 = 1.0f

            var stream = new MemoryStream();
            transport.AttachStream(stream);

            // Trigger event via ProcessReceivedBytes
            comm.ProcessReceivedBytes(packet);

            Assert.NotNull(received);
            Assert.Equal(24.0f, received.ColdJunctionTemp);
            Assert.Equal(306.0f, received.TipTemp);
            Assert.Equal(1.0f, received.Duty);
        }
    }
}
