using System.Collections.ObjectModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// TEMP pure-data probe (no GUI): does a collapse-Scale write refresh the spatial providers in time
/// for a subsequent Virtualize? The NodeBoundsProvider / NodePairBoundsProvider caches the node's
/// COLLAPSED Anchor/Size and refreshes only on node Anchor/Size PropertyChanged. Generated nodes
/// (and NodeDefaultViewModel) re-raise Anchor/Size when the parent Layout.Scale changes via their
/// WorkflowNodeScaleTracker (attached in the Parent setter). If that synchronous chain fires before
/// the next Virtualize, a window at the NEW collapsed coordinates must still hit the node — if the
/// providers were stale (scale-1 rect), the query misses and the node drops out of VisibleItems.
/// That drop is the suspected "deep-zoom links vanish" mechanism (links only enter the pool through
/// their pair provider, whose union box is built from the same cached node rects).
/// </summary>
[TestClass]
public class DeepZoomSpatialRefreshProbeTests
{
    [TestMethod]
    public void ScaleWrite_ThenVirtualize_KeepsNodeVisible_AtNewCollapsedPosition()
    {
        var tree = new TreeDefaultViewModel();
        var node = new TestEnumNode();
        tree.GetHelper().CreateNode(node);

        // Demo MainWindow.LoadTree world geometry (positive anchors, size 260x180).
        node.Anchor = new Anchor(80, 80, 0);
        node.Size = new Size(260, 180);

        var visible = new ObservableCollection<IWorkflowViewModel>();
        Assert.AreEqual(1, tree.EnableMap(200, visible), "spatial map should enable once");
        tree.Virtualize(new Viewport(0, 0, 2000, 2000));
        Assert.IsTrue(visible.Contains(node), "precondition: node visible at scale 1");

        // Mirror the adapter zoom: write Scale first (collapsed getters now yield world/Scale).
        tree.Layout.Scale = new Scale(0.1, 0.1);
        Assert.AreEqual(800d, node.Anchor.Horizontal, 1e-6, "precondition: collapsed getter ×10");
        Assert.AreEqual(2600d, node.Size.Width, 1e-6, "precondition: collapsed size ×10");

        // Realistic viewport (≈1100 px) centered on the node's collapsed center (world 210,210 → 2100,2100).
        var window = new Viewport(2100 - 550, 2100 - 500, 1100, 1000);
        tree.Virtualize(window);

        // PASS iff providers refreshed synchronously on the Scale write. If they are stale at the
        // scale-1 rect (80,80..340,260) the window misses them and the node drops — that is exactly
        // the bug the GUI reports as links vanishing beyond ~0.2x (links ride on these providers).
        Assert.IsTrue(visible.Contains(node),
            "node must stay visible at the collapsed position after a scale-only change — providers " +
            "must have re-indexed on the Scale PropertyChanged, before the next Virtualize");
    }
}
