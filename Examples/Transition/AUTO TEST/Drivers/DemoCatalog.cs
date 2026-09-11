namespace VeloxDev.AT.Drivers;

/// <summary>
/// The register of platforms an acceptance driver exists for, and the gate every UI suite goes through.
/// </summary>
internal static class DemoCatalog
{
    private static readonly (string Platform, Func<IDemoDriver> Create)[] Registry =
    [
        // 新平台在这里登记一行，探测套件与门禁就自动覆盖到它。
        ("WPF", static () => new WpfDemoDriver()),
        ("WinForms", static () => new WinFormsDemoDriver()),
        ("Avalonia", static () => new AvaloniaDemoDriver()),
        ("MAUI", static () => new MauiDemoDriver()),
        ("WinUI", static () => new WinUIDemoDriver()),
        ("Jalium", static () => new JaliumDemoDriver()),
        ("Blazor", static () => new BlazorDemoDriver()),
    ];

    /// <summary>The registered platforms, narrowed to <c>VELOXDEV_AT_PLATFORMS</c> when that is set.</summary>
    internal static IReadOnlyList<string> Platforms =>
        [.. Registry.Select(entry => entry.Platform).Where(AtConfig.RunsOn)];

    /// <summary>Create the driver for a platform.</summary>
    /// <exception cref="InvalidOperationException">No driver is registered for that platform.</exception>
    internal static IDemoDriver Create(string platform)
    {
        foreach (var entry in Registry)
        {
            if (string.Equals(entry.Platform, platform, StringComparison.OrdinalIgnoreCase)) return entry.Create();
        }

        throw new InvalidOperationException(
            $"No acceptance driver is registered for '{platform}'. Registered: {string.Join(", ", Registry.Select(entry => entry.Platform))}.");
    }

    /// <summary>
    /// Skip the calling test unless the suites are switched on and this platform is in scope.
    /// </summary>
    /// <remarks>
    /// Called at the top of every UI test rather than once in class setup, on purpose: a skip raised from a class
    /// initialiser is a property of the fixture and some runners report it as a failure, whereas a skip raised from
    /// the test itself is unambiguously a skip.
    /// </remarks>
    internal static void RequireEnabled(string platform)
    {
        if (!AtConfig.Enabled)
        {
            Assert.Inconclusive("VELOXDEV_AT is not 1. The UI suites need an interactive desktop, so they stay off unless asked for.");
        }

        if (!AtConfig.RunsOn(platform))
        {
            Assert.Inconclusive($"{platform} is not in VELOXDEV_AT_PLATFORMS.");
        }
    }
}
