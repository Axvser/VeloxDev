using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

// 两家都有 `JsonSerializer`，所以一律走别名 —— 这个文件里必须一眼看出哪一行是谁的。
using Stj = System.Text.Json.JsonSerializer;
using StjOptions = System.Text.Json.JsonSerializerOptions;
using StjReferences = System.Text.Json.Serialization.ReferenceHandler;
using Nst = Newtonsoft.Json.JsonConvert;
using NstSettings = Newtonsoft.Json.JsonSerializerSettings;
using NstReferences = Newtonsoft.Json.PreserveReferencesHandling;
using NstTypes = Newtonsoft.Json.TypeNameHandling;
using NstFormat = Newtonsoft.Json.Formatting;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// <c>System.Text.Json</c>'s source-generated metadata for the corpus root.
/// </summary>
/// <remarks>
/// Without it the comparison would be against the slowest configuration of System.Text.Json — reflection — while
/// the archive engine is generated code. This is the fair counterpart, and the one Microsoft recommends.
/// </remarks>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    ReferenceHandler = System.Text.Json.Serialization.JsonKnownReferenceHandler.Preserve,
    WriteIndented = true,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[System.Text.Json.Serialization.JsonSerializable(typeof(VeloxDev.WorkflowSystem.TreeDefaultViewModel))]
internal partial class CorpusJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

/// <summary>
/// The same graph through three serializers, so "is this fast" has an answer that is not a guess.
/// </summary>
/// <remarks>
/// <para>
/// All three are configured to <b>preserve references</b>, because that is what the archive format does on every
/// document whether or not it is asked to: <c>ReferenceHandler.Preserve</c> for System.Text.Json,
/// <c>PreserveReferencesHandling.Objects</c> for Newtonsoft. Newtonsoft also gets
/// <c>TypeNameHandling.Auto</c>, its counterpart of the <c>$type</c> the archive writes for a polymorphic member.
/// Without those the comparison would be against serializers doing a different job.
/// </para>
/// <para>
/// <b>What this does not equalise:</b> which members each one writes. The archive writes the generated contract;
/// System.Text.Json and Newtonsoft write the public surface as they find it. So the documents differ in size, and
/// the times are "what each does on this graph" rather than "the same bytes, three ways". The setup prints the
/// three sizes so the difference can at least be seen.
/// </para>
/// </remarks>
[Config(typeof(BenchmarkConfig))]
public class ComparisonBenchmarks
{
    /// <summary>How many nodes the corpus tree holds.</summary>
    [Params(1000)]
    public int NodeCount { get; set; }

    private TreeDefaultViewModel _tree = null!;
    private string _archive = null!;
    private string _systemTextJson = null!;
    private string _newtonsoft = null!;

    private StjOptions _stj = null!;
    private NstSettings _nst = null!;

    /// <summary>Builds the graph and each serializer's document, and prints their sizes.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _tree = Corpus.BuildTree(NodeCount);

        _stj = new StjOptions
        {
            ReferenceHandler = StjReferences.Preserve,
            WriteIndented = true,
            // 不加这一条，System.Text.Json **默认设置下根本写不了这棵树** —— 它对着 NaN/±Infinity 抛
            // `ArgumentException`。这套格式把它们写成字符串，Newtonsoft 默认也写字符串。
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        _nst = new NstSettings
        {
            PreserveReferencesHandling = NstReferences.Objects,
            TypeNameHandling = NstTypes.Auto,
            Formatting = NstFormat.Indented,
        };

        _archive = VeloxJsonSerializer.Serialize(_tree);
        _systemTextJson = Stj.Serialize(_tree, _stj);
        _newtonsoft = Nst.SerializeObject(_tree, _nst);

        Console.WriteLine();
        Console.WriteLine($"[{NodeCount} nodes] document sizes");
        Console.WriteLine($"  archive    : {_archive.Length,12:N0} chars");
        Console.WriteLine($"  stj        : {_systemTextJson.Length,12:N0} chars");
        Console.WriteLine($"  newtonsoft : {_newtonsoft.Length,12:N0} chars");
        Console.WriteLine();
    }

    /// <summary>Writes the graph with this library's archive engine.</summary>
    /// <returns>The document.</returns>
    [Benchmark(Baseline = true)]
    public string Archive_Serialize() => VeloxJsonSerializer.Serialize(_tree);

    /// <summary>Reads the graph back with this library's archive engine.</summary>
    /// <returns>The graph, or the JSON literal's <see langword="null"/>.</returns>
    [Benchmark]
    public TreeDefaultViewModel? Archive_Deserialize() => VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(_archive);

    /// <summary>Writes the graph with <c>System.Text.Json</c>.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string Stj_Serialize() => Stj.Serialize(_tree, _stj);

    /// <summary>Reads the graph back with <c>System.Text.Json</c>.</summary>
    /// <returns>The graph.</returns>
    /// <remarks>
    /// <b>Deliberately not a benchmark: it does not work.</b> These ViewModels are primary-constructor classes whose
    /// promoted properties are named differently from the constructor's parameters — <c>Offset(double left,
    /// double top)</c> exposes <c>Horizontal</c> and <c>Vertical</c> — and System.Text.Json refuses a constructor
    /// whose parameters bind to nothing:
    /// <c>Each parameter in the deserialization constructor on type 'VeloxDev.WorkflowSystem.Offset' must bind to
    /// an object property or field on deserialization.</c> <c>IncludeFields</c> does not help; the fields are
    /// <c>_horizontal</c>/<c>_vertical</c>. Reading this library's own graph with System.Text.Json would take
    /// annotating the framework's types or writing converters for them — which is precisely the work the archive
    /// format does at compile time instead. Serialization, which does not consult constructors, works.
    /// </remarks>
    public TreeDefaultViewModel? Stj_Deserialize() => Stj.Deserialize<TreeDefaultViewModel>(_systemTextJson, _stj);

    /// <summary>Writes the graph with <c>System.Text.Json</c> and its source-generated metadata.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string StjSourceGen_Serialize()
        => Stj.Serialize(_tree, CorpusJsonContext.Default.TreeDefaultViewModel);

    /// <summary>Writes the graph with Newtonsoft.Json.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string Nst_Serialize() => Nst.SerializeObject(_tree, _nst);

    /// <summary>Reads the graph back with Newtonsoft.Json.</summary>
    /// <returns>The graph.</returns>
    [Benchmark]
    public TreeDefaultViewModel? Nst_Deserialize() => Nst.DeserializeObject<TreeDefaultViewModel>(_newtonsoft, _nst);
}
