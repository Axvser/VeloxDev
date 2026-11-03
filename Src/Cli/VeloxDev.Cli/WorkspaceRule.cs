namespace VeloxDev.Cli;

/// <summary>
/// The optional path sandbox. When the host names a workspace, every file this process reads or writes must be
/// below it — the archive, the manifest, dependency assemblies, checkpoints, logs and <c>SaveArchive</c> targets.
/// <para>
/// It is deliberately opt-in: the default host has no notion of a project root, and imposing one would break the
/// legitimate "run an archive from anywhere" use. A host that launches the server for one project should set it.
/// </para>
/// </summary>
internal static class WorkspaceRule
{
    /// <summary>Normalizes and validates the workspace root itself.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"The workspace directory does not exist: {full}");

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Refuses a path outside the workspace.</summary>
    /// <exception cref="InvalidOperationException">The path escapes the workspace.</exception>
    public static void Ensure(string? workspace, string? path, string what)
    {
        if (workspace is null || string.IsNullOrWhiteSpace(path)) return;

        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var prefix = workspace + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, comparison))
            throw new InvalidOperationException(
                $"{what} '{full}' is outside the workspace '{workspace}'. Start the process without --workspace to lift this restriction.");
    }
}
