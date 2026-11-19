using System.Globalization;

namespace VeloxDev.AT.Engine;

/// <summary>
/// The two spellings a CSS colour arrives in: <c>#rrggbb</c>, which the browser adapter writes back untouched at an
/// animation's endpoints, and <c>rgb()</c>/<c>rgba()</c>, which it writes on every frame in between.
/// </summary>
/// <remarks>
/// Both reduce to the same channels, so a suite can assert on channels without caring which form a frame produced —
/// which is the whole reason this reader exists rather than a second <see cref="RgbColor"/>: that type is the payload's
/// own spelling, and this one is the tolerance the browser forces on whoever reads it. Alpha is dropped: the
/// assertions are per RGB channel, and the demos animate opaque fills.
/// </remarks>
internal static class CssColor
{
    /// <summary>Parse either spelling.</summary>
    /// <exception cref="FormatException">The text is neither.</exception>
    internal static RgbColor Parse(string text)
        => TryParse(text, out var color)
            ? color
            : throw new FormatException($"'{text}' is neither a #rrggbb nor an rgb()/rgba() colour.");

    /// <summary>Try to parse either spelling.</summary>
    internal static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        return trimmed[0] == '#'
            ? RgbColor.TryParse(trimmed, out color)
            : TryParseRgbFunction(trimmed, out color);
    }

    private static bool TryParseRgbFunction(string text, out RgbColor color)
    {
        color = default;
        if (!text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) return false;

        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close <= open) return false;

        var parts = text[(open + 1)..close]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is not 3 and not 4) return false;

        if (!TryComponent(parts[0], out var red)
            || !TryComponent(parts[1], out var green)
            || !TryComponent(parts[2], out var blue))
        {
            return false;
        }

        color = new RgbColor(red, green, blue);
        return true;
    }

    /// <summary>A channel: a percentage or a plain number, clamped and rounded the way the adapter writes it.</summary>
    private static bool TryComponent(string text, out byte value)
    {
        value = 0;
        if (text.EndsWith('%') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            value = Clamp(percent / 100d * 255d);
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            value = Clamp(numeric);
            return true;
        }

        return false;
    }

    private static byte Clamp(double value)
        => value <= 0d ? (byte)0 : value >= 255d ? (byte)255 : (byte)Math.Round(value);
}
