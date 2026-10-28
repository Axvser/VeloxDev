using Bunit;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// 网格装饰器只消费 SurfaceViewport；bUnit 渲染即可覆盖刻度/标签的计算与标记。
[TestClass]
public sealed class GridDecoratorTests
{
    [TestMethod]
    public void GridDecorator_RendersRulersForAViewport()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();
        var viewport = new SurfaceViewport(graph.Tree, 12d, 34d, 800d, 600d, 5d, 7d);

        var cut = ctx.Render<WorkflowGridDecorator>(p => p
            .Add(x => x.Viewport, viewport)
            .Add(x => x.RulerThickness, 28d)
            .Add(x => x.Spacing, 40d));

        Assert.IsTrue(cut.Markup.Length > 0);
        Assert.IsTrue(cut.Markup.Contains("28px"));
    }
}
