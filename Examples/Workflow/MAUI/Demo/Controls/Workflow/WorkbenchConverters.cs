using System.Globalization;

namespace Demo.Controls;

/// <summary>Inverts a boolean — the workbench shows one of two labels from a single model flag.</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag ? !flag : false;
}

/// <summary>True only when a numeric value is zero — the empty-state hints bind a collection count through it.</summary>
public sealed class ZeroToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int count => count == 0,
            long count => count == 0,
            double count => Math.Abs(count) < double.Epsilon,
            null => true,
            _ => false,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The workbench lists are read-only.");
}

/// <summary>Turns a compiled-outline depth into a left margin, one step per nesting level.</summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    /// <summary>How far one level indents, in pixels.</summary>
    public double Step { get; set; } = 12;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new Thickness(value is int depth && depth > 0 ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The compiled structure is read-only.");
}

/// <summary>
/// Reads the canvas scale as a percentage. The layout stores scale the way the surface multiplies by it,
/// so a zoomed-in canvas has a value below 1.
/// </summary>
public sealed class ZoomPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 1d,
        };

        return scale > 0
            ? Math.Round(100d / scale).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The status bar is read-only.");
}
