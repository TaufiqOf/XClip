using System;
using System.Globalization;
using Avalonia.Data.Converters;
using XClip.ViewModels;

namespace XClip.Converters;

public sealed class IsImageFormatConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        var isImage = value is ClipBoardDataFormat format &&
                      format == ClipBoardDataFormat.Image;

        if (parameter?.ToString() == "Inverse")
            return !isImage;

        return isImage;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

}