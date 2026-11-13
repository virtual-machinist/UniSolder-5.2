using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.ViewModels;
using Xunit;

namespace UniSolder.Desktop.Tests.ViewModels
{
    public class FirmwareFlashingViewModelTests
    {
        private string CreateTempHexFile(string content)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"unisolder_vm_test_{Guid.NewGuid():N}.hex");
            File.WriteAllText(tempFile, content);
            return tempFile;
        }

        [Fact]
        public void InitialState_IsReadyToSelectFile()
        {
            var mockFlashService = new Mock<IFirmwareUpdateService>();
            var mockConnection = new Mock<IDeviceConnectionService>();

            using var vm = new FirmwareFlashingViewModel(mockFlashService.Object, mockConnection.Object);

            Assert.Null(vm.HexFilePath);
            Assert.False(vm.IsFlashing);
            Assert.False(vm.CanStartFlash);
            Assert.False(vm.CanCancelFlash);
            Assert.Equal(0, vm.ProgressPercentage);
        }

        [Fact]
        public void LoadHexFile_WithValidFile_SetsPropertiesAndEnablesStartFlash()
        {
            string hexContent = ":10010000214601360121470136007EFE09D2190140\n:00000001FF\n";
            string hexPath = CreateTempHexFile(hexContent);

            try
            {
                var mockFlashService = new Mock<IFirmwareUpdateService>();
                var mockConnection = new Mock<IDeviceConnectionService>();

                using var vm = new FirmwareFlashingViewModel(mockFlashService.Object, mockConnection.Object);

                vm.LoadHexFile(hexPath);

                Assert.Equal(hexPath, vm.HexFilePath);
                Assert.NotNull(vm.HexFileName);
                Assert.True(vm.RecordCount > 0);
                Assert.True(vm.TotalByteCount > 0);
                Assert.True(vm.CanStartFlash);
                Assert.Contains("Ready to flash", vm.StatusMessage);
            }
            finally
            {
                if (File.Exists(hexPath)) File.Delete(hexPath);
            }
        }

        [Fact]
        public async Task StartFlashingAsync_CallsFlashServiceAndUpdatesProgress()
        {
            string hexContent = ":10010000214601360121470136007EFE09D2190140\n:00000001FF\n";
            string hexPath = CreateTempHexFile(hexContent);

            try
            {
                var mockFlashService = new Mock<IFirmwareUpdateService>();
                mockFlashService.Setup(s => s.FlashFirmwareAsync(
                    It.IsAny<string>(),
                    It.IsAny<IProgress<FlashProgress>>(),
                    It.IsAny<CancellationToken>()))
                    .Callback<string, IProgress<FlashProgress>, CancellationToken>((path, prog, token) =>
                    {
                        prog.Report(new FlashProgress { Percentage = 50.0, StatusMessage = "Halfway" });
                        prog.Report(new FlashProgress { Percentage = 100.0, StatusMessage = "Done", IsComplete = true });
                    })
                    .Returns(Task.CompletedTask);

                var mockConnection = new Mock<IDeviceConnectionService>();

                using var vm = new FirmwareFlashingViewModel(mockFlashService.Object, mockConnection.Object);
                vm.LoadHexFile(hexPath);

                await vm.StartFlashingAsync();

                Assert.False(vm.IsFlashing);
                Assert.Equal(100.0, vm.ProgressPercentage);
                Assert.Contains("successfully", vm.StatusMessage);
                Assert.Contains("Completed Successfully", vm.LogOutput);
            }
            finally
            {
                if (File.Exists(hexPath)) File.Delete(hexPath);
            }
        }

        [Fact]
        public void ClearLog_EmptiesLogOutput()
        {
            var mockFlashService = new Mock<IFirmwareUpdateService>();
            var mockConnection = new Mock<IDeviceConnectionService>();

            using var vm = new FirmwareFlashingViewModel(mockFlashService.Object, mockConnection.Object);
            vm.LoadHexFile("/dummy/nonexistent.hex");
            Assert.NotEmpty(vm.LogOutput);

            vm.ClearLogCommand.Execute(null);
            Assert.Empty(vm.LogOutput);
        }
    }
}
