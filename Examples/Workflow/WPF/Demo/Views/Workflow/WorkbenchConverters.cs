using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Demo.Views.Workflow;

/// <summary>Turns the canvas scale factor into the percentage the status bar shows.</summary>
public sealed class ZoomPercentConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var scale = value switch
        {
            double d => d,
            int i => i,
            _ => 1d,
        };

        return scale > 0
            ? Math.Round(100d / scale).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "—";
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("The status bar is read-only.");
}

/// <summary>Indents one compiled-structure row by its depth, in pixels.</summary>
public sealed class DepthIndentConverter : IValueConverter
{
    /// <summary>How far one level indents, in pixels.</summary>
    public double Step { get; set; } = 14;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new Thickness(value is int depth && depth > 0 ? depth * Step : 0, 0, 0, 0);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("The compiled structure is read-only.");
}

/// <summary>Makes the element visible while the bound flag is false — the mirror of the built-in converter.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("The status bar is read-only.");
}

/// <summary>Makes the element visible when the bound count is zero — the empty-state hint.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("The compiled structure is read-only.");
}
