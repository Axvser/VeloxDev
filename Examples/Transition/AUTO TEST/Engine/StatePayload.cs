using System.Globalization;

namespace VeloxDev.AT.Engine;

/// <summary>
/// The demo's machine-readable observation payload — the text of its <c>over.state</c> element — split into the
/// key/value pairs it is made of.
/// </summary>
/// <remarks>
/// Deliberately a plain key/value reader and not a typed model per demo. The payload is the one surface every demo
/// agrees on, and a suite that parsed the prose readout next to it would break the first time a label was reworded.
/// </remarks>
internal sealed class StatePayload
{
    private readonly Dictionary<string, string> _values;

    private StatePayload(Dictionary<string, string> values, string raw)
    {
        _values = values;
        Raw = raw;
    }

    /// <summary>The text the payload was parsed from, kept verbatim for failure messages.</summary>
    internal string Raw { get; }

    /// <summary>
    /// Parse the <c>k=v;k=v;</c> form. A field without a separator is rejected rather than dropped: a payload this
    /// reader cannot fully understand is a payload whose numbers must not be trusted.
    /// </summary>
    internal static StatePayload Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("The observation payload was empty; a demo writes it on every readout tick.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = field.IndexOf('=');
            if (separator <= 0)
                throw new FormatException($"The observation payload has a field with no key/value separator: '{field}' in '{text}'.");

            values[field[..separator].Trim()] = field[(separator + 1)..].Trim();
        }

        return new StatePayload(values, text);
    }

    /// <summary>The payload's format version — the first thing to check when a demo and a suite have drifted apart.</summary>
    internal int Version => (int)Number("v");

    /// <summary>
    /// Increments once per readout tick. Two reads whose sequence differs are proof the demo's readout timer is alive,
    /// which is what makes a fixed sleep unnecessary before clicking.
    /// </summary>
    internal long Sequence => (long)Number("seq");

    /// <summary>Which scenario is running, or <c>none</c> when the demo is idle.</summary>
    internal string Scenario => Text("scen");

    /// <summary>
    /// Whether the running scenario has finished. This — never "the value equals the target" — is what says an
    /// animation has landed: both overshoot curves cross their target again on the way back.
    /// </summary>
    internal bool Done => Number("done") != 0d;

    /// <summary>Milliseconds since the current scenario started, stamped inside the click handler itself.</summary>
    internal double ElapsedMs => Number("t");

    /// <summary>Whether the payload carries a field at all.</summary>
    internal bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The raw text of a field.</summary>
    internal string Text(string key)
        => _values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The payload has no '{key}'. It has: {string.Join(", ", _values.Keys)}.");

    /// <summary>
    /// A field as a number. Invariant first, then the running culture: the demos format their readouts with <c>F3</c>,
    /// which writes a decimal comma under a comma culture, and a suite should not fail over regional settings.
    /// </summary>
    internal double Number(string key)
    {
        var text = Text(key);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)) return invariant;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)) return current;

        throw new FormatException($"The payload field '{key}' is '{text}', which is not a number.");
    }

    /// <summary>A field as a colour, for the fields a demo writes as <c>#rrggbb</c>.</summary>
    internal RgbColor Color(string key) => RgbColor.Parse(Text(key));

    public override string ToString() => Raw;
}
