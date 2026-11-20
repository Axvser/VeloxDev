using System.IO;

namespace VeloxDev.Benchmarks;

/// <summary>Where benchmark and harness runs put their files.</summary>
/// <remarks>
/// Always the OS temp area, never the repository: BenchmarkDotNet's default is relative to the working
/// directory, so a run started from the repository root would otherwise leave <c>BenchmarkDotNet.Artifacts</c>
/// there, and a run started from a project would leave it among the sources.
/// </remarks>
internal static class BenchmarkArtifacts
{
    /// <summary>The directory one benchmark project writes to.</summary>
    /// <param name="project">A stable short name, e.g. <c>serialization</c> or <c>adapters-razor</c>.</param>
    internal static string For(string project)
        => Path.Combine(Path.GetTempPath(), "VeloxDev.Benchmarks", project);
}
