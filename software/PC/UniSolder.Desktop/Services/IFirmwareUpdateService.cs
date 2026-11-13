using System;
using System.Threading;
using System.Threading.Tasks;

namespace UniSolder.Desktop.Services
{
    public class FlashProgress
    {
        public string StatusMessage { get; set; } = string.Empty;
        public double Percentage { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public bool IsComplete { get; set; }
        public bool IsError { get; set; }
    }

    public interface IFirmwareUpdateService
    {
        Task FlashFirmwareAsync(string hexFilePath, IProgress<FlashProgress> progress, CancellationToken cancellationToken = default);
    }
}
