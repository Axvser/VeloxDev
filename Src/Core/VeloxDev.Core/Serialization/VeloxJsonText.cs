using System.Globalization;
using System.IO;

namespace VeloxDev.Serialization;

/// <summary>
/// How a value is spelled in a document.
/// </summary>
/// <remarks>
/// The archive serializer and the JSON tree both get their spelling from here, so a string escapes the same way
/// and a number is written the same way whichever one produced it. Two copies of that rule would be two places
/// for a document to start differing.
/// </remarks>
internal static class VeloxJsonText
{
    /// <summary>
    /// Writes a string's contents, quoted but unescaped by the caller.
    /// </summary>
    /// <param name="writer">Where the document is written.</param>
    /// <param name="value">The text between the quotes.</param>
    internal static void Escape(TextWriter writer, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': writer.Write("\\\""); break;
                case '\\': writer.Write("\\\\"); break;
                case '\b': writer.Write("\\b"); break;
                case '\f': writer.Write("\\f"); break;
                case '\n': writer.Write("\\n"); break;
                case '\r': writer.Write("\\r"); break;
                case '\t': writer.Write("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        writer.Write("\\u");
                        writer.Write(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.Write(c);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Spells a double the way the archive format does: shortest round-trippable, an integral value carrying a
    /// trailing <c>.0</c>, and the non-finite values as strings.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The JSON spelling — a bare number, or a quoted string for the non-finite values.</returns>
    internal static string Double(double value)
    {
        if (double.IsNaN(value)) return "\"NaN\"";
        if (double.IsPositiveInfinity(value)) return "\"Infinity\"";
        if (double.IsNegativeInfinity(value)) return "\"-Infinity\"";

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0) text += ".0";
        return text;
    }
}
