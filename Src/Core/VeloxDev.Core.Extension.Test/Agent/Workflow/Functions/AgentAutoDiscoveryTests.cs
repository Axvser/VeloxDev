using Demo.ViewModels;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Threading;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Core.Extension.Test.Discovery;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions
{
/// <summary>
/// Direct coverage of <see cref="WorkflowAgentScope.WithAutoDiscovery"/>: type classification
/// (components / enums / interfaces / data), deep-scan member inference (generic unwrapping,
/// [SlotSelectors], [AgentCommandParameter]), framework-builtin exclusion, cross-language
/// global dedup, and the assembly-by-name overload.
/// </summary>
[TestClass]
public class AgentAutoDiscoveryTests
{
    private static WorkflowAgentScope NewScope() => new(new TreeDefaultViewModel());

    // ── Lib assembly: real customer components + [SlotSelectors] + [AgentContext] data ──

    [TestMethod]
    public void LibAssembly_RegistersComponentsEnumsAndAgentContextData()
    {
        var scope = NewScope().WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);

        var context = scope.ProvideCustomerContext(AgentLanguages.English);

        // Concrete workflow components (node / slot / link / tree).
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.NodeViewModel"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.EnumSelectorNodeViewModel"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.BoolSelectorNodeViewModel"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.SlotViewModel"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.LinkViewModel"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.TreeViewModel"));

        // Enums extracted from [SlotSelectors] on SlotEnumerator properties.
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.NetworkRequestMethod"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.VoltageRange"));
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.ModelProtocol"));

        // [AgentContext]-annotated data class (ISlotProvider selector).
        Assert.IsTrue(scope.ProvideCustomerDataContext(AgentLanguages.English)
            .Contains("Type: Demo.ViewModels.CustomRouteSelector"));
    }

    [TestMethod]
    public void LibAssembly_ExcludesFrameworkTypesFromCustomerContext()
    {
        var scope = NewScope().WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);

        var context = scope.ProvideCustomerContext(AgentLanguages.English);
        var data = scope.ProvideCustomerDataContext(AgentLanguages.English);

        // Framework data / components / interfaces must never surface as customer types.
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.Anchor"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.NodeDefaultViewModel"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.IWorkflowNodeViewModel"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.MVVM.IVeloxCommand"),
            "the framework command interface must not leak into customer interfaces");
        Assert.IsFalse(data.Contains("Type: VeloxDev.WorkflowSystem.TaskContext"));

        // CompilerEx plumbing (VeloxDev.Core.WorkflowSystem namespace) must not leak — RouterCompileMode
        // must never be REGISTERED as a customer enum (its "Type: <FullName>" Enum rendering must not
        // appear). Its short type name may still legitimately show up in a customer node's property
        // table as the type of a documented CompileMode member — that is not a registration leak.
        Assert.IsFalse(context.Contains("Type: VeloxDev.Core.WorkflowSystem.CompilerEx.RouterCompileMode"));
    }

    [TestMethod]
    public void CoreAssembly_Scan_DoesNotRegisterFrameworkTypesAsCustomer()
    {
        // Mirrors the demo's WithAutoDiscovery("VeloxDev.Core"): the framework's own default
        // view models and data types must not be re-registered as customer components/data.
        var scope = NewScope().WithAutoDiscovery(typeof(TreeDefaultViewModel).Assembly);

        var context = scope.ProvideCustomerContext(AgentLanguages.English);
        var data = scope.ProvideCustomerDataContext(AgentLanguages.English);

        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.NodeDefaultViewModel"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.TreeDefaultViewModel"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.SlotDefaultViewModel"));
        Assert.IsFalse(context.Contains("Type: VeloxDev.WorkflowSystem.LinkDefaultViewModel"));
        Assert.IsFalse(data.Contains("Type: VeloxDev.WorkflowSystem.TaskContext"));
        Assert.IsFalse(data.Contains("Type: VeloxDev.WorkflowSystem.Anchor"));
    }

    [TestMethod]
    public void ProgressiveContextPrompt_ListsDiscoveredTypes()
    {
        var scope = NewScope().WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);

        var prompt = scope.ProvideProgressiveContextPrompt(AgentLanguages.English);

        Assert.IsTrue(prompt.Contains("Demo.ViewModels.EnumSelectorNodeViewModel"));
        Assert.IsTrue(prompt.Contains("Demo.ViewModels.NodeViewModel"));
        Assert.IsTrue(prompt.Contains("Demo.ViewModels.NetworkRequestMethod"));
    }

    [TestMethod]
    public void ListCreatableTypes_SurfacesAutoDiscoveredAssemblies()
    {
        var tree = new TreeDefaultViewModel();
        var scope = new WorkflowAgentScope(tree).WithAutoDiscovery(typeof(EnumSelectorNodeViewModel).Assembly);
        var toolkit = new WorkflowAgentToolkit(scope);

        var result = InvokeTool(toolkit, "ListCreatableTypes");
        var json = JObject.Parse(result);
        var nodeTypes = json["nodeTypes"] as JArray;
        Assert.IsNotNull(nodeTypes);
        Assert.IsTrue(nodeTypes!.Any(n =>
            string.Equals(n["name"]?.ToString(), "EnumSelectorNodeViewModel", StringComparison.Ordinal)),
            "ListCreatableTypes must surface types from scope-registered assemblies");
    }

    // ── Probe assembly (test assembly): deep-scan member inference branches ──

    [TestMethod]
    public void ProbeAssembly_RegistersAllDiscoveryCategories()
    {
        var scope = NewScope().WithAutoDiscovery(typeof(ProbeNode).Assembly);

        var context = scope.ProvideCustomerContext(AgentLanguages.English);
        var data = scope.ProvideCustomerDataContext(AgentLanguages.English);
        string T(string name) => $"Type: VeloxDev.Core.Extension.Test.Discovery.{name}";

        // Concrete workflow component registered; abstract component skipped.
        Assert.IsTrue(context.Contains(T("ProbeNode")));
        Assert.IsFalse(context.Contains(T("ProbeAbstractNode")));

        // Enums via plain member type, [SlotSelectors], and generic unwrapping.
        foreach (var e in new[] { "ProbeColor", "ProbeSize", "ProbeShape", "ProbeMaterial", "ProbeSurface" })
            Assert.IsTrue(context.Contains(T(e)), $"enum {e} was not auto-discovered");
        // Enum referenced ONLY via [SlotSelectors] (no plain member type) must still register.
        Assert.IsTrue(context.Contains(T("ProbeFlavor")), "[SlotSelectors]-only enum was not auto-discovered");

        // Interface via member type.
        Assert.IsTrue(context.Contains(T("IProbeHandler")));

        // Data via struct member, [AgentCommandParameter], and [AgentContext] class.
        Assert.IsTrue(data.Contains(T("ProbeOffset")));
        Assert.IsTrue(data.Contains(T("ProbePayload")));
        Assert.IsTrue(data.Contains(T("ProbeContext")));
    }

    [TestMethod]
    public void ProbeAssembly_DoesNotRegisterWorkflowBaseMembersAsCustomerData()
    {
        // The interface-implemented members (Anchor, Size, ObservableCollection<slot>, commands)
        // are all framework-builtin and must not pollute the customer data/context.
        var scope = NewScope().WithAutoDiscovery(typeof(ProbeNode).Assembly);

        Assert.IsFalse(scope.ProvideCustomerDataContext(AgentLanguages.English)
            .Contains("Type: VeloxDev.WorkflowSystem.Anchor"));
        Assert.IsFalse(scope.ProvideCustomerContext(AgentLanguages.English)
            .Contains("Type: VeloxDev.WorkflowSystem.IWorkflowSlotViewModel"));
    }

    [TestMethod]
    public void AutoDiscovery_DeduplicatesAcrossLanguages()
    {
        // Register the assembly under English first; the second call (Chinese) must NOT
        // re-register the same types. ProbeContext carries both an English and a Chinese
        // [AgentContext] description, so if dedup failed it would be rendered twice (English
        // + Chinese) and the Chinese description would surface. (ProvideCustomerDataContext
        // renders all registered language slots, so type-name assertions cannot observe the
        // language slot — the description language can.)
        var scope = NewScope()
            .WithAutoDiscovery(typeof(ProbeNode).Assembly, AgentLanguages.English)
            .WithAutoDiscovery(typeof(ProbeNode).Assembly, AgentLanguages.Chinese);

        var rendered = scope.ProvideCustomerDataContext();
        Assert.IsTrue(rendered.Contains("probe context data type"),
            "the English-registered description must be rendered");
        Assert.IsFalse(rendered.Contains("探针上下文"),
            "cross-language global dedup failed: type re-registered under Chinese and its Chinese description surfaced");
    }

    // ── Assembly-by-name overload ──

    [TestMethod]
    public void AutoDiscovery_ByName_ResolvesLoadedAssembly()
    {
        var scope = NewScope().WithAutoDiscovery("Lib");

        var context = scope.ProvideCustomerContext(AgentLanguages.English);
        Assert.IsTrue(context.Contains("Type: Demo.ViewModels.NodeViewModel"));
    }

    [TestMethod]
    public void AutoDiscovery_ByName_ThrowsForUnknownAssembly()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new WorkflowAgentScope(new TreeDefaultViewModel()).WithAutoDiscovery("NoSuchAssembly.Velox"));
    }

    // ── helper ──

    private static string InvokeTool(WorkflowAgentToolkit toolkit, string toolName)
    {
        var method = typeof(WorkflowAgentToolkit)
            .GetMethod(toolName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, $"Tool method '{toolName}' was not found.");

        var raw = method.Invoke(toolkit, null);
        Assert.IsInstanceOfType<string>(raw);
        return (string)raw!;
    }
}
} // namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions

