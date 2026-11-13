using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UniSolder.Desktop.ViewModels;

namespace UniSolder.Desktop.Views
{
    public partial class FirmwareFlashingDialog : UserControl
    {
        public FirmwareFlashingDialog()
        {
            InitializeComponent();
        }

        private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Firmware Intel HEX File",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("HEX Files (*.hex)")
                    {
                        Patterns = new[] { "*.hex", "*.HEX" }
                    },
                    new FilePickerFileType("All Files (*.*)")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files != null && files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                if (DataContext is FirmwareFlashingViewModel vm)
                {
                    vm.LoadHexFile(path);
                }
            }
        }
    }
}
