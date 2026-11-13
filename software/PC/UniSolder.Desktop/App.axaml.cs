using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UniSolder.Desktop.Services;
using UniSolder.Desktop.ViewModels;
using UniSolder.Desktop.Views;

namespace UniSolder.Desktop
{
    public partial class App : Application
    {
        private IDeviceConnectionService? _connectionService;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _connectionService = new DeviceConnectionService();
                var chartVm = new TelemetryChartViewModel(_connectionService);
                var pidVm = new PidTuningViewModel(_connectionService);
                var flashService = new FirmwareUpdateService(_connectionService);
                var flashVm = new FirmwareFlashingViewModel(flashService, _connectionService);

                var mainVm = new MainWindowViewModel(_connectionService, chartVm, pidVm, flashVm);

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm
                };

                _ = _connectionService.StartMonitoringAsync();

                desktop.Exit += (sender, args) =>
                {
                    _connectionService.Dispose();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
