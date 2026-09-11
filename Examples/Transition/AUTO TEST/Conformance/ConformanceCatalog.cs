namespace VeloxDev.AT.Conformance;

/// <summary>
/// The register of platforms a sampler-conformance table exists for.
/// </summary>
/// <remarks>
/// One line per platform, so a new one is registered here and its suite entry is all that is left to add. A platform
/// that ships samplers but has no table here is invisible to the suite — which is why the suite's coverage guard
/// compares the demo's report against the table rather than trusting either side alone.
/// </remarks>
internal static class ConformanceCatalog
{
    /// <summary>
    /// The eased times every sampler is driven at. Shared with the pure-data suite so a failure on either side names
    /// the same time; all are exactly representable in binary, so the grid itself adds no error.
    /// </summary>
    internal static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

    private static readonly Dictionary<string, Func<IReadOnlyList<ConformanceEntry>>> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WpfConformance.Platform] = () => WpfConformance.All,
            [WinFormsConformance.Platform] = () => WinFormsConformance.All,
            [BlazorConformance.Platform] = () => BlazorConformance.All,
            [MauiConformance.Platform] = () => MauiConformance.All,
            [WinUiConformance.Platform] = () => WinUiConformance.All,
            [JaliumConformance.Platform] = () => JaliumConformance.All,
            [AvaloniaConformance.Platform] = () => AvaloniaConformance.All,
        };

    /// <summary>The platforms a conformance suite can run on.</summary>
    internal static IReadOnlyList<string> Platforms => [.. Registry.Keys];

    /// <summary>Whether a table exists for this platform.</summary>
    internal static bool Has(string platform) => Registry.ContainsKey(platform);

    /// <summary>The table for a platform.</summary>
    /// <exception cref="InvalidOperationException">No table is registered for that platform.</exception>
    internal static IReadOnlyList<ConformanceEntry> For(string platform)
        => Registry.TryGetValue(platform, out var create)
            ? create()
            : throw new InvalidOperationException(
                $"No sampler-conformance table is registered for '{platform}'. Registered: {string.Join(", ", Registry.Keys)}.");
}
