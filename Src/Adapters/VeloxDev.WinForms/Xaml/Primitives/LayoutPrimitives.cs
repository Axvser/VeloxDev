using System.Drawing;
using System.Globalization;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>How a control participates in painting and layout.</summary>
public enum Visibility
{
    /// <summary>The control is painted, laid out and occupies a slot.</summary>
    Visible = 0,

    /// <summary>The control is not painted, but layout still reserves its slot.</summary>
    Hidden = 1,

    /// <summary>The control is not painted and layout skips it.</summary>
    Collapsed = 2,
}

/// <summary>Where a layout panel places a control horizontally inside its slot.</summary>
public enum HorizontalAlignment
{
    /// <summary>Place the control at the left edge.</summary>
    Left = 0,

    /// <summary>Centre the control horizontally.</summary>
    Center = 1,

    /// <summary>Place the control at the right edge.</summary>
    Right = 2,

    /// <summary>Stretch the control across the slot.</summary>
    Stretch = 3,
}

/// <summary>Where a layout panel places a control vertically inside its slot.</summary>
public enum VerticalAlignment
{
    /// <summary>Place the control at the top edge.</summary>
    Top = 0,

    /// <summary>Centre the control vertically.</summary>
    Center = 1,

    /// <summary>Place the control at the bottom edge.</summary>
    Bottom = 2,

    /// <summary>Stretch the control across the slot.</summary>
    Stretch = 3,
}

/// <summary>The edge a <see cref="DockPanel"/> docks a child to.</summary>
/// <remarks>
/// The type is not named <c>Dock</c> because <see cref="System.Windows.Forms.Control.Dock"/> is an inherited member;
/// the attached property itself is still <c>DockPanel.Dock</c>.
/// </remarks>
public enum DockPlacement
{
    /// <summary>Dock to the left edge.</summary>
    Left = 0,

    /// <summary>Dock to the top edge.</summary>
    Top = 1,

    /// <summary>Dock to the right edge.</summary>
    Right = 2,

    /// <summary>Dock to the bottom edge.</summary>
    Bottom = 3,

    /// <summary>Do not dock; the child fills the remaining room when it is the last child.</summary>
    None = 4,
}

/// <summary>The unit a <see cref="GridLength"/> is expressed in.</summary>
public enum GridUnitType
{
    /// <summary>A fixed number of pixels.</summary>
    Pixel = 0,

    /// <summary>Size to the content.</summary>
    Auto = 1,

    /// <summary>A weighted share of the remaining room.</summary>
    Star = 2,
}

/// <summary>How an <see cref="Image"/> fits its source into its bounds.</summary>
public enum Stretch
{
    /// <summary>Draw at the source's own size.</summary>
    None = 0,

    /// <summary>Fill the bounds, ignoring the aspect ratio.</summary>
    Fill = 1,

    /// <summary>Fit inside the bounds and keep the aspect ratio.</summary>
    Uniform = 2,

    /// <summary>Fill the bounds without letterboxing and keep the aspect ratio.</summary>
    UniformToFill = 3,
}

/// <summary>A grid row or column length: pixels, auto sizing or a weighted share.</summary>
public readonly struct GridLength : IEquatable<GridLength>
{
    /// <summary>Creates a pixel length.</summary>
    /// <param name="value">The length in pixels.</param>
    public GridLength(double value)
        : this(value, GridUnitType.Pixel)
    {
    }

    /// <summary>Creates a length in <paramref name="unitType"/>.</summary>
    /// <param name="value">The numeric value; ignored for <see cref="GridUnitType.Auto"/>.</param>
    /// <param name="unitType">The unit the value is expressed in.</param>
    public GridLength(double value, GridUnitType unitType)
    {
        Value = unitType == GridUnitType.Auto ? 0d : value;
        GridUnitType = unitType;
    }

    /// <summary>A length that sizes to its content.</summary>
    public static GridLength Auto => new(0d, GridUnitType.Auto);

    /// <summary>A single weighted share.</summary>
    public static GridLength Star => new(1d, GridUnitType.Star);

    /// <summary>The numeric value; zero for <see cref="GridUnitType.Auto"/>.</summary>
    public double Value { get; }

    /// <summary>The unit the value is expressed in.</summary>
    public GridUnitType GridUnitType { get; }

    /// <summary>True when the length sizes to its content.</summary>
    public bool IsAuto => GridUnitType == GridUnitType.Auto;

    /// <summary>True when the length is a weighted share.</summary>
    public bool IsStar => GridUnitType == GridUnitType.Star;

    /// <summary>True when the length is a fixed pixel count.</summary>
    public bool IsAbsolute => GridUnitType == GridUnitType.Pixel;

    /// <summary>Parses <c>"Auto"</c>, <c>"*"</c>, <c>"2*"</c> or a pixel count.</summary>
    /// <param name="text">The literal text.</param>
    public static GridLength Parse(string text)
    {
        if (!TryParse(text, out var length))
        {
            throw new FormatException($"'{text}' is not a grid length (Auto, *, 2* or a pixel count).");
        }

        return length;
    }

    /// <summary>Tries to parse <c>"Auto"</c>, <c>"*"</c>, <c>"2*"</c> or a pixel count.</summary>
    /// <param name="text">The literal text.</param>
    /// <param name="length">The parsed length when this returns <see langword="true"/>.</param>
    public static bool TryParse(string? text, out GridLength length)
    {
        length = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            length = Auto;
            return true;
        }

        if (text.EndsWith("*", StringComparison.Ordinal))
        {
            var weight = text.Length == 1
                ? 1d
                : double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : double.NaN;
            if (double.IsNaN(weight) || weight < 0d)
            {
                return false;
            }

            length = new GridLength(weight, GridUnitType.Star);
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
        {
            length = new GridLength(pixels);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool Equals(GridLength other) => Value.Equals(other.Value) && GridUnitType == other.GridUnitType;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridLength other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Value.GetHashCode() * 397) ^ (int)GridUnitType;

    /// <inheritdoc />
    public override string ToString() => GridUnitType switch
    {
        GridUnitType.Auto => "Auto",
        GridUnitType.Star => Value == 1d ? "*" : $"{Value.ToString(CultureInfo.InvariantCulture)}*",
        _ => Value.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>One row of a <see cref="Grid"/>.</summary>
public sealed class RowDefinition
{
    /// <summary>The row's height; a weighted share by default.</summary>
    public GridLength Height { get; set; } = GridLength.Star;
}

/// <summary>One column of a <see cref="Grid"/>.</summary>
public sealed class ColumnDefinition
{
    /// <summary>The column's width; a weighted share by default.</summary>
    public GridLength Width { get; set; } = GridLength.Star;
}
