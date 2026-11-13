using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniSolder.Core;
using UniSolder.Desktop.Services;

namespace UniSolder.Desktop.ViewModels
{
    public partial class FirmwareFlashingViewModel : ViewModelBase, IDisposable
    {
        private readonly IFirmwareUpdateService _firmwareUpdateService;
        private readonly IDeviceConnectionService _connectionService;
        private CancellationTokenSource? _flashCts;
        private readonly StringBuilder _logBuilder = new StringBuilder();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartFlash))]
        private string? _hexFilePath;

        [ObservableProperty]
        private string? _hexFileName;

        [ObservableProperty]
        private int _recordCount;

        [ObservableProperty]
        private long _totalByteCount;

        [ObservableProperty]
        private double _progressPercentage;

        [ObservableProperty]
        private string _statusMessage = "Select a firmware .hex file to begin.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartFlash))]
        [NotifyPropertyChangedFor(nameof(CanCancelFlash))]
        private bool _isFlashing;

        [ObservableProperty]
        private string _logOutput = string.Empty;

        public bool CanStartFlash => !IsFlashing && !string.IsNullOrWhiteSpace(HexFilePath) && File.Exists(HexFilePath);
        public bool CanCancelFlash => IsFlashing;

        public FirmwareFlashingViewModel(IFirmwareUpdateService firmwareUpdateService, IDeviceConnectionService connectionService)
        {
            _firmwareUpdateService = firmwareUpdateService ?? throw new ArgumentNullException(nameof(firmwareUpdateService));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        }

        public void LoadHexFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                StatusMessage = "File does not exist.";
                AppendLog($"[ERROR] File does not exist: {filePath}");
                return;
            }

            try
            {
                var hex = new HexFileManager();
                if (hex.LoadHexFile(filePath))
                {
                    HexFilePath = filePath;
                    HexFileName = Path.GetFileName(filePath);
                    RecordCount = hex.Records.Count;
                    TotalByteCount = hex.Records.Sum(r => (long)r.RecDataLen);

                    StatusMessage = $"Loaded {HexFileName}: {RecordCount} records ({TotalByteCount:N0} bytes). Ready to flash.";
                    AppendLog($"[HEX] Loaded '{HexFileName}' with {RecordCount} records ({TotalByteCount:N0} bytes).");
                }
                else
                {
                    StatusMessage = "Failed to parse HEX file checksum or structure.";
                    AppendLog($"[ERROR] Failed to parse HEX file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"[ERROR] {ex.Message}");
            }
        }

        [RelayCommand]
        public async Task StartFlashingAsync()
        {
            if (string.IsNullOrWhiteSpace(HexFilePath) || !File.Exists(HexFilePath))
            {
                StatusMessage = "Please select a valid HEX file.";
                return;
            }

            IsFlashing = true;
            ProgressPercentage = 0.0;
            _flashCts = new CancellationTokenSource();

            AppendLog($"--- Starting Firmware Flashing at {DateTime.Now:HH:mm:ss} ---");

            var progress = new Progress<FlashProgress>(p =>
            {
                ProgressPercentage = p.Percentage;
                StatusMessage = p.StatusMessage;

                if (!string.IsNullOrEmpty(p.StatusMessage))
                {
                    AppendLog($"[{p.Percentage:0}%] {p.StatusMessage}");
                }
            });

            try
            {
                await _firmwareUpdateService.FlashFirmwareAsync(HexFilePath, progress, _flashCts.Token);
                StatusMessage = "Firmware updated successfully!";
                AppendLog($"--- Flashing Completed Successfully at {DateTime.Now:HH:mm:ss} ---");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Firmware flashing was cancelled by user.";
                AppendLog("[ABORT] Flashing cancelled by user.");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Flashing failed: {ex.Message}";
                AppendLog($"[ERROR] Flashing failed: {ex.Message}");
            }
            finally
            {
                IsFlashing = false;
                _flashCts?.Dispose();
                _flashCts = null;
            }
        }

        [RelayCommand]
        public void CancelFlashing()
        {
            if (IsFlashing && _flashCts != null)
            {
                _flashCts.Cancel();
                StatusMessage = "Cancelling flash operation...";
            }
        }

        [RelayCommand]
        public void ClearLog()
        {
            _logBuilder.Clear();
            LogOutput = string.Empty;
        }

        private void AppendLog(string message)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogOutput = _logBuilder.ToString();
        }

        public void Dispose()
        {
            _flashCts?.Cancel();
            _flashCts?.Dispose();
        }
    }
}
