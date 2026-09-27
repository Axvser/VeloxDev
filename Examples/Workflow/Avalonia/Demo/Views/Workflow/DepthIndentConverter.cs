using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Demo;

/// <summary>
/// Turns a compiled-structure row's depth into a left margin, so the flat outline reads like the tree it describes.
/// </summary>
/// <remarks>
/// Per-platform on purpose: the projection (<c>CompiledOutline</c>) is platform-neutral and carries only the depth,
/// and how much a level indents by is a view decision.
/// </remarks>
internal sealed class DepthIndentConverter : IValueConverter
{
    /// <summary>How far one level indents, in pixels.</summary>
    public double Step { get; set; } = 14;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new Thickness(value is int depth && depth > 0 ? depth * Step : 0, 0, 0, 0);

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("The compiled structure is read-only.");
}
