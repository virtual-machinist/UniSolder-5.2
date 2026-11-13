using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniSolder.Core;

namespace UniSolder.Desktop.Services
{
    public class FirmwareUpdateService : IFirmwareUpdateService
    {
        private readonly IDeviceConnectionService _connectionService;

        public FirmwareUpdateService(IDeviceConnectionService connectionService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        }

        public async Task FlashFirmwareAsync(string hexFilePath, IProgress<FlashProgress> progress, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hexFilePath) || !File.Exists(hexFilePath))
            {
                var err = new FlashProgress
                {
                    StatusMessage = "HEX file not found.",
                    IsError = true
                };
                progress.Report(err);
                throw new FileNotFoundException("HEX file not found.", hexFilePath);
            }

            var sw = Stopwatch.StartNew();

            await Task.Run(() =>
            {
                progress.Report(new FlashProgress
                {
                    StatusMessage = "Parsing Intel HEX file...",
                    Percentage = 2.0,
                    ElapsedTime = sw.Elapsed
                });

                var hex = new HexFileManager();
                if (!hex.LoadHexFile(hexFilePath) || hex.Records.Count == 0)
                {
                    var err = new FlashProgress
                    {
                        StatusMessage = "Failed to parse Intel HEX file or file contains no records.",
                        IsError = true,
                        ElapsedTime = sw.Elapsed
                    };
                    progress.Report(err);
                    throw new InvalidDataException("Invalid or empty Intel HEX file.");
                }

                progress.Report(new FlashProgress
                {
                    StatusMessage = $"Parsed {hex.Records.Count} records. Connecting to device...",
                    Percentage = 5.0,
                    ElapsedTime = sw.Elapsed
                });

                if (!_connectionService.IsConnected)
                {
                    if (!_connectionService.TryConnect())
                    {
                        var err = new FlashProgress
                        {
                            StatusMessage = "Could not connect to UniSolder device.",
                            IsError = true,
                            ElapsedTime = sw.Elapsed
                        };
                        progress.Report(err);
                        throw new InvalidOperationException("Could not connect to UniSolder device.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Check operating mode
                byte opMode = 0;
                _connectionService.Comm.DevGetOpMode(ref opMode);

                // If in application mode (mode == 1), jump to bootloader
                if (opMode != 2 && opMode != 16)
                {
                    progress.Report(new FlashProgress
                    {
                        StatusMessage = "Requesting device jump to Bootloader...",
                        Percentage = 8.0,
                        ElapsedTime = sw.Elapsed
                    });

                    _connectionService.Comm.AppJumpToBootloader();
                    Thread.Sleep(500);

                    _connectionService.Disconnect();

                    bool reconnected = false;
                    for (int i = 0; i < 20; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Thread.Sleep(400);

                        if (_connectionService.TryConnect())
                        {
                            byte mode = 0;
                            _connectionService.Comm.DevGetOpMode(ref mode);
                            if (mode == 2 || mode == 16)
                            {
                                reconnected = true;
                                break;
                            }
                        }
                    }

                    if (!reconnected && !_connectionService.IsConnected)
                    {
                        var err = new FlashProgress
                        {
                            StatusMessage = "Device re-enumeration in Bootloader mode timed out.",
                            IsError = true,
                            ElapsedTime = sw.Elapsed
                        };
                        progress.Report(err);
                        throw new TimeoutException("Device re-enumeration in Bootloader mode timed out.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Erase Flash
                progress.Report(new FlashProgress
                {
                    StatusMessage = "Erasing device flash memory...",
                    Percentage = 12.0,
                    ElapsedTime = sw.Elapsed
                });

                int eraseResult = _connectionService.Comm.BlEraseFlash();
                if (eraseResult != 0)
                {
                    var err = new FlashProgress
                    {
                        StatusMessage = $"Flash erase failed (error code {eraseResult}).",
                        IsError = true,
                        ElapsedTime = sw.Elapsed
                    };
                    progress.Report(err);
                    throw new InvalidOperationException($"Flash erase failed with code {eraseResult}.");
                }

                progress.Report(new FlashProgress
                {
                    StatusMessage = "Flash erased. Programming firmware chunks...",
                    Percentage = 15.0,
                    ElapsedTime = sw.Elapsed
                });

                // Program Flash records
                for (int i = 0; i < hex.Records.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var record = hex.Records[i];
                    byte[] data = record.Data;
                    int progResult = _connectionService.Comm.BlProgramFlash(record.Address, ref data, 0, (int)record.RecDataLen);

                    if (progResult != 0)
                    {
                        var err = new FlashProgress
                        {
                            StatusMessage = $"Flash write failed at 0x{record.Address:X8} (code: {progResult}).",
                            IsError = true,
                            ElapsedTime = sw.Elapsed
                        };
                        progress.Report(err);
                        throw new InvalidOperationException($"Flash programming failed at address 0x{record.Address:X8} with code {progResult}.");
                    }

                    double pct = 15.0 + (80.0 * (i + 1) / hex.Records.Count);
                    progress.Report(new FlashProgress
                    {
                        StatusMessage = $"Flashing record {i + 1}/{hex.Records.Count} (0x{record.Address:X8})...",
                        Percentage = Math.Round(pct, 1),
                        ElapsedTime = sw.Elapsed
                    });
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Complete Programming
                progress.Report(new FlashProgress
                {
                    StatusMessage = "Finalizing flash programming...",
                    Percentage = 96.0,
                    ElapsedTime = sw.Elapsed
                });

                _connectionService.Comm.BlProgramComplete();

                // Jump to Application
                progress.Report(new FlashProgress
                {
                    StatusMessage = "Restarting device into Application mode...",
                    Percentage = 98.0,
                    ElapsedTime = sw.Elapsed
                });

                _connectionService.Comm.BlJumpToApplication();

                sw.Stop();
                progress.Report(new FlashProgress
                {
                    StatusMessage = $"Firmware flashed successfully in {sw.Elapsed.TotalSeconds:F1}s!",
                    Percentage = 100.0,
                    ElapsedTime = sw.Elapsed,
                    IsComplete = true
                });
            }, cancellationToken);
        }
    }
}
