using System.IO;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Where a run puts everything it produces.</summary>
/// <remarks>
/// <para>
/// Under the project, not under whatever directory the process happened to start in. BenchmarkDotNet's default is
/// <c>BenchmarkDotNet.Artifacts</c> relative to the working directory, so a run launched from the repository root
/// scattered its output there instead of beside the project that produced it.
/// </para>
/// <para>
/// Resolved from the assembly's own location (<c>bin/Debug/net10.0</c>, three levels up) rather than from the
/// working directory, so it lands in the same place however the executable was started. The directory is ignored
/// by the repository — <c>BenchmarkDotNet.Artifacts/</c> in <c>.gitignore</c> matches at any depth.
/// </para>
/// </remarks>
internal static class Artifacts
{
    /// <summary>The Markdown report a run leaves behind.</summary>
    internal const string ReportFileName = "serialization-performance.md";

    /// <summary>The absolute path of this project's artifacts directory.</summary>
    internal static string Path { get; } = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BenchmarkDotNet.Artifacts"));
}
