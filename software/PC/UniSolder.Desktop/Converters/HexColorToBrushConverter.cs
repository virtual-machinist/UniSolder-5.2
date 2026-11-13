using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UniSolder.Desktop.Converters
{
    public class HexColorToBrushConverter : IValueConverter
    {
        public static readonly HexColorToBrushConverter Instance = new HexColorToBrushConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrEmpty(hex))
            {
                try
                {
                    return new SolidColorBrush(Color.Parse(hex));
                }
                catch
                {
                    // Fallback to white on parse error
                }
            }
            return new SolidColorBrush(Colors.White);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
