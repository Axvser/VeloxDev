using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace VeloxDev.Docs;

public sealed class ImageScaleConverter : IValueConverter
{
    public static ImageScaleConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = value is double d ? d : 1.0;
        return 640.0 * scale;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
