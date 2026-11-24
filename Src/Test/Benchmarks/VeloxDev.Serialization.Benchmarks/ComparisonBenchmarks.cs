using BenchmarkDotNet.Attributes;
using VeloxDev.WorkflowSystem;

// 两家都有 `JsonSerializer`，所以一律走别名 —— 这个文件里必须一眼看出哪一行是谁的。
using Stj = System.Text.Json.JsonSerializer;
using StjOptions = System.Text.Json.JsonSerializerOptions;
using StjReferences = System.Text.Json.Serialization.ReferenceHandler;
using Metadata = System.Text.Json.Serialization.Metadata;
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
/// four sizes so the difference can at least be seen.
/// </para>
/// </remarks>
[Config(typeof(BenchmarkConfig))]
public class ComparisonBenchmarks : ScaleAwareBenchmarks
{

    private TreeDefaultViewModel _tree = null!;
    private string _archive = null!;
    private string _systemTextJson = null!;
    private string _systemTextJsonSourceGen = null!;
    private string _newtonsoft = null!;

    private StjOptions _stj = null!;
    private StjOptions _stjSourceGen = null!;
    private NstSettings _nst = null!;

    /// <summary>Builds the graph and each serializer's document, checks every read reproduces it, prints the sizes.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _tree = Corpus.Shared(NodeCount);

        _stj = StjOptions();

        // 源生成那一路：上下文先答，答不上的（多态契约 —— 源生成要靠类型自己贴 `[JsonDerivedType]`，而
        // 这些是框架类型，贴不了）落到反射。绝大多数类型仍走源生成，这一行问的「元数据从哪来」还在。
        _stjSourceGen = StjOptions();
        _stjSourceGen.TypeInfoResolver = Metadata.JsonTypeInfoResolver
            .Combine(CorpusJsonContext.Default, new Metadata.DefaultJsonTypeInfoResolver())
            .WithBridge();

        _nst = new NstSettings
        {
            PreserveReferencesHandling = NstReferences.Objects,
            TypeNameHandling = NstTypes.Auto,
            Formatting = NstFormat.Indented,
        };

        _archive = VeloxJsonSerializer.Serialize(_tree);
        _systemTextJson = Stj.Serialize(_tree, _stj);
        _systemTextJsonSourceGen = Stj.Serialize(_tree, _stjSourceGen);
        _newtonsoft = Nst.SerializeObject(_tree, _nst);

        DocumentSizes.Record(
            NodeCount,
            _archive.Length,
            _systemTextJson.Length,
            _systemTextJsonSourceGen.Length,
            _newtonsoft.Length);

        // 读得出数不等于读对了 —— 先核一遍再量，理由见那个方法。
        VerifyEveryReadReconstructs();

