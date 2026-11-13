using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UniSolder.Desktop.Converters
{
    public class ConnectionStatusBrushConverter : IValueConverter
    {
        public static readonly ConnectionStatusBrushConverter Instance = new ConnectionStatusBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isConnected && isConnected)
            {
                return new SolidColorBrush(Color.Parse("#10B981")); // Emerald Green
            }
            return new SolidColorBrush(Color.Parse("#EF4444")); // Red / Inactive
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
