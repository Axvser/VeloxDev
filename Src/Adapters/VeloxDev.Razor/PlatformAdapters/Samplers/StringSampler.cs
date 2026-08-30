using System.Drawing;
using System.Globalization;

namespace VeloxDev.Adapters.NativeSamplers
{
    /// <summary>
    /// Samples CSS color strings for Razor/Blazor properties.
    /// Non-color strings fall back to discrete frame switching.
    /// </summary>
    public class StringSampler : ISampler
    {
        public object? NormalizeStart(object? start, object? end, object? options) => start;
        public object? NormalizeEnd(object? start, object? end, object? options) => end;

        /// <summary>
        /// Computes the string value at normalized time <paramref name="t"/> in [0,1] and updates the property.
        /// Endpoint exactness is guaranteed here: t &lt;= 0 writes the precise start, t &gt;= 1 writes the precise end.
        /// The CSS color parse is cached in <paramref name="working"/> (once per animation) so middle frames only
        /// build the output string — no per-frame substring/Split/TryParse allocations.
        /// </summary>
        public void InsertFrame(object target, ITransitionProperty property, ref object? working, object? start, object? end, object? options, double t)
        {
            if (t <= 0) { property.SetValue(target, start); return; }
            if (t >= 1) { property.SetValue(target, end); return; }

            var startValue = start as string;
            var endValue = end as string;

            if (ReferenceEquals(working, DiscreteMarker))
            {
                // Discrete: hold the start value until the end (t >= 1 already returned the end value above).
                property.SetValue(target, startValue);
                return;
            }

            if (working is not ColorRange range)
            {
                if (TryResolveColorRange(startValue, endValue, out var startColor, out var endColor))
                {
                    range = new ColorRange(startColor, endColor);
                    working = range;
                }
                else
                {
                    working = DiscreteMarker;
                    property.SetValue(target, startValue);
                    return;
                }
            }

            property.SetValue(target, ToCssColor(InterpolateColor(range.Start, range.End, (float)t)));
        }

        private static readonly object DiscreteMarker = new();

        private sealed class ColorRange
        {
            public ColorRange(Color start, Color end)
            {
                Start = start;
                End = end;
            }

            public Color Start { get; }
            public Color End { get; }
        }

        private static bool TryResolveColorRange(string? start, string? end, out Color startColor, out Color endColor)
        {
            var hasStartColor = TryParseCssColor(start, out startColor);
            var hasEndColor = TryParseCssColor(end, out endColor);

            if (!hasStartColor && !hasEndColor)
            {
                return false;
            }

            if (!hasStartColor)
            {
                startColor = Color.FromArgb(0, endColor.R, endColor.G, endColor.B);
            }
            else if (!hasEndColor)
            {
                endColor = Color.FromArgb(0, startColor.R, startColor.G, startColor.B);
            }

            return true;
        }

        private static Color InterpolateColor(Color start, Color end, float progress)
        {
            return Color.FromArgb(
                InterpolateChannel(start.A, end.A, progress),
                InterpolateChannel(start.R, end.R, progress),
                InterpolateChannel(start.G, end.G, progress),
                InterpolateChannel(start.B, end.B, progress));
        }

        private static byte InterpolateChannel(byte start, byte end, float progress)
        {
            return (byte)Math.Round(start + ((end - start) * progress));
        }

        private static string ToCssColor(Color color)
        {
            // Writes directly into the string buffer via the interpolated string handler — one allocation per
            // frame (the output string itself), no intermediate FormattableString.
            return string.Create(CultureInfo.InvariantCulture,
                $"rgba({color.R}, {color.G}, {color.B}, {(color.A / 255d):0.###})");
        }

        private static bool TryParseCssColor(string? value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                return TryParseHexColor(text, out color);
            }

            if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseRgbColor(text, out color);
            }

            if (Enum.TryParse<KnownColor>(text, true, out var knownColor))
            {
                color = Color.FromKnownColor(knownColor);
                return true;
            }

            return false;
        }

        private static bool TryParseHexColor(string value, out Color color)
        {
            color = default;
            var hex = value[1..];
            switch (hex.Length)
            {
                case 3:
                    color = Color.FromArgb(
                        255,
                        ParseDuplicatedHexByte(hex[0]),
                        ParseDuplicatedHexByte(hex[1]),
                        ParseDuplicatedHexByte(hex[2]));
                    return true;
                case 4:
                    color = Color.FromArgb(
                        ParseDuplicatedHexByte(hex[3]),
                        ParseDuplicatedHexByte(hex[0]),
                        ParseDuplicatedHexByte(hex[1]),
                        ParseDuplicatedHexByte(hex[2]));
                    return true;
                case 6:
                    return TryParseHexByte(hex, 0, out var red)
                        && TryParseHexByte(hex, 2, out var green)
                        && TryParseHexByte(hex, 4, out var blue)
                        && SetColor(255, red, green, blue, out color);
                case 8:
                    return TryParseHexByte(hex, 0, out red)
                        && TryParseHexByte(hex, 2, out green)
                        && TryParseHexByte(hex, 4, out blue)
                        && TryParseHexByte(hex, 6, out var alpha)
                        && SetColor(alpha, red, green, blue, out color);
                default:
                    return false;
            }
        }

        private static bool TryParseRgbColor(string value, out Color color)
        {
            color = default;
            var startIndex = value.IndexOf('(');
            var endIndex = value.LastIndexOf(')');
            if (startIndex < 0 || endIndex <= startIndex)
            {
                return false;
            }

            var parts = value[(startIndex + 1)..endIndex]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length is not 3 and not 4)
            {
                return false;
            }

            if (!TryParseRgbComponent(parts[0], out var red)
                || !TryParseRgbComponent(parts[1], out var green)
                || !TryParseRgbComponent(parts[2], out var blue))
            {
                return false;
            }

            var alpha = (byte)255;
            if (parts.Length == 4 && !TryParseAlphaComponent(parts[3], out alpha))
            {
                return false;
            }

            color = Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        private static bool TryParseRgbComponent(string value, out byte component)
        {
            component = 0;
            if (value.EndsWith("%", StringComparison.Ordinal)
                && float.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            {
                component = ClampToByte((int)Math.Round((Math.Clamp(percentage, 0f, 100f) / 100f) * 255f));
                return true;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            {
                component = ClampToByte((int)Math.Round(numeric));
                return true;
            }

            return false;
        }

        private static bool TryParseAlphaComponent(string value, out byte alpha)
        {
            alpha = 255;
            if (value.EndsWith("%", StringComparison.Ordinal)
                && float.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            {
                alpha = ClampToByte((int)Math.Round((Math.Clamp(percentage, 0f, 100f) / 100f) * 255f));
                return true;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
            {
                return false;
            }

            alpha = numeric <= 1f
                ? ClampToByte((int)Math.Round(Math.Clamp(numeric, 0f, 1f) * 255f))
                : ClampToByte((int)Math.Round(numeric));

            return true;
        }

        private static byte ParseDuplicatedHexByte(char value)
        {
            var buffer = string.Concat(value, value);
            return byte.Parse(buffer, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static bool TryParseHexByte(string value, int startIndex, out byte result)
        {
            return byte.TryParse(value.Substring(startIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static bool SetColor(byte alpha, byte red, byte green, byte blue, out Color color)
        {
            color = Color.FromArgb(alpha, red, green, blue);
            return true;
        }

        private static byte ClampToByte(int value)
        {
            return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
        }
    }
}