        Console.WriteLine();
        Console.WriteLine($"[{Scales.NameOf(NodeCount)} · {NodeCount} nodes] document sizes");
        Console.WriteLine($"  archive       : {_archive.Length,12:N0} chars");
        Console.WriteLine($"  stj           : {_systemTextJson.Length,12:N0} chars");
        Console.WriteLine($"  stj (srcgen)  : {_systemTextJsonSourceGen.Length,12:N0} chars");
        Console.WriteLine($"  newtonsoft    : {_newtonsoft.Length,12:N0} chars");
        Console.WriteLine();
    }

    // 两家共用的设置。源生成那一行换的只是**元数据从哪来**，所以其余设置必须逐字相同 —— 否则它比的就是
    // 别的东西了。
    private static StjOptions StjOptions()
    {
        var options = new StjOptions
        {
            ReferenceHandler = StjReferences.Preserve,
            WriteIndented = true,
            // 不加这一条，System.Text.Json **默认设置下根本写不了这棵树** —— 它对着 NaN/±Infinity 抛
            // `ArgumentException`。这套格式把它们写成字符串，Newtonsoft 默认也写字符串。
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        options.TypeInfoResolver = new Metadata.DefaultJsonTypeInfoResolver().WithBridge();
        return options;
    }

    /// <summary>
    /// Reads the graph back with every serializer and throws when the result is not the same graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read that fails outright is obvious — the case produces an error instead of a number. A read that
    /// <i>succeeds but drops things</i> is the dangerous one: it produces a fast number for work that was never
    /// done, and nothing in the table would say so. This is the guard against that, and it runs before the
    /// documents are measured rather than after.
    /// </para>
    /// <para>
    /// Cached per magnitude: <c>[GlobalSetup]</c> runs once per benchmark case, so an eight-case class would
    /// otherwise re-read a 30 000-node graph eight times for a check whose answer cannot change.
    /// </para>
    /// </remarks>
    private void VerifyEveryReadReconstructs()
    {
        if (!Verified.Add(NodeCount)) return;

        Check("archive", VeloxJsonSerializer.Deserialize<TreeDefaultViewModel>(_archive));
        Check("stj", Stj.Deserialize<TreeDefaultViewModel>(_systemTextJson, _stj));
        Check("stj (srcgen)", Stj.Deserialize<TreeDefaultViewModel>(_systemTextJsonSourceGen, _stjSourceGen));
        Check("newtonsoft", Nst.DeserializeObject<TreeDefaultViewModel>(_newtonsoft, _nst));

        void Check(string who, TreeDefaultViewModel? restored)
        {
            if (restored is null) throw new InvalidOperationException($"{who}: the read produced null.");

            if (restored.Nodes.Count != _tree.Nodes.Count)
            {
                throw new InvalidOperationException(
                    $"{who}: read back {restored.Nodes.Count} nodes, the graph has {_tree.Nodes.Count} — " +
                    "a read that drops objects would be timed as a win.");
            }

            if (restored.Links.Count != _tree.Links.Count)
            {
                throw new InvalidOperationException(
                    $"{who}: read back {restored.Links.Count} links, the graph has {_tree.Links.Count}.");
            }
        }
    }

    /// <summary>The magnitudes whose four reads have already been checked.</summary>
    private static readonly HashSet<int> Verified = [];

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
    /// <para>
    /// Out of the box this throws, twice over, and both are written down here because they are what the bridge
    /// exists for. First: these ViewModels are primary-constructor classes whose promoted properties are named
    /// differently from the constructor's parameters — <c>Offset(double left, double top)</c> exposes
    /// <c>Horizontal</c> and <c>Vertical</c> — and System.Text.Json refuses a constructor whose parameters bind to
    /// nothing. <c>IncludeFields</c> does not help; the fields are <c>_horizontal</c>/<c>_vertical</c>. Second,
    /// once that is answered: the graph holds interface-typed members such as <c>VirtualLink</c>, and
    /// System.Text.Json will not read one without a discriminator saying which implementation the document holds.
    /// </para>
    /// <para>
    /// Both are answered in <see cref="StjSerializationBridge"/> with configuration rather than converters, which
    /// is the only thing that keeps this row comparable. What is deliberately <i>not</i> exercised: the tree's
    /// interface-keyed <c>LinksMap</c> is empty in this corpus, and a populated one would need a converter.
    /// </para>
    /// </remarks>
    [Benchmark]
    public TreeDefaultViewModel? Stj_Deserialize() => Stj.Deserialize<TreeDefaultViewModel>(_systemTextJson, _stj);

    /// <summary>Writes the graph with <c>System.Text.Json</c> and its source-generated metadata.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string StjSourceGen_Serialize() => Stj.Serialize(_tree, _stjSourceGen);

    /// <summary>Reads the graph back with <c>System.Text.Json</c> and its source-generated metadata.</summary>
    /// <returns>The graph.</returns>
    [Benchmark]
    public TreeDefaultViewModel? StjSourceGen_Deserialize()
        => Stj.Deserialize<TreeDefaultViewModel>(_systemTextJsonSourceGen, _stjSourceGen);

    /// <summary>Writes the graph with Newtonsoft.Json.</summary>
    /// <returns>The document.</returns>
    [Benchmark]
    public string Nst_Serialize() => Nst.SerializeObject(_tree, _nst);

    /// <summary>Reads the graph back with Newtonsoft.Json.</summary>
    /// <returns>The graph.</returns>
    [Benchmark]
    public TreeDefaultViewModel? Nst_Deserialize() => Nst.DeserializeObject<TreeDefaultViewModel>(_newtonsoft, _nst);
}
