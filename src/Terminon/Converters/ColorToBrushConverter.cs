using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Terminon.Converters;

[ValueConversion(typeof(Color), typeof(SolidColorBrush))]
public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SolidColorBrush b ? b.Color : Colors.Transparent;
}

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Enum.Parse(targetType, parameter?.ToString() ?? string.Empty);
        return Binding.DoNothing;
    }
}
