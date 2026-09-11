using System.Globalization;

namespace VeloxDev.AT.Engine;

/// <summary>
/// The sampler-conformance payload a demo publishes on its <c>over.conf</c> element: what each of its adapter's
/// samplers wrote, for every eased time it was driven at.
/// </summary>
/// <remarks>
/// The shape is the same <c>k=v;</c> form as <see cref="StatePayload"/>, with one field per frame named
/// <c>s.&lt;sampler&gt;.&lt;time index&gt;</c>. The value is the produced value's type name followed by its components,
/// fixed order, so a sampler that changes the type it produces is caught as that rather than as a number mismatch.
/// </remarks>
internal sealed class ConformancePayload
{
    private ConformancePayload(int version, long sequence, int declaredCount, IReadOnlyList<Frame> frames, string raw)
    {
        Version = version;
        Sequence = sequence;
        DeclaredCount = declaredCount;
        Frames = frames;
        Raw = raw;
    }

    /// <summary>One sampler driven once: what it produced at one eased time.</summary>
    /// <param name="Sampler">The sampler's type name — the key both sides of the check agree on.</param>
    /// <param name="TimeIndex">Which entry of the shared time grid this frame was driven at.</param>
    /// <param name="TypeTag">The produced value's type name.</param>
    /// <param name="Components">The produced value's components, in the order the demo serialises them.</param>
    internal sealed record Frame(string Sampler, int TimeIndex, string TypeTag, IReadOnlyList<double> Components);

    /// <summary>The payload's format version.</summary>
    internal int Version { get; }

    /// <summary>
    /// Increments once per activated sampler handle. Two reads whose sequence differs are proof that a click landed
    /// rather than being dropped — the same handshake the readout uses, and not replaceable by a sleep.
    /// </summary>
    internal long Sequence { get; }

    /// <summary>The number of frames the demo says it wrote. Checked against what was actually parsed.</summary>
    internal int DeclaredCount { get; }

    /// <summary>Every frame, in payload order.</summary>
    internal IReadOnlyList<Frame> Frames { get; }

    /// <summary>The text the payload was parsed from, kept verbatim for failure messages.</summary>
    internal string Raw { get; }

    internal static ConformancePayload Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("The conformance payload was empty; a demo writes it once the window is loaded.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = field.IndexOf('=');
            if (separator <= 0)
                throw new FormatException($"The conformance payload has a field with no key/value separator: '{field}' in '{text}'.");

            values[field[..separator].Trim()] = field[(separator + 1)..].Trim();
        }

        var frames = new List<Frame>();
        foreach (var (key, value) in values)
        {
            if (!key.StartsWith("s.", StringComparison.Ordinal)) continue;

            // s.<sampler>.<time index> —— 采样器名里不会有点，所以从右边断一次就够。
            var lastDot = key.LastIndexOf('.');
            var name = key[2..lastDot];
            if (!int.TryParse(key[(lastDot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                throw new FormatException($"The conformance field '{key}' does not end in a time index.");

            var parts = value.Split(',');
            var components = new double[parts.Length - 1];
            for (var i = 1; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i - 1]))
                    throw new FormatException($"The conformance field '{key}' has a component '{parts[i]}' that is not a number.");
            }

            frames.Add(new Frame(name, index, parts[0], components));
        }

        if (!values.TryGetValue("v", out var versionText) || !int.TryParse(versionText, out var version))
            throw new FormatException($"The conformance payload has no readable version: '{text}'.");

        var parsedSequence = values.TryGetValue("seq", out var sequenceText) && long.TryParse(sequenceText, out var sequence)
            ? sequence
            : throw new FormatException($"The conformance payload has no readable sequence number: '{text}'.");

        var declared = values.TryGetValue("n", out var countText) && int.TryParse(countText, out var count) ? count : -1;

        return new ConformancePayload(version, parsedSequence, declared, frames, text);
    }
}
