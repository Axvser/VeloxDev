using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>Times the archive engine reading and writing a tree at three sizes.</summary>
/// <remarks>
/// Each case writes and reads the <i>same</i> graph, so a change that speeds writing up by making the reader's
/// work harder shows as a loss on the other half rather than as a win. <see cref="NodeCount"/> is the only
/// parameter: the corpus holds both strings and numbers already, and the engine's cost per node is what the
/// sizes are meant to separate.
/// </remarks>
[Config(typeof(BenchmarkConfig))]
public class SerializationBenchmarks
{
    /// <summary>How many nodes the corpus tree holds.</summary>
    [Params(2, 1000, 10000)]
    public int NodeCount { get; set; }

    private TreeDefaultViewModel _tree = null!;
    private string _json = null!;

    /// <summary>Builds the corpus and the document, once per parameter value.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _tree = Corpus.BuildTree(NodeCount);
        _json = VeloxJsonSerializer.Serialize(_tree);
    }

    /// <summary>Writes the corpus as a document.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string Serialize() => VeloxJsonSerializer.Serialize(_tree);

    /// <summary>Reads the document back into a graph.</summary>
    /// <returns>The graph, or <see langword="null"/> when the document held the JSON literal.</returns>
    [Benchmark]
    public TreeDefaultViewModel? Deserialize() => VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(_json);
}
