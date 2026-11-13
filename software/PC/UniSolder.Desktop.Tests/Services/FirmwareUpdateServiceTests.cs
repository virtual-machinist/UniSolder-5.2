using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using UniSolder.Core;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.Tests.Helpers;
using Xunit;

namespace UniSolder.Desktop.Tests.Services
{
    public class FirmwareUpdateServiceTests
    {
        private string CreateTempHexFile(string content)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"unisolder_test_{Guid.NewGuid():N}.hex");
            File.WriteAllText(tempFile, content);
            return tempFile;
        }

        [Fact]
        public async Task FlashFirmwareAsync_WhenFileDoesNotExist_ThrowsFileNotFoundException()
        {
            var mockConnection = new Mock<IDeviceConnectionService>();
            var service = new FirmwareUpdateService(mockConnection.Object);
            var progress = new Mock<IProgress<FlashProgress>>();

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                service.FlashFirmwareAsync("/non/existent/path/firmware.hex", progress.Object));
        }

        [Fact]
        public async Task FlashFirmwareAsync_WhenFileIsInvalidHex_ThrowsInvalidDataException()
        {
            string hexPath = CreateTempHexFile("INVALID HEX CONTENT WITHOUT START CODES");
            try
            {
                var mockConnection = new Mock<IDeviceConnectionService>();
                var service = new FirmwareUpdateService(mockConnection.Object);
                var progress = new Mock<IProgress<FlashProgress>>();

                await Assert.ThrowsAsync<InvalidDataException>(() =>
                    service.FlashFirmwareAsync(hexPath, progress.Object));
            }
            finally
            {
                if (File.Exists(hexPath)) File.Delete(hexPath);
            }
        }

        [Fact]
        public async Task FlashFirmwareAsync_ExecutesFullProgrammingSequence()
        {
            // Valid Intel HEX record: :10010000214601360121470136007EFE09D2190140
            // Followed by EOF: :00000001FF
            string hexContent = ":10010000214601360121470136007EFE09D2190140\n:00000001FF\n";
            string hexPath = CreateTempHexFile(hexContent);

            try
            {
                var mockTransport = new MockUniComm();
                mockTransport.OnWriteResponder = written =>
                {
                    byte cmd = written[0];
                    byte[] rx = new byte[64];
                    rx[0] = cmd;
                    rx[1] = 0; // status OK

                    if (cmd == (byte)UniSolderComm.Commands.DEV_GET_OPERATING_MODE)
                    {
                        rx[1] = 2; // Bootloader mode
                    }

                    return rx;
                };

                var comm = new UniSolderComm { Transport = mockTransport };
                var mockConnection = new Mock<IDeviceConnectionService>();
                mockConnection.Setup(s => s.Comm).Returns(comm);
                mockConnection.Setup(s => s.IsConnected).Returns(true);
                mockConnection.Setup(s => s.TryConnect()).Returns(true);

                var service = new FirmwareUpdateService(mockConnection.Object);

                FlashProgress? lastProgress = null;
                var progress = new Progress<FlashProgress>(p => lastProgress = p);

                await service.FlashFirmwareAsync(hexPath, progress);

                Assert.NotNull(lastProgress);
                Assert.True(lastProgress.IsComplete);
                Assert.Equal(100.0, lastProgress.Percentage);
                Assert.Contains("successfully", lastProgress.StatusMessage);
            }
            finally
            {
                if (File.Exists(hexPath)) File.Delete(hexPath);
            }
        }
    }
}
