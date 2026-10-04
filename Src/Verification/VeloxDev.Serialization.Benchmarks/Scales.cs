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

    /// <summary>
    /// The magnitudes this run measures. Small, medium and large by default; <c>--all</c> adds the huge one.
    /// </summary>
    /// <remarks>
    /// The huge scale is roughly two thirds of the wall clock and its numbers are the least trustworthy
    /// (see the report's self-check), so it is opt-in rather than the default a change has to pay for.
    /// </remarks>
    internal static IReadOnlyList<int> Selected { get; private set; } = [Small, Medium, Large];

    /// <summary>Reads this tool's own switches and leaves BenchmarkDotNet's arguments alone.</summary>
    /// <remarks>
    /// <c>--all</c> adds the huge magnitude; <c>--scale 10000</c> (or <c>--scale=10000</c>) narrows the run to one.
    /// The latter is the "just measure one number" path — pair it with <c>--filter</c> and a run is seconds.
    /// </remarks>
    /// <param name="args">The command line.</param>
    /// <returns>The arguments BenchmarkDotNet should see.</returns>
    internal static string[] Apply(string[] args)
    {
        // 自己认的开关必须摘掉再交给 BenchmarkDotNet，否则它会当成未知参数直接报错。
        var rest = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], AllSwitch, StringComparison.OrdinalIgnoreCase))
            {
                Selected = [Small, Medium, Large, Huge];
                continue;
            }

            if (string.Equals(args[i], ScaleSwitch, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var byTwo))
            {
                Selected = [byTwo];
                i++;
                continue;
            }

            if (args[i].StartsWith(ScaleSwitch + "=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i][(ScaleSwitch.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var byEquals))
            {
                Selected = [byEquals];
                continue;
            }

            rest.Add(args[i]);
        }

        return [.. rest];
    }

    private const string AllSwitch = "--all";
    private const string ScaleSwitch = "--scale";
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
