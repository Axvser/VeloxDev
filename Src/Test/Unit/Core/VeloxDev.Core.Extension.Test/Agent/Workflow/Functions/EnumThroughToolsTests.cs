using Demo.ViewModels;
using Newtonsoft.Json.Linq;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// An enum argument survives the trip from the model to the property or command it names.
/// </summary>
/// <remarks>
/// <para>
/// It did not, and nothing said so. The tool surface <i>writes</i> an enum as its member name — that is what a
/// model sees when it lists a node, and what the <c>CompileMode</c> description above tells it to send back. The
/// read side went straight to the archive serializer, whose run-time scalar table deliberately has no arm for an
/// enum (a generated reader turns one back with a cast; the engine has no metadata of its own to do that with).
/// So the value landed on <c>Expect('{')</c> and threw, the tool caught it, and the answer was a per-property
/// error where the documentation promised a write.
/// </para>
/// <para>
/// The conversion now happens in <see cref="AgentJsonValue"/>, where the target <see cref="System.Type"/> is in
/// hand: a member name or the underlying integer, either way. These tests pin both spellings, on both the
/// property route and the command route.
/// </para>
/// </remarks>
[TestClass]
public class EnumThroughToolsTests
{
    private static string Patch(WorkflowAgentScope scope, int nodeIndex, string json)
        => WorkflowToolInvoker.Invoke(scope, "PatchNodeProperties", ("nodeIndex", nodeIndex), ("jsonPatch", json));

    /// <summary>
    /// The exact call the node's own <c>AgentContext</c> advertises: <c>{"CompileMode":"Static"}</c>.
    /// </summary>
    [TestMethod]
    public void PatchNodeProperties_SetsAnEnumFromItsMemberName()
    {
        var tree = new TreeDefaultViewModel();
        var node = new EnumSelectorNodeViewModel();
        tree.GetHelper().CreateNode(node);
        Assert.AreEqual(RouterCompileMode.Dynamic, node.CompileMode, "precondition: the constructor default");

        var patch = JObject.Parse(Patch(new WorkflowAgentScope(tree), 0, """{"CompileMode":"Static"}"""));

        Assert.AreEqual("ok", patch["status"]?.Value<string>(), patch.ToString());
        var detail = patch["details"]!.First(d => d["property"]!.Value<string>() == "CompileMode")!;
        Assert.AreEqual("ok", detail["status"]?.Value<string>(), detail.ToString());
        Assert.AreEqual(RouterCompileMode.Static, node.CompileMode,
            "the property must actually hold the member the model named");
    }

    /// <summary>A name is matched the way the rest of the surface matches one — case-insensitively.</summary>
    [TestMethod]
    public void PatchNodeProperties_MatchesAnEnumNameCaseInsensitively()
    {
        var tree = new TreeDefaultViewModel();
        var node = new EnumSelectorNodeViewModel();
        tree.GetHelper().CreateNode(node);

        Patch(new WorkflowAgentScope(tree), 0, """{"CompileMode":"static"}""");

        Assert.AreEqual(RouterCompileMode.Static, node.CompileMode);
    }

    /// <summary>The underlying integer works too — it is the spelling the archive itself writes.</summary>
    [TestMethod]
    public void PatchNodeProperties_TakesAnEnumByItsUnderlyingNumber()
    {
        var tree = new TreeDefaultViewModel();
        var node = new EnumSelectorNodeViewModel();
        tree.GetHelper().CreateNode(node);

        Patch(new WorkflowAgentScope(tree), 0, $"{{\"CompileMode\":{(int)RouterCompileMode.Static}}}");

        Assert.AreEqual(RouterCompileMode.Static, node.CompileMode);
    }

