using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>Test-only enum used as a SlotEnumerator selector.</summary>
public enum TestRouteKind { Alpha, Beta, Gamma }

/// <summary>
/// Mirrors the Demo EnumSelector node: a node owning a SlotEnumerator whose SelectorType is set
/// in the constructor (so after load it must be re-resolved from the serialized SelectorTypeName).
/// </summary>
[WorkflowBuilder.Node<NodeHelper<TestEnumNode>>(workSemaphore: 1)]
public partial class TestEnumNode
{
    public TestEnumNode()
    {
        InitializeWorkflow();
        OutputSlots.SetSelector(typeof(TestRouteKind));
    }

    [VeloxProperty]
    [SlotSelectors(typeof(TestRouteKind))]
    public partial SlotEnumerator<SlotDefaultViewModel> OutputSlots { get; set; }
}

[TestClass]
public class WorkflowSerializationTests
{
    /// <summary>
    /// Serialize an entire tree containing a SlotEnumerator node, then deserialize it back and
    /// verify the selector type survived. This reproduces the Demo's save/load path where the
    /// dropdown must show the serialized enum type, not the constructor default.
    /// </summary>
    [TestMethod]
    public void TreeWithEnumNode_RoundTripsSelectorType()
    {
        var tree = new TreeDefaultViewModel();
        var node = new TestEnumNode();
        tree.GetHelper().CreateNode(node);
        node.OutputSlots.CurrentValue = TestRouteKind.Beta;

        Assert.AreEqual(typeof(TestRouteKind), node.OutputSlots.SelectorType, "precondition: selector set in constructor");

        var json = tree.Serialize();
        var restored = json.Deserialize<TreeDefaultViewModel>();

        var restoredNode = restored.Nodes.OfType<TestEnumNode>().SingleOrDefault();
        Assert.IsNotNull(restoredNode, "deserialized tree should contain the enum node");
        Assert.AreEqual(typeof(TestRouteKind).FullName, restoredNode!.OutputSlots.SelectorTypeName,
            "SelectorTypeName must survive the full-tree round-trip");
        Assert.AreEqual(typeof(TestRouteKind), restoredNode.OutputSlots.SelectorType,
            "SelectorType must survive the full-tree round-trip (re-resolved from SelectorTypeName)");
        Assert.AreEqual("Beta", restoredNode.OutputSlots.CurrentValue,
            "CurrentValue must survive as its UI string form");
        Assert.AreEqual(TestRouteKind.Beta, restoredNode.OutputSlots.NormalizeSelectorValue(restoredNode.OutputSlots.CurrentValue),
            "CurrentValue must normalize back to the enum member for routing");
    }

    /// <summary>
    /// CurrentValue is UI-friendly: getter returns the string form; setter accepts a string, an enum,
    /// or an underlying numeric value and stores the normalized selector-typed value.
    /// </summary>
    [TestMethod]
    public void CurrentValue_NormalizesSetterAndReturnsString()
    {
        var node = new TestEnumNode();

        node.OutputSlots.CurrentValue = TestRouteKind.Beta;          // enum in
        Assert.AreEqual("Beta", node.OutputSlots.CurrentValue, "getter must return the string form");

        node.OutputSlots.CurrentValue = "Gamma";                      // string in
        Assert.AreEqual("Gamma", node.OutputSlots.CurrentValue);
        Assert.AreEqual(TestRouteKind.Gamma, node.OutputSlots.NormalizeSelectorValue(node.OutputSlots.CurrentValue));

        node.OutputSlots.CurrentValue = 0;                            // underlying int in
        Assert.AreEqual(TestRouteKind.Alpha, node.OutputSlots.NormalizeSelectorValue(node.OutputSlots.CurrentValue));
    }
}
