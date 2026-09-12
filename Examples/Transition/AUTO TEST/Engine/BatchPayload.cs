namespace VeloxDev.AT.Engine;

/// <summary>
/// The payload a demo publishes on its <c>over.batch</c> element: one run of **every** case row at once.
/// </summary>
/// <remarks>
/// The per-row payloads (<c>over.conf</c>, <c>over.live</c>) are shaped around "click one row, read what it wrote",
/// so driving twelve rows that way means twelve clicks and twelve waits — and each wait is a whole animation. This
/// payload is the bulk channel: one click starts every row, and one read collects all of them.
/// <para>
/// It carries the same two things the per-row payloads do, in the same field shapes, which is why the parsers are
/// reused rather than duplicated:
/// </para>
/// <list type="bullet">
/// <item><c>s.&lt;sampler&gt;.&lt;time index&gt;=…</c> — the closed-form frames, exactly as <c>over.conf</c> writes them.</item>
/// <item>
/// <c>l.&lt;sampler&gt;.&lt;field&gt;=…</c> — one row's live digest, exactly as <c>over.live</c> writes it, with the
/// version, sequence and done flag repeated inside each group so a group parses on its own.
/// </item>
/// </list>
/// <para>
/// <c>done=0</c> until every row has settled, so the suite can tell "still running" from "the previous run's
/// payload" without a sleep — the same handshake the per-row payload uses, extended across rows.
/// </para>
/// </remarks>
internal sealed class BatchPayload
{
    /// <summary>The prefix a row's live fields are grouped under.</summary>
    private const string LivePrefix = "l.";

    private BatchPayload(
        int version,
        long sequence,
        bool done,
        int rows,
        ConformancePayload frames,
        IReadOnlyList<LivePayload> lives,
        string raw)
    {
        Version = version;
        Sequence = sequence;
        Done = done;
        Rows = rows;
        Frames = frames;
        Lives = lives;
        Raw = raw;
    }

    /// <summary>The payload's format version.</summary>
    internal int Version { get; }

    /// <summary>Increments once per bulk run. Two reads whose sequence differs prove this is a new run.</summary>
    internal long Sequence { get; }

    /// <summary>Whether every row has finished. The rest of the payload is only meaningful once it has.</summary>
    internal bool Done { get; }

    /// <summary>How many rows the demo says it ran. Cross-checked against the rows actually reported.</summary>
    internal int Rows { get; }

    /// <summary>Every row's closed-form frames, in one <see cref="ConformancePayload"/>.</summary>
    internal ConformancePayload Frames { get; }

    /// <summary>Every row's live digest, one entry per row, keyed by the sampler name it carries.</summary>
    internal IReadOnlyList<LivePayload> Lives { get; }

    /// <summary>The text this was parsed from, kept verbatim for failure messages.</summary>
    internal string Raw { get; }

    internal static BatchPayload Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("The batch payload was empty; a demo writes it once its window is loaded.");

        var values = ConformancePayload.Fields(text, "batch");

        if (!values.TryGetValue("v", out var versionText) || !int.TryParse(versionText, out var version))
            throw new FormatException($"The batch payload has no readable version: '{text}'.");

        if (!values.TryGetValue("seq", out var sequenceText) || !long.TryParse(sequenceText, out var sequence))
            throw new FormatException($"The batch payload has no readable sequence number: '{text}'.");

        if (!values.TryGetValue("done", out var doneText) || !int.TryParse(doneText, out var done))
            throw new FormatException($"The batch payload does not say whether the run finished: '{text}'.");

        var rows = values.TryGetValue("rows", out var rowsText) && int.TryParse(rowsText, out var r) ? r : -1;

        // 闭式解那半与逐行载荷同一套字段，直接交给同一个解析器 —— 它会按 s.<采样器>.<t> 名字把帧分好。
        var frames = ConformancePayload.From(values, text);

        return new BatchPayload(version, sequence, done != 0, rows, frames, GroupLives(values, version, sequence, text), text);
    }

    /// <summary>
    /// Group the <c>l.&lt;sampler&gt;.&lt;field&gt;</c> keys back into one dictionary per row and parse each.
    /// </summary>
    /// <remarks>
    /// The sampler name is what the group is keyed by, so it is read from the key rather than from a field — a row
    /// whose group came back under the wrong name is then a parse-level impossibility instead of a silent mix-up.
    /// </remarks>
    private static List<LivePayload> GroupLives(
        IReadOnlyDictionary<string, string> values, int version, long sequence, string raw)
    {
        Dictionary<string, Dictionary<string, string>> groups = new(StringComparer.Ordinal);
        List<string> order = [];

        foreach (var (key, value) in values)
        {
            if (!key.StartsWith(LivePrefix, StringComparison.Ordinal)) continue;

            var lastDot = key.LastIndexOf('.');
            if (lastDot <= LivePrefix.Length) continue;

            var sampler = key[LivePrefix.Length..lastDot];
            if (!groups.TryGetValue(sampler, out var group))
            {
                group = new Dictionary<string, string>(StringComparer.Ordinal);
                groups[sampler] = group;
                order.Add(sampler);
            }

            group[key[(lastDot + 1)..]] = value;
        }

        var lives = new List<LivePayload>();
        foreach (var sampler in order)
        {
            var group = groups[sampler];

            // 版本/序号/done 属于整份载荷，不属于某一行；补进去，好让每一条都用自己的解析器独立解出来。
            group["v"] = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            group["seq"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            group["done"] = group.TryGetValue("done", out var rowDone) ? rowDone : "1";
            group["sampler"] = sampler;

            lives.Add(LivePayload.From(group, $"{raw} [row {sampler}]"));
        }

        return lives;
    }
}
