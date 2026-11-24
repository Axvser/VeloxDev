using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WPF.Headless.Tests.Tests;

// 槽锚点写回：节点视图里两个具名端口量出中心，行为把世界坐标写进 slot.Anchor。
// 没有画布祖先时走 SlotAnchorFromNode（节点锚点 + 视图内中心），这正是模板里的几何。
[STATestClass]
public sealed class SlotLayoutHeadlessTests
{
    [TestMethod]
    public void NamedSlots_GetAnchorsFromTheirMeasuredCentres()
    {
        var graph = TestGraph.Create(connect: false);
        var node = graph.Left;
        var input = new SlotDefaultViewModel();
        var output = new SlotDefaultViewModel();
        node.GetHelper().CreateSlot(input);
        node.GetHelper().CreateSlot(output);

        var inputBorder = new Border
        {
            Width = 16d,
            Height = 16d,
            DataContext = input,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8d, 8d, 0d, 0d),
        };
        var outputBorder = new Border
        {
            Width = 16d,
            Height = 16d,
            DataContext = output,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0d, 8d, 8d, 0d),
        };

        var grid = new Grid();
        grid.Children.Add(inputBorder);
        grid.Children.Add(outputBorder);

        var view = new UserControl
        {
            Width = 160d,
            Height = 90d,
            DataContext = node,
            Content = grid,
        };

        var scope = new NameScope();
        NameScope.SetNameScope(view, scope);
        scope.RegisterName("PART_InputSlot", inputBorder);
        scope.RegisterName("PART_OutputSlot", outputBorder);

        WorkflowSlotLayoutBehavior.SetSlotNames(view, "PART_InputSlot,PART_OutputSlot");
        WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

        view.Measure(new System.Windows.Size(160d, 90d));
        view.Arrange(new Rect(0d, 0d, 160d, 90d));
        view.UpdateLayout();
        WpfHeadlessSurface.Pump();

        // node.Anchor = (40,40). Input: margin 8 + half 8 = 16 → 56 both axes.
        Assert.AreEqual(56d, input.Anchor.Horizontal, 1d);
        Assert.AreEqual(56d, input.Anchor.Vertical, 1d);

        // Output: right edge 160 − margin 8 − half 8 = 144 → 184.
        Assert.AreEqual(184d, output.Anchor.Horizontal, 1d);
        Assert.AreEqual(56d, output.Anchor.Vertical, 1d);
    }

    [TestMethod]
    public void AnchorsFollowLayoutAndZoomRefreshes()
    {
        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel
        {
            Anchor = new Anchor(40d, 40d, 0),
            Size = new VeloxDev.WorkflowSystem.Size(160d, 90d),
        };
        tree.GetHelper().CreateNode(node);

        var slot = new SlotDefaultViewModel();
        node.GetHelper().CreateSlot(slot);

        var slotBorder = new Border
        {
            Width = 16d,
            Height = 16d,
            DataContext = slot,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8d),
        };

        var grid = new Grid();
        grid.Children.Add(slotBorder);

        var view = new UserControl
        {
            Width = 160d,
            Height = 90d,
            DataContext = node,
            Content = grid,
        };

        var scope = new NameScope();
        NameScope.SetNameScope(view, scope);
        scope.RegisterName("PART_Slot", slotBorder);

        var canvas = new Canvas { Width = 400d, Height = 300d, Name = "PART_Canvas" };
        Canvas.SetLeft(view, 40d);
        Canvas.SetTop(view, 40d);
        canvas.Children.Add(view);

        WorkflowSlotLayoutBehavior.SetSlotNames(view, "PART_Slot");
        WorkflowSlotLayoutBehavior.SetCoordinateHostName(view, "PART_Canvas");
        WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

        canvas.Measure(new System.Windows.Size(400d, 300d));
        canvas.Arrange(new Rect(0d, 0d, 400d, 300d));
        canvas.UpdateLayout();
        WpfHeadlessSurface.Pump();

        // view at canvas (40,40); margin 8 + half 8 = centre (56,56); ActualOffset is still zero.
        Assert.AreEqual(56d, slot.Anchor.Horizontal, 1d);
        Assert.AreEqual(56d, slot.Anchor.Vertical, 1d);

        // Zoom: scaling the node view 2x around its own origin moves the measured centre to 40 + 32.
        // A transform-only change leaves the stamp untouched, so this has to go through Refresh (Viewport).
        view.RenderTransformOrigin = new Point(0d, 0d);
        view.RenderTransform = new ScaleTransform(2d, 2d);
        view.UpdateLayout();
        WorkflowSlotLayoutBehavior.Refresh(view);
        WpfHeadlessSurface.Pump();

        Assert.AreEqual(72d, slot.Anchor.Horizontal, 1d);
        Assert.AreEqual(72d, slot.Anchor.Vertical, 1d);

        // Scroll/origin shift: the measured scroll-space centre still includes ActualOffset, so the anchor subtracts it.
        view.RenderTransform = Transform.Identity;
        tree.Layout.NegativeOffset = new Offset(10d, 20d);
        view.UpdateLayout();
        WorkflowSlotLayoutBehavior.Refresh(view);
        WpfHeadlessSurface.Pump();

        Assert.AreEqual(46d, slot.Anchor.Horizontal, 1d);
        Assert.AreEqual(36d, slot.Anchor.Vertical, 1d);
    }
}
