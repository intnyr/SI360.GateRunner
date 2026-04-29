using System.Globalization;
using System.Windows.Data;

namespace SI360.GateRunner.Services;

public sealed class PositiveConverter : IValueConverter
{
    public static readonly PositiveConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && d > 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NegativeConverter : IValueConverter
{
    public static readonly NegativeConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && d < 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
