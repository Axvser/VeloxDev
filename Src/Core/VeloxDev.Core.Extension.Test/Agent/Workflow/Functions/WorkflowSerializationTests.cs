using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using VeloxDev.AI;
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

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

    /// <summary>
    /// Regression: saving a tree while zoomed (Layout.Scale != 1) then loading it must leave nodes spatially
    /// visible. Node Anchor/Size getters collapse toward the world origin by the scale (÷scale), so the
    /// serializer used to write collapsed coordinates and the load stored them verbatim — the getter then
    /// collapsed a second time and every node landed at 1/4 its world position, outside the saved viewport,
    /// so the spatial index returned an empty VisibleItems.
    ///
    /// Anchor/Size now serialize their raw/world values ([OnSerializing] expands the transient collapsed
    /// instance) and, on load, push the restored raw value back into the field they were collapsed from
    /// ([OnDeserialized] writes the transient's JSON values into the node), so a load collapses exactly
    /// once and nodes stay inside the world-space viewport.
    /// </summary>
    [TestMethod]
    public void TreeZoomedBeforeSave_NodesVisibleAfterRoundTrip()
    {
        const double scale = 2d;
        const double worldLeft = 1400d;
        const double worldTop = 900d;
        const double worldWidth = 300d;
        const double worldHeight = 200d;

        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(scale, scale);
        var node = new TestEnumNode();
        tree.GetHelper().CreateNode(node);
        node.Anchor = new Anchor(worldLeft, worldTop, 0);
        node.Size = new Size(worldWidth, worldHeight);

        // Collapsing getter: view value (what the renderer and the spatial index consume) is ÷scale.
        Assert.AreEqual(worldLeft / scale, node.Anchor.Horizontal, 1e-9, "precondition: getter collapses by scale");
        Assert.AreEqual(worldWidth / scale, node.Size.Width, 1e-9, "precondition: getter collapses by scale");

        var json = tree.Serialize();

        // The file must store world (raw) coordinates, not the collapsed render values: a file saved
        // while zoomed must load identically at any zoom. The collapsing getter would otherwise leak
        // ÷scale values into the JSON.
        var jo = JObject.Parse(json);
        var nodeJson = jo["Nodes"]![0]!["Anchor"]!;
        Assert.AreEqual(worldLeft, (double)nodeJson["Horizontal"]!, 1e-9, "JSON must store the raw/world Anchor.Horizontal");
        Assert.AreEqual(worldTop, (double)nodeJson["Vertical"]!, 1e-9, "JSON must store the raw/world Anchor.Vertical");

        var restored = json.Deserialize<TreeDefaultViewModel>();

        Assert.AreEqual(new Scale(scale, scale), restored.Layout.Scale, "zoom level must survive the round-trip");

        var restoredNode = restored.Nodes.OfType<TestEnumNode>().SingleOrDefault();
        Assert.IsNotNull(restoredNode, "deserialized tree should contain the node");

        // The node must not be double-collapsed: the getter collapses exactly once (raw ÷ scale).
        Assert.AreEqual(worldLeft / scale, restoredNode!.Anchor.Horizontal, 1e-9,
            "Anchor must collapse exactly once after load — the JSON must have stored the raw/world value");
        Assert.AreEqual(worldTop / scale, restoredNode.Anchor.Vertical, 1e-9);
        Assert.AreEqual(worldWidth / scale, restoredNode.Size.Width, 1e-9);
        Assert.AreEqual(worldHeight / scale, restoredNode.Size.Height, 1e-9);

        // The saved viewport lives in the collapsed/render space. World-to-viewport in this model is the
        // identity (ActualOffset = 0), so a viewport centered on the collapsed position must hit the node.
        var visible = new ObservableCollection<IWorkflowViewModel>();
        Assert.AreEqual(1, restored.EnableMap(200, visible), "spatial map should enable once");
        var viewport = new Viewport(
            restoredNode.Anchor.Horizontal - 100d,
            restoredNode.Anchor.Vertical - 100d,
            400d,
            400d);
        restored.Virtualize(viewport);
        Assert.IsTrue(visible.Contains(restoredNode),
            "the node must be spatially visible after round-trip — before the fix the double-collapsed " +
            "bounds fell outside the world-space viewport and VisibleItems stayed empty");
    }

    /// <summary>
    /// Same regression as <see cref="TreeZoomedBeforeSave_NodesVisibleAfterRoundTrip"/> but for the
    /// hand-written <see cref="NodeDefaultViewModel"/> template (no SlotEnumerator). Its Anchor/Size
    /// getters collapse by scale exactly like the generated nodes, so it exercises the same
    /// ObjectCreationHandling.Auto setter-skip path on load — the shared Anchor/Size serialization
    /// hooks must restore the raw values for it too.
    /// </summary>
    [TestMethod]
    public void TreeZoomedBeforeSave_NodeDefaultViewModel_KeepsAnchorAndSize()
    {
        const double scale = 2d;
        const double worldLeft = 1400d;
        const double worldTop = 900d;
        const double worldWidth = 300d;
        const double worldHeight = 200d;

        var tree = new TreeDefaultViewModel();
        tree.Layout.Scale = new Scale(scale, scale);
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.Anchor = new Anchor(worldLeft, worldTop, 0);
        node.Size = new Size(worldWidth, worldHeight);

        Assert.AreEqual(worldLeft / scale, node.Anchor.Horizontal, 1e-9, "precondition: getter collapses by scale");

        var json = tree.Serialize();
        var restored = json.Deserialize<TreeDefaultViewModel>();

        Assert.AreEqual(new Scale(scale, scale), restored.Layout.Scale, "zoom level must survive the round-trip");
        var restoredNode = restored.Nodes.OfType<NodeDefaultViewModel>().SingleOrDefault();
        Assert.IsNotNull(restoredNode, "deserialized tree should contain the node");
        Assert.AreEqual(worldLeft / scale, restoredNode!.Anchor.Horizontal, 1e-9,
            "Anchor must collapse exactly once after load — the JSON must have stored the raw/world value");
        Assert.AreEqual(worldTop / scale, restoredNode.Anchor.Vertical, 1e-9);
        Assert.AreEqual(worldWidth / scale, restoredNode.Size.Width, 1e-9);
        Assert.AreEqual(worldHeight / scale, restoredNode.Size.Height, 1e-9);
    }
}
