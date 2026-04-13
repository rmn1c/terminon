using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Terminon.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value is bool b && b;
        bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        if (invert) v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility vis && vis == Visibility.Visible;
}

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value is null;
        bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        bool show = invert ? isNull : !isNull;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
