using global::Jalium.UI;
using global::Jalium.UI.Controls;
using VeloxDev.AdapterTestKit;
using VeloxDev.Jalium.Headless.Tests.Infrastructure;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Jalium.Headless.Tests.Tests;

// 与 WPF/Avalonia 同一条槽锚点契约：具名端口的视图中心 + 节点锚点 = slot.Anchor。
[TestClass]
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
        view.RegisterName("PART_InputSlot", inputBorder);
        view.RegisterName("PART_OutputSlot", outputBorder);

        WorkflowSlotLayoutBehavior.SetSlotNames(view, "PART_InputSlot,PART_OutputSlot");
        WorkflowSlotLayoutBehavior.SetIsEnabled(view, true);

        JaliumHeadlessSurface.SetLoaded(view);
        view.Measure(new global::Jalium.UI.Size(160d, 90d));
        view.Arrange(new global::Jalium.UI.Rect(0d, 0d, 160d, 90d));
        JaliumHeadlessSurface.Pump();

        Assert.AreEqual(56d, input.Anchor.Horizontal, 1d);
        Assert.AreEqual(56d, input.Anchor.Vertical, 1d);
        Assert.AreEqual(184d, output.Anchor.Horizontal, 1d);
        Assert.AreEqual(56d, output.Anchor.Vertical, 1d);
    }
}
