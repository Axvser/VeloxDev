using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// Presentation 是纯函数：颜色字符串、输入/输出槽分类、槽名表。
[TestClass]
public sealed class WorkflowPresentationTests
{
    [TestMethod]
    public void ToCss_ConvertsXamlArgb()
    {
        Assert.AreEqual("rgba(37,37,38,0.784)", WorkflowPresentation.ToCss("#C8252526"));
        Assert.AreEqual("#2A2D2E", WorkflowPresentation.ToCss("#2A2D2E"));
        Assert.AreEqual("red", WorkflowPresentation.ToCss("red"));
        Assert.AreEqual("rgba(1,2,3,0.5)", WorkflowPresentation.ToCss("rgba(1,2,3,0.5)"));
    }

    [TestMethod]
    public void InputAndOutputSlots_SplitByChannel()
    {
        var graph = TestGraph.Create(connect: false);
        var node = graph.Left;
        var input = new SlotDefaultViewModel { Channel = SlotChannel.OneSource };
        var output = new SlotDefaultViewModel { Channel = SlotChannel.OneTarget };
        node.GetHelper().CreateSlot(input);
        node.GetHelper().CreateSlot(output);

        CollectionAssert.Contains(WorkflowPresentation.InputSlotsOf(node).ToList(), input);
        CollectionAssert.Contains(WorkflowPresentation.OutputSlotsOf(node).ToList(), output);
        Assert.IsFalse(WorkflowPresentation.InputSlotsOf(node).Contains(output));
        Assert.IsFalse(WorkflowPresentation.OutputSlotsOf(node).Contains(input));
    }

    [TestMethod]
    public void SlotNamesOf_ReturnsAStableMap()
    {
        var graph = TestGraph.Create(connect: false);
        var map = WorkflowPresentation.SlotNamesOf(graph.Left);
        Assert.IsNotNull(map);
    }
}
