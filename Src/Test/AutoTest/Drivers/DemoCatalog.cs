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

    /// <summary>
    /// The demos this run has started, one per platform, kept alive until the assembly teardown.
    /// </summary>
    /// <remarks>
    /// Safe as a plain dictionary because the assembly is not parallelized: <see cref="For"/> is only ever entered
    /// by one test at a time.
    /// </remarks>
    private static readonly Dictionary<string, IDemoDriver> Live = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registered platforms, narrowed to <c>VELOXDEV_AT_PLATFORMS</c> when that is set.</summary>
    internal static IReadOnlyList<string> Platforms =>
        [.. Registry.Select(entry => entry.Platform).Where(AtConfig.RunsOn)];

    /// <summary>
    /// Every registered platform, ignoring <c>VELOXDEV_AT_PLATFORMS</c>.
    /// </summary>
    /// <remarks>
    /// For the coverage guards, which ask what the project *can* drive rather than what this run was narrowed to.
    /// Comparing a full catalog against the narrowed list makes every excluded platform look like a table nobody
    /// runs — which is exactly what happened the first time the suites were run for a subset of platforms.
    /// </remarks>
    internal static IReadOnlyList<string> Registered =>
        [.. Registry.Select(entry => entry.Platform)];

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
    /// The driver for a platform, launched on first use and then kept for the whole run.
    /// </summary>
    /// <remarks>
    /// One process per platform rather than one per test. Starting a demo takes seconds — it is the single largest
    /// cost in the run, and every suite that drives the same platform used to pay it again: WPF was started three
    /// times and Blazor four. The launch is what a person waits through, and it is also the jarring part of
    /// watching the suite, so paying it once per platform is what makes the run something you can sit through.
    /// <para>
    /// The suite is already serial assembly-wide (see <c>AssemblyInfo</c>), so a shared driver has exactly one user
    /// at a time. What it must no longer assume is a fresh process: a suite has to bring the demo back to its rest
    /// state itself, which is what <see cref="IDemoDriver.Settle"/> is for. That was already the shape these suites
    /// used — each one reset before it measured — so this makes an existing habit the stated contract rather than
    /// introducing a new one.
    /// </para>
    /// </remarks>
    internal static IDemoDriver For(string platform)
    {
        if (Live.TryGetValue(platform, out var existing))
        {
            return existing;
        }

        var driver = Create(platform);
        driver.Launch();
        Live[platform] = driver;
        return driver;
    }

    /// <summary>
    /// Open the platform's demo for the duration of one suite class, or return <c>null</c> when the suites are
    /// switched off or this platform is out of scope.
    /// </summary>
    /// <remarks>
    /// Deliberately returns null rather than skipping. A skip raised from a class initialiser is a property of the
    /// fixture, and some runners report it as a failure, whereas a skip raised from a test body is unambiguously a
    /// skip — so this only decides whether a demo is worth opening, and <see cref="RequireEnabled"/> still does the
    /// skipping, from the test.
    /// </remarks>
    internal static IDemoDriver? TryOpen(string platform)
    {
        if (!AtConfig.Enabled || !AtConfig.RunsOn(platform))
        {
            return null;
        }

        return For(platform);
    }

    /// <summary>
    /// Close one platform's demo.
    /// </summary>
    /// <remarks>
    /// Called when a platform's suite class finishes, so exactly one demo is open at a time. That is what makes the
    /// run followable: a person watching should see a window open, be driven through everything that platform has,
    /// and close before the next one appears — not seven windows accumulate and then get visited in turn.
    /// </remarks>
    internal static void Release(string platform)
    {
        if (!Live.Remove(platform, out var driver))
        {
            return;
        }

        try
        {
            driver.Dispose();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[AT] failed to close the {platform} demo: {exception.Message}");
        }
    }

    /// <summary>Stop every demo this run started. Called from the assembly teardown.</summary>
    internal static void Shutdown()
    {
        foreach (var driver in Live.Values)
        {
            try
            {
                driver.Dispose();
            }
            catch (Exception exception)
            {
                // Teardown must not turn a clean run into a failed one; the process janitor below is the backstop.
                Console.WriteLine($"[AT] failed to close the {driver.Platform} demo: {exception.Message}");
            }
        }

        Live.Clear();
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
