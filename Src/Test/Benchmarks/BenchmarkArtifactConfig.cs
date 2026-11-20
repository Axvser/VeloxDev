using BenchmarkDotNet.Configs;

namespace VeloxDev.Benchmarks;

/// <summary>A default BenchmarkDotNet config whose only change is the artifacts directory.</summary>
internal static class BenchmarkArtifactConfig
{
    /// <summary>Builds the config for one benchmark project.</summary>
    /// <param name="project">A stable short name, passed to <see cref="BenchmarkArtifacts.For"/>.</param>
    internal static IConfig Create(string project)
        => ManualConfig.Create(DefaultConfig.Instance).WithArtifactsPath(BenchmarkArtifacts.For(project));
}
