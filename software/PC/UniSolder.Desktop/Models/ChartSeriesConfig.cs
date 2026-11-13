using CommunityToolkit.Mvvm.ComponentModel;

namespace UniSolder.Desktop.Models
{
    public partial class ChartSeriesConfig : ObservableObject
    {
        public string Key { get; }
        public string Name { get; }
        public string HexColor { get; }
        public string Unit { get; }

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private double _currentValue;

        public ChartSeriesConfig(string key, string name, string hexColor, string unit = "°C", bool isVisible = true)
        {
            Key = key;
            Name = name;
            HexColor = hexColor;
            Unit = unit;
            IsVisible = isVisible;
        }
    }
}
