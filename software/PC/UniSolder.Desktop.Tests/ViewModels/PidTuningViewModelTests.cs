using System;
using Moq;
using UniSolder.Core;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.ViewModels;
using Xunit;

namespace UniSolder.Desktop.Tests.ViewModels
{
    public class PidTuningViewModelTests
    {
        [Fact]
        public void InitialState_HasExpectedDefaults()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.Comm).Returns(new UniSolderComm());

            using var vm = new PidTuningViewModel(mockService.Object);

            Assert.Equal(16384, vm.RawKp);
            Assert.Equal(0.5, vm.ScaledKp, 2);
            Assert.Equal(3276, vm.RawKi);
            Assert.Equal(0.1, vm.ScaledKi, 2);
            Assert.Equal(10, vm.DGain);
            Assert.Equal(5, vm.OvsGain);
            Assert.Equal(128, vm.Gain);
        }

        [Fact]
        public void ScaledKp_ClampsAndConvertsAccurately()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.Comm).Returns(new UniSolderComm());

            using var vm = new PidTuningViewModel(mockService.Object);

            vm.ScaledKp = 0.25;
            Assert.Equal(8192, vm.RawKp);
            Assert.Equal(0.25, vm.ScaledKp, 2);

            vm.ScaledKp = 1.5; // Above max
            Assert.Equal(32767, vm.RawKp);
            Assert.Equal(1.0, vm.ScaledKp, 2);

            vm.ScaledKp = -0.1; // Below min
            Assert.Equal(0, vm.RawKp);
            Assert.Equal(0.0, vm.ScaledKp, 2);
        }

        [Fact]
        public void ScaledKi_ClampsAndConvertsAccurately()
        {
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.Comm).Returns(new UniSolderComm());

            using var vm = new PidTuningViewModel(mockService.Object);

            vm.ScaledKi = 0.050;
            Assert.Equal((ushort)Math.Round(0.050 * 32767.0), vm.RawKi);
            Assert.Equal(0.050, vm.ScaledKi, 3);
        }

        [Fact]
        public void WriteParametersCommand_SendsPIDToComm()
        {
            var transport = new UsbHidTransport();
            var stream = new System.IO.MemoryStream();
            transport.AttachStream(stream);

            var comm = new UniSolderComm { Transport = transport };
            var mockService = new Mock<IDeviceConnectionService>();
            mockService.Setup(s => s.Comm).Returns(comm);
            mockService.Setup(s => s.IsConnected).Returns(true);

            using var vm = new PidTuningViewModel(mockService.Object);
            vm.RawKp = 10000;
            vm.RawKi = 2000;
            vm.DGain = 15;
            vm.OvsGain = 8;
            vm.Gain = 200;

            vm.WriteParametersCommand.Execute(null);

            Assert.Contains("written", vm.StatusMessage);
            Assert.True(transport.BytesSent() > 0);
        }
    }
}