namespace VeloxDev.Core.Extension.Test.Discovery
{
    // ── Discovery fixture types (live in the test assembly) ──

public enum ProbeColor { Red, Green, Blue }
public enum ProbeSize { Small, Large }
public enum ProbeShape { Round, Square }
public enum ProbeMaterial { Wood, Metal }
public enum ProbeSurface { Matte, Glossy }
public enum ProbeFlavor { Sweet, Sour }

public interface IProbeHandler { string Handle(string input); }

public struct ProbeOffset
{
    public double X { get; set; }
    public double Y { get; set; }
}

[AgentContext(AgentLanguages.English, "probe context data type")]
[AgentContext(AgentLanguages.Chinese, "探针上下文数据类型")]
public class ProbeContext { public string Note { get; set; } = ""; }

public class ProbePayload { public string Body { get; set; } = ""; }

/// <summary>
/// A concrete customer workflow component carrying discovery-relevant members that exercise
/// every deep-scan branch of <see cref="WorkflowAgentScope.WithAutoDiscovery"/>.
/// Members are never instantiated — only reflected — so command implementations are stubbed.
/// </summary>
public class ProbeNode : IWorkflowNodeViewModel
{
    // ── IWorkflowViewModel / INotify* ──
    public event System.ComponentModel.PropertyChangingEventHandler? PropertyChanging;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void InitializeWorkflow() { }
    public void OnPropertyChanging(string propertyName) { }
    public void OnPropertyChanged(string propertyName) { }
    public IVeloxCommand CloseCommand => null!;

