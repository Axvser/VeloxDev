using System.Globalization;

namespace VeloxDev.AT.Engine;

/// <summary>
/// The live payload a demo publishes on its <c>over.live</c> element: what the control held while a real transition
/// ran one sampler, sampled from the control's own property.
/// </summary>
/// <remarks>
/// Same <c>k=v;</c> shape as <see cref="StatePayload"/> and <see cref="ConformancePayload"/>. What it adds over the
/// conformance payload is time: that one is five frames driven one at a time, this one is what landed on the element
/// over the whole run — the observed envelope, whether any sample was not a finite number, and the value the element
/// settled on once the animation stopped.
/// <para>
/// A click publishes <c>done=0</c> immediately and <c>done=1</c> when the run is over. That is what lets a suite tell
/// "this run has not finished" apart from "this is still the previous run's payload", which a sleep cannot do.
/// </para>
/// </remarks>
internal sealed class LivePayload
{
    private LivePayload(
        int version,
        long sequence,
        bool done,
        string sampler,
        string typeTag,
        int declaredComponents,
        int samples,
        int bad,
        string? error,
        IReadOnlyList<double> last,
        IReadOnlyList<double> min,
        IReadOnlyList<double> max,
        string raw)
    {
        Version = version;
        Sequence = sequence;
        Done = done;
        Sampler = sampler;
        TypeTag = typeTag;
        DeclaredComponents = declaredComponents;
        Samples = samples;
        Bad = bad;
        Error = error;
        Last = last;
        Min = min;
        Max = max;
        Raw = raw;
    }

    /// <summary>The payload's format version.</summary>
    internal int Version { get; }

    /// <summary>
    /// The activation count, shared with the conformance payload: both are written by the same click, so one number
    /// proves the click landed for both.
    /// </summary>
    internal long Sequence { get; }

    /// <summary>Whether the run has finished. The rest of the payload is only meaningful once it has.</summary>
    internal bool Done { get; }

    /// <summary>The sampler this run drove.</summary>
    internal string Sampler { get; }

    /// <summary>The runtime type the control's property held, as the demo read it back.</summary>
    internal string TypeTag { get; }

    /// <summary>The component count the demo says it wrote. Cross-checked against the vectors actually parsed.</summary>
    internal int DeclaredComponents { get; }

    /// <summary>How many times the property was sampled while the run was in flight.</summary>
    internal int Samples { get; }

    /// <summary>How many of those samples held a component that was not a finite number.</summary>
    internal int Bad { get; }

    /// <summary>
    /// What starting the run threw, if it threw: <c>null</c> when the demo reported none. An animation failing later
    /// than this is invisible — the pipeline swallows it — and is what <see cref="Last"/> is for.
    /// </summary>
    internal string? Error { get; }

    /// <summary>The value the control held once the run was over, per component.</summary>
    internal IReadOnlyList<double> Last { get; }

    /// <summary>The smallest value each component was seen to hold.</summary>
    internal IReadOnlyList<double> Min { get; }

    /// <summary>The largest value each component was seen to hold.</summary>
    internal IReadOnlyList<double> Max { get; }

    /// <summary>The text this was parsed from, kept verbatim for failure messages.</summary>
    internal string Raw { get; }

    internal static LivePayload Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("The live payload was empty; a demo writes it once its window is loaded.");

        return From(ConformancePayload.Fields(text, "live"), text);
    }

    /// <summary>
    /// Build from fields that have already been split out — the batch payload carries one of these per row, under
    /// <c>l.&lt;sampler&gt;.</c> keys, and hands each row's group here with its own version/sequence/done filled in.
    /// </summary>
    internal static LivePayload From(IReadOnlyDictionary<string, string> values, string raw)
    {
        if (!values.TryGetValue("v", out var versionText) || !int.TryParse(versionText, out var version))
            throw new FormatException($"The live payload has no readable version: '{raw}'.");

        if (!values.TryGetValue("seq", out var sequenceText) || !long.TryParse(sequenceText, out var sequence))
            throw new FormatException($"The live payload has no readable sequence number: '{raw}'.");

        // done=0 的载荷只有 seq 与采样器名 —— 那正是它的用处：证明这一次点击开始了，值还没出来。
        if (!values.TryGetValue("done", out var doneText) || !int.TryParse(doneText, out var done))
            throw new FormatException($"The live payload does not say whether the run finished: '{raw}'.");

        values.TryGetValue("sampler", out var sampler);
        values.TryGetValue("type", out var typeTag);

        var declared = values.TryGetValue("k", out var countText) && int.TryParse(countText, out var count) ? count : -1;
        var samples = values.TryGetValue("samples", out var samplesText) && int.TryParse(samplesText, out var s) ? s : -1;
        var bad = values.TryGetValue("bad", out var badText) && int.TryParse(badText, out var b) ? b : -1;

        // "-" 是"没有异常"的占位符 —— 空字段在 key=value 载荷里读不出"有值且为空"与"没这个字段"的区别。
        var error = values.TryGetValue("err", out var errorText) && !string.IsNullOrEmpty(errorText) && errorText != "-"
            ? errorText
            : null;

        return new LivePayload(
            version,
            sequence,
            done != 0,
            sampler ?? "-",
            typeTag ?? "-",
            declared,
            samples,
            bad,
            error,
            Vector(values, "last"),
            Vector(values, "min"),
            Vector(values, "max"),
            raw);
    }

    private static IReadOnlyList<double> Vector(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text)) return [];

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var components = new double[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out components[index]))
                throw new FormatException($"The live payload's '{key}' has a component '{parts[index]}' that is not a number.");
        }

        return components;
    }
}
