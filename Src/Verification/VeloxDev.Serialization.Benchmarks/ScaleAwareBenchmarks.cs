using BenchmarkDotNet.Attributes;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// The one thing the two benchmark classes share: which data magnitudes to run, decided on the command line.
/// </summary>
/// <remarks>
/// <c>[Params]</c> would fix the magnitudes at compile time, so every run would pay for the huge scale. Reading
/// them from <see cref="Scales.Selected"/> through <c>[ParamsSource]</c> keeps all four available without making
/// all four mandatory. BenchmarkDotNet wants the source on the benchmark type itself, which is what this base is.
/// </remarks>
public abstract class ScaleAwareBenchmarks
{
    /// <summary>The magnitudes this run measures — <see cref="Scales.Selected"/>.</summary>
    public static IReadOnlyList<int> NodeCounts => Scales.Selected;

    /// <summary>How many nodes the corpus tree holds.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int NodeCount { get; set; }
}