    // ── IWorkflowNodeViewModel ──
    public IWorkflowTreeViewModel? Parent { get; set; }
    public Anchor Anchor { get; set; }
    public Size Size { get; set; }
    public System.Collections.ObjectModel.ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; } = [];
    public IVeloxCommand MoveCommand => null!;
    public IVeloxCommand SetAnchorCommand => null!;
    public IVeloxCommand SetSizeCommand => null!;
    public IVeloxCommand CreateSlotCommand => null!;
    public IVeloxCommand DeleteCommand => null!;
    public IVeloxCommand ReceiveCommand => null!;
    public IVeloxCommand BroadcastCommand => null!;
    public IVeloxCommand ReverseBroadcastCommand => null!;
    public IWorkflowNodeViewModelHelper GetHelper() => null!;
    public void SetHelper(IWorkflowNodeViewModelHelper helper) { }

    // ── discovery probe members ──
    public ProbeColor Color { get; set; }                                // plain enum property
    public IProbeHandler? Handler { get; set; }                          // interface property
    public ProbeOffset Offset { get; set; }                              // struct value object
    public List<ProbeSize> Sizes { get; set; } = [];                     // List<T> unwrap
    public Dictionary<string, ProbeShape> Shapes { get; set; } = new();  // multi-param generic
    public Task<ProbeMaterial>? Material { get; set; }                   // Task<T> unwrap
    public ProbeSurface? Surface { get; set; }                           // Nullable<T> unwrap
    public IEnumerable<ProbeOffset> Offsets { get; set; } = [];          // IEnumerable<T> → struct

    [SlotSelectors(typeof(ProbeColor), typeof(ProbeShape))]
    public int SelectorProxy { get; set; }                               // [SlotSelectors] member path

    [SlotSelectors(typeof(ProbeFlavor))]
    public int FlavorSelector { get; set; }                              // [SlotSelectors] enum with no plain member

    [AgentCommandParameter(typeof(ProbePayload))]
    public ProbePayload? Active { get; set; }                            // [AgentCommandParameter] property

    [AgentCommandParameter(typeof(ProbePayload))]
#pragma warning disable CS0169 // probe-only field, never read
    private ProbePayload? _queued;                                       // [AgentCommandParameter] field
#pragma warning restore CS0169

    [AgentCommandParameter(typeof(ProbePayload))]
    public void ApplyPayload(ProbePayload payload) { }                   // [AgentCommandParameter] method + param
}

/// <summary>An abstract workflow component — auto-discovery must skip it (Pass 1).</summary>
public abstract class ProbeAbstractNode : ProbeNode { }
}

