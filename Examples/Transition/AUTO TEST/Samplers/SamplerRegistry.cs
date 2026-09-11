using System.Collections.ObjectModel;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.SamplerTest;

/// <summary>
/// Every sampler the library ships, one entry each, with the closed form it has to satisfy.
/// </summary>
/// <remarks>
/// One file per adapter feeds this, so the tables can be written and reviewed independently. The list is not a
/// convenience: <see cref="SamplerCoverageTests"/> reflects over every <see cref="ISampler"/> in the product
/// assemblies and fails if one of them is missing from here, so a sampler added without a table entry breaks the
/// build rather than going untested.
/// </remarks>
internal static class SamplerRegistry
{
    internal static IReadOnlyList<SamplerEntry> Entries { get; } = new ReadOnlyCollection<SamplerEntry>(
    [
        .. CoreSamplerEntries.All,
        .. AdapterSamplerEntries.All,
    ]);
}
