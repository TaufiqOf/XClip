using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace XClip.Converters;

public class TruncateConverter : IValueConverter
{
    public int MaxLength { get; set; } = 500;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && str.Length > MaxLength) return str[..MaxLength] + "...";
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}