    /// <summary>
    /// A name that is not a member is refused by name, and the property keeps what it had.
    /// </summary>
    /// <remarks>
    /// The failure has to name the value: this is the one message a model can act on by itself, and the
    /// alternative — a silently unchanged node — leaves it convinced the write landed.
    /// </remarks>
    [TestMethod]
    public void PatchNodeProperties_RefusesAnUnknownEnumMemberAndLeavesThePropertyAlone()
    {
        var tree = new TreeDefaultViewModel();
        var node = new EnumSelectorNodeViewModel();
        tree.GetHelper().CreateNode(node);

        var patch = JObject.Parse(Patch(new WorkflowAgentScope(tree), 0, """{"CompileMode":"Sideways"}"""));

        var detail = patch["details"]!.First(d => d["property"]!.Value<string>() == "CompileMode")!;
        Assert.AreEqual("error", detail["status"]?.Value<string>(), patch.ToString());
        Assert.AreEqual("Dynamic", node.CompileMode.ToString(), "a refused write must not half-apply");
    }

    /// <summary>
    /// The command route: a slot's channel is set from a member name, through the tool and its host gate.
    /// </summary>
    /// <remarks>
    /// <c>SetChannelCommand</c> is the enum-parameter command the Agent actually reaches
    /// (<c>[AgentCommandParameter(typeof(SlotChannel))]</c> on the slot interface), so it is the one that proves
    /// the command route rather than only the property route.
    /// </remarks>
    [TestMethod]
    public void ExecuteCommandById_SetsASlotChannelFromItsMemberName()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.None });

        var slot = node.Slots[0];
        var slotId = ((IWorkflowIdentifiable)slot).RuntimeId;

        var scope = new WorkflowAgentScope(tree).WithAllowedGenericCommands("SetChannel");
        var result = JObject.Parse(WorkflowToolInvoker.Invoke(scope, "ExecuteCommandById",
            ("runtimeId", slotId), ("commandName", "SetChannel"), ("jsonParameter", "\"OneSource\"")));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.AreEqual(SlotChannel.OneSource, slot.Channel, "the enum argument reached the command");
    }

    /// <summary>The same command takes the underlying integer, uncapitalised.</summary>
    [TestMethod]
    public void ExecuteCommandById_TakesAChannelByItsUnderlyingNumber()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.CreateSlotCommand.Execute(new SlotDefaultViewModel { Channel = SlotChannel.None });

        var slot = node.Slots[0];

        var result = JObject.Parse(CommandInvoker.Invoke(slot, "SetChannel",
            ((int)SlotChannel.MultipleTargets).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.AreEqual("ok", result["status"]?.Value<string>(), result.ToString());
        Assert.AreEqual(SlotChannel.MultipleTargets, slot.Channel);
    }

    /// <summary>An enum argument that is neither a member nor a number is refused, naming the type.</summary>
    [TestMethod]
    public void AnEnumArgumentThatIsNeitherNameNorNumber_IsRefusedByType()
    {
        var slot = new SlotDefaultViewModel { Channel = SlotChannel.None };

        var result = JObject.Parse(CommandInvoker.Invoke(slot, "SetChannel", "\"Sideways\""));

        Assert.AreEqual("error", result["status"]?.Value<string>(), result.ToString());
        StringAssert.Contains(result["message"]!.Value<string>()!, nameof(SlotChannel));
        Assert.AreEqual(SlotChannel.None, slot.Channel);
    }

    /// <summary>
    /// A JSON literal clears an enum through its backing field; the value must be the zero member, not a throw.
    /// </summary>
    /// <remarks>
    /// <c>null</c> short-circuits before any conversion, so this is really pinning that the new enum arm did not
    /// swallow the null case on its way past.
    /// </remarks>
    [TestMethod]
    public void PatchNodeProperties_WithAJsonNull_ClearsRatherThanThrows()
    {
        var tree = new TreeDefaultViewModel();
        var node = new EnumSelectorNodeViewModel();
        tree.GetHelper().CreateNode(node);

        var patch = JObject.Parse(Patch(new WorkflowAgentScope(tree), 0, """{"CompileMode":null}"""));

        Assert.IsNotNull(patch["details"], patch.ToString());
        Assert.AreEqual("Dynamic", node.CompileMode.ToString(),
            "a null for a non-nullable enum is a no-op, not a crash and not a silent zero");
    }
}
