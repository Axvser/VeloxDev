using System;
using System.Drawing;
using System.Globalization;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Parses the colour text a generated host carries in its symbols — <c>#RRGGBB</c>, <c>#AARRGGBB</c>, or a
/// <see cref="Color"/> name.
/// </summary>
/// <remarks>
/// Templates spell their palette as strings because a <c>dotnet new</c> symbol is text; this is the one place that
/// turns those strings into colour, so every surface item agrees on what a value means.
/// </remarks>
public static class WorkflowSurfaceColors
{
    /// <summary>Parses a colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    public static Color Parse(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }
}
