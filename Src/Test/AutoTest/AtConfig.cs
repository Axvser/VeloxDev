namespace VeloxDev.AT;

/// <summary>
/// Where the demos live and how the acceptance suites are gated.
/// </summary>
internal static class AtConfig
{
    /// <summary>
    /// Set <c>VELOXDEV_AT=1</c> to let the UI suites run. Without it they skip, because they need an interactive
    /// desktop and would otherwise hang or fail on any headless agent. The theory suite ignores this: it needs
    /// nothing but arithmetic and is worth running anywhere.
    /// </summary>
    internal static bool Enabled => Environment.GetEnvironmentVariable("VELOXDEV_AT") == "1";

    /// <summary>
    /// How long to linger after **every** click a suite makes — sampler handles and load-mode buttons alike. Unset
    /// means <see cref="DefaultPaceMs"/>; set <c>VELOXDEV_AT_PACE</c> to a number of milliseconds, and to <c>0</c>
    /// for no pause at all.
    /// </summary>
    /// <remarks>
    /// <b>Slowed down by default, on purpose.</b> Two reasons, and the second is not cosmetic: a demo window is
    /// otherwise gone before the eye catches it, and — because each click stops the previous one's animation — a run
    /// with no pause cuts every sampler's run off a tenth of a second in, so nobody watching ever sees one finish.
    /// <para>
    /// It applies to <see cref="Drivers.DemoDriverBase.Click"/>, so it covers the load-mode row's buttons as well as
    /// the sampler handles; raising it is what turns a run into something a person can follow end to end. A CI job or
    /// an agent that only wants the verdict sets <c>VELOXDEV_AT_PACE=0</c>.
    /// </para>
    /// </remarks>
    internal static TimeSpan Pace =>
        int.TryParse(Environment.GetEnvironmentVariable("VELOXDEV_AT_PACE"), out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Max(0, milliseconds))
            : TimeSpan.FromMilliseconds(DefaultPaceMs);

    /// <summary>Long enough for the bench to finish playing one sampler before the next click interrupts it.</summary>
    private const int DefaultPaceMs = 800;

    /// <summary>
    /// How long to hold at the start of **each case**, after the banner naming it. Unset means
    /// <see cref="DefaultObserveMs"/>; set <c>VELOXDEV_AT_OBSERVE</c> to a number of milliseconds, and to <c>0</c>
    /// to move straight on.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Pace"/>, because the two answer different questions. The pace is the gap
    /// between two clicks <em>inside</em> one case, so it has to stay short or a case that clicks a dozen times takes
    /// a minute apiece. This is the gap <em>between</em> cases: long enough to read which one is about to run and to
    /// see what the last one left on screen, which is what turns a run from a blur with a verdict into something
    /// followable.
    /// <para>
    /// The demos themselves are started once per platform for the whole run rather than per suite, which removes the
    /// other thing that made a run hard to watch: the window vanishing and a new one appearing between cases.
    /// </para>
    /// </remarks>
    internal static TimeSpan Observe =>
        int.TryParse(Environment.GetEnvironmentVariable("VELOXDEV_AT_OBSERVE"), out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Max(0, milliseconds))
            : TimeSpan.FromMilliseconds(DefaultObserveMs);

    private const int DefaultObserveMs = 1200;

    /// <summary>
    /// Comma-separated subset to run, e.g. <c>VELOXDEV_AT_PLATFORMS=WPF,Blazor</c>. Empty or unset means all.
    /// </summary>
    internal static bool RunsOn(string platform)
    {
        var requested = Environment.GetEnvironmentVariable("VELOXDEV_AT_PLATFORMS");
        if (string.IsNullOrWhiteSpace(requested)) return true;

        return requested
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(platform, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The repository root, found by walking up for the solution file rather than by counting <c>..</c> segments,
    /// so the suites survive being moved inside the tree.
    /// </summary>
    internal static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VeloxDev.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException(
                    $"VeloxDev.slnx was not found above {AppContext.BaseDirectory}; the acceptance suites locate the demos through it.");
        }
    }

    /// <summary>
    /// The path of a demo's executable, relative to the repository root. <c>VELOXDEV_AT_DEMO_ROOT</c> overrides the
    /// root so an agent can point at a publish output instead of a build output.
    /// </summary>
    internal static string DemoExecutable(string relativePath)
    {
        var root = Environment.GetEnvironmentVariable("VELOXDEV_AT_DEMO_ROOT");
        return Path.Combine(string.IsNullOrWhiteSpace(root) ? RepositoryRoot : root, relativePath);
    }
}
