using System.Globalization;

namespace VeloxDev.AT.Engine;

/// <summary>
/// An 8-bit-per-channel colour, which is the form the demos report a solid brush in.
/// </summary>
/// <remarks>
/// Its own type rather than <c>System.Windows.Media.Color</c>: the acceptance project references no UI framework, so
/// that it stays a single-target-framework project with no workload of its own.
/// </remarks>
internal readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Parse <c>#rrggbb</c>, in either case.</summary>
    internal static RgbColor Parse(string text)
        => TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a #rrggbb colour.");

    /// <summary>Try to parse <c>#rrggbb</c>, in either case.</summary>
    internal static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (text is null || text.Length != 7 || text[0] != '#') return false;

        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;

        color = new RgbColor(r, g, b);
        return true;
    }

    /// <summary>The payload's own spelling: lower case, the way the demos write it.</summary>
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}
