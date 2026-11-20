using System.Globalization;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// The four-side length used by the animatable <see cref="Control.Margin"/>, <see cref="Control.Padding"/> and
/// <see cref="Border.BorderThickness"/> properties.
/// </summary>
/// <remarks>
/// <para>
/// The components are <see cref="float"/>, not <see cref="int"/>, because the transition system samples them every
/// frame: rounding to whole pixels inside the value type would turn a smooth animation into a staircase. The native
/// WinForms <see cref="System.Windows.Forms.Padding"/> is written with <see cref="ToPadding"/>, which rounds once per
/// frame at the boundary.
/// </para>
/// <para>
/// A literal is either one number for all four sides (<c>"8"</c>), two numbers for horizontal and vertical
/// (<c>"8,4"</c>) or four numbers in left, top, right, bottom order (<c>"1,2,3,4"</c>).
/// </para>
/// </remarks>
public readonly struct Thickness : IEquatable<Thickness>
{
    /// <summary>A thickness whose four sides are zero.</summary>
    public static readonly Thickness Empty = new(0f);

    // WinForms 的 Control.DefaultMargin 是 3；DP 默认值取同一个数，动画起点才不会从 0 跳一下。
    internal static readonly Thickness DefaultMargin = new(3f);

    /// <summary>Creates a thickness with all four sides set to <paramref name="uniformLength"/>.</summary>
    /// <param name="uniformLength">The length of every side.</param>
    public Thickness(float uniformLength)
        : this(uniformLength, uniformLength, uniformLength, uniformLength)
    {
    }

    /// <summary>Creates a thickness from horizontal and vertical lengths.</summary>
    /// <param name="horizontal">The left and right length.</param>
    /// <param name="vertical">The top and bottom length.</param>
    public Thickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    /// <summary>Creates a thickness with one length per side.</summary>
    /// <param name="left">The left length.</param>
    /// <param name="top">The top length.</param>
    /// <param name="right">The right length.</param>
    /// <param name="bottom">The bottom length.</param>
    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>The left length.</summary>
    public float Left { get; }

    /// <summary>The top length.</summary>
    public float Top { get; }

    /// <summary>The right length.</summary>
    public float Right { get; }

    /// <summary>The bottom length.</summary>
    public float Bottom { get; }

    /// <summary>The sum of <see cref="Left"/> and <see cref="Right"/>.</summary>
    public float Horizontal => Left + Right;

    /// <summary>The sum of <see cref="Top"/> and <see cref="Bottom"/>.</summary>
    public float Vertical => Top + Bottom;

    /// <summary>True when all four sides are zero.</summary>
    public bool IsEmpty => Left == 0f && Top == 0f && Right == 0f && Bottom == 0f;

    /// <summary>True when all four sides are equal.</summary>
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    /// <summary>Parses <c>"8"</c>, <c>"8,4"</c> or <c>"1,2,3,4"</c> with the invariant culture.</summary>
    /// <param name="text">The literal text.</param>
    /// <returns>The parsed thickness.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is null, empty or has an unsupported component count.</exception>
    public static Thickness Parse(string text)
    {
        if (!TryParse(text, out var thickness))
        {
            throw new FormatException(
                $"'{text}' is not a thickness value (one, two or four numbers).");
        }

        return thickness;
    }

    /// <summary>Tries to parse <c>"8"</c>, <c>"8,4"</c> or <c>"1,2,3,4"</c> with the invariant culture.</summary>
    /// <param name="text">The literal text.</param>
    /// <param name="thickness">The parsed thickness when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is a thickness literal.</returns>
    public static bool TryParse(string? text, out Thickness thickness)
    {
        thickness = Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var pieces = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var values = new float[pieces.Length];
        for (var index = 0; index < pieces.Length; index++)
        {
            if (!float.TryParse(pieces[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
            {
                return false;
            }
        }

        switch (values.Length)
        {
            case 1:
                thickness = new Thickness(values[0]);
                return true;
            case 2:
                thickness = new Thickness(values[0], values[1]);
                return true;
            case 4:
                thickness = new Thickness(values[0], values[1], values[2], values[3]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Converts this thickness to the integer native padding WinForms layout reads.</summary>
    /// <returns>The four sides rounded to whole pixels.</returns>
    public System.Windows.Forms.Padding ToPadding() => new(
        Round(Left),
        Round(Top),
        Round(Right),
        Round(Bottom));

    /// <summary>Creates a thickness from a native WinForms padding.</summary>
    /// <param name="padding">The padding to copy.</param>
    /// <returns>The equivalent thickness.</returns>
    public static Thickness FromPadding(System.Windows.Forms.Padding padding)
        => new(padding.Left, padding.Top, padding.Right, padding.Bottom);

    /// <summary>Creates a thickness with all four sides set to <paramref name="uniformLength"/>.</summary>
    /// <param name="uniformLength">The length of every side.</param>
    /// <returns>The equivalent thickness.</returns>
    public static implicit operator Thickness(float uniformLength) => new(uniformLength);

    /// <summary>Creates a thickness with all four sides set to <paramref name="uniformLength"/>.</summary>
    /// <param name="uniformLength">The length of every side.</param>
    /// <returns>The equivalent thickness.</returns>
    public static implicit operator Thickness(int uniformLength) => new(uniformLength);

    /// <summary>Creates a thickness with all four sides set to <paramref name="uniformLength"/>.</summary>
    /// <param name="uniformLength">The length of every side.</param>
    /// <returns>The equivalent thickness.</returns>
    public static implicit operator Thickness(double uniformLength) => new((float)uniformLength);

    /// <summary>Compares two thicknesses side by side.</summary>
    /// <param name="left">The first thickness.</param>
    /// <param name="right">The second thickness.</param>
    /// <returns><see langword="true"/> when every side is equal.</returns>
    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    /// <summary>Compares two thicknesses side by side.</summary>
    /// <param name="left">The first thickness.</param>
    /// <param name="right">The second thickness.</param>
    /// <returns><see langword="true"/> when any side differs.</returns>
    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Thickness other)
        => Left.Equals(other.Left)
            && Top.Equals(other.Top)
            && Right.Equals(other.Right)
            && Bottom.Equals(other.Bottom);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    /// <summary>Returns the four sides in left, top, right, bottom order.</summary>
    /// <returns>The invariant-culture literal form.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Left},{Top},{Right},{Bottom}");

    private static int Round(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
