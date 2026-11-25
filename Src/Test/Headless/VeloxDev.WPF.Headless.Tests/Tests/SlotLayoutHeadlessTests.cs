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

    [TestMethod]
    public void EnumeratorContainers_RebuiltForTheSameSlots_ReMeasureTheSentinels()
    {
        var graph = TestGraph.Create(connect: false);
        var node = graph.Left;
        var first = new SlotDefaultViewModel { Parent = node };
        var second = new SlotDefaultViewModel { Parent = node };
        node.Slots.Add(first);
        node.Slots.Add(second);

        var slots = new System.Collections.ObjectModel.ObservableCollection<SlotDefaultViewModel> { first, second };
#pragma warning disable CS0618 // WPF's runtime template factory is the only way to build this without XAML.
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(FrameworkElement.WidthProperty, 8d);
        factory.SetValue(FrameworkElement.HeightProperty, 8d);
        var template = new DataTemplate { VisualTree = factory };
#pragma warning restore CS0618
        var items = new ItemsControl { Name = "PART_OutputSlots", ItemsSource = slots, ItemTemplate = template };

        var view = new UserControl { Width = 160d, Height = 90d, DataContext = node, Content = items };
        var scope = new NameScope();
        NameScope.SetNameScope(view, scope);
        scope.RegisterName("PART_OutputSlots", items);

        WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(view, "PART_OutputSlots");
        WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

        view.Measure(new System.Windows.Size(160d, 90d));
        view.Arrange(new Rect(0d, 0d, 160d, 90d));
        view.UpdateLayout();
        WpfHeadlessSurface.Pump();
        Assert.IsFalse(double.IsNaN(first.Anchor.Horizontal), "the initial containers must be measured");

        // Same model slots, a rebuilt container set: the model hash and the count are unchanged, so only
        // the realized-control identity can invalidate the stamp.
        first.Anchor = new Anchor(-1000d, -1000d, 0);
        second.Anchor = new Anchor(-2000d, -2000d, 0);
        // A different source collection instance forces the generator to recreate its containers.
        items.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<SlotDefaultViewModel> { first, second };
        view.Measure(new System.Windows.Size(160d, 90d));
        view.Arrange(new Rect(0d, 0d, 160d, 90d));
        view.UpdateLayout();

        // The bare-STA WPF harness does not raise LayoutUpdated for a hand-measured view; invoke the
        // behavior's layout entry directly so the stamp path under test does run.
        var onLayoutUpdated = typeof(WorkflowSlotLayoutBehavior).GetMethod(
            "OnLayoutUpdated",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.IsNotNull(onLayoutUpdated, "WorkflowSlotLayoutBehavior.OnLayoutUpdated exists");
        onLayoutUpdated!.Invoke(null, new object?[] { view, EventArgs.Empty });
        WpfHeadlessSurface.Pump();

        Assert.AreNotEqual(-1000d, first.Anchor.Horizontal, "a rebuilt container set must re-measure the same model slots");
        Assert.AreNotEqual(-2000d, second.Anchor.Horizontal, "the second rebuilt container must be re-measured too");
    }
}
