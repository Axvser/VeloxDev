using VeloxDev.Benchmarks;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Where a run puts everything it produces.</summary>
/// <remarks>
/// Under the OS temp area, not the repository. BenchmarkDotNet's default is <c>BenchmarkDotNet.Artifacts</c>
/// relative to the working directory, so a run from the repository root or from the project would leave files
/// in the tree. The path does not depend on where the process started.
/// </remarks>
internal static class Artifacts
{
    /// <summary>The Markdown report a run leaves behind.</summary>
    internal const string ReportFileName = "serialization-performance.md";

    /// <summary>The absolute path of this project's artifacts directory.</summary>
    internal static string Path { get; } = BenchmarkArtifacts.For("serialization");
}
