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
