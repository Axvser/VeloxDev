using System.Collections.Concurrent;
using System.Globalization;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// The four data magnitudes every benchmark runs at.
/// </summary>
/// <remarks>
/// One place, so the two benchmark classes cannot drift apart and the report can name a scale without guessing.
/// <c>超大</c> stops at 30 000 on purpose: Newtonsoft allocates about 50 MB per operation at 1 000 nodes, so one
/// more order of magnitude would be a gigabyte per operation and the measurement would be of the GC, not of the
/// serializers.
/// </remarks>
internal static class Scales
{
    internal const int Small = 100;
    internal const int Medium = 1_000;
    internal const int Large = 10_000;
    internal const int Huge = 30_000;

    /// <summary>The scale's name, for a report written in the language the project is written in.</summary>
    internal static string NameOf(int nodeCount) => nodeCount switch
    {
        Small => "小",
        Medium => "中",
        Large => "大",
        Huge => "超大",
        _ => nodeCount.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The document sizes the setups measured, so the report can put a size beside every timing.
/// </summary>
/// <remarks>
/// A static because the benchmarks run in this process (<c>InProcessEmitToolchain</c>) and a report is written
/// after them, from the same run. Sizes are the whole point of the comparison: a serializer that writes four
/// times less has an easier job on both ends.
/// </remarks>
internal static class DocumentSizes
{
    private static readonly ConcurrentDictionary<int, (int Archive, int Stj, int Newtonsoft)> Measured = new();

    /// <summary>Remembers what the three serializers produced for one scale.</summary>
    internal static void Record(int nodeCount, int archive, int stj, int newtonsoft)
        => Measured[nodeCount] = (archive, stj, newtonsoft);

    /// <summary>What was measured, keyed by node count.</summary>
    internal static IReadOnlyDictionary<int, (int Archive, int Stj, int Newtonsoft)> All => Measured;
}
