namespace VeloxDev.AT.LoadMode;

/// <summary>
/// The register of platforms whose load-mode row is under test.
/// </summary>
/// <remarks>
/// One line per platform. A platform in the demo register but not here is a silent gap — the suite iterates this
/// table, so a missing entry means its load modes are simply never driven.
/// </remarks>
internal static class LoadModeCatalog
{
    private static readonly Dictionary<string, Func<LoadModeEntry>> Registry =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WpfLoadMode.Platform] = () => WpfLoadMode.Entry,
            [JaliumLoadMode.Platform] = () => JaliumLoadMode.Entry,
            [BlazorLoadMode.Platform] = () => BlazorLoadMode.Entry,
            [MauiLoadMode.Platform] = () => MauiLoadMode.Entry,
            [WinFormsLoadMode.Platform] = () => WinFormsLoadMode.Entry,
            [WinUiLoadMode.Platform] = () => WinUiLoadMode.Entry,
            [AvaloniaLoadMode.Platform] = () => AvaloniaLoadMode.Entry,
        };

    /// <summary>The platforms a load-mode suite can run on.</summary>
    internal static IReadOnlyList<string> Platforms => [.. Registry.Keys];

    /// <summary>Whether a table exists for this platform.</summary>
    internal static bool Has(string platform) => Registry.ContainsKey(platform);

    /// <summary>The entry for a platform.</summary>
    /// <exception cref="InvalidOperationException">No table is registered for that platform.</exception>
    internal static LoadModeEntry For(string platform)
        => Registry.TryGetValue(platform, out var create)
            ? create()
            : throw new InvalidOperationException(
                $"No load-mode table is registered for '{platform}'. Registered: {string.Join(", ", Registry.Keys)}.");
}
