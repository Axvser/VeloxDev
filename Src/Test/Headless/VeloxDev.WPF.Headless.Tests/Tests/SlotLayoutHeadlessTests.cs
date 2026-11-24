using System.Windows;
using System.Windows.Controls;
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
}
