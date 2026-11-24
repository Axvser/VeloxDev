using Bunit;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// 这三块迁移后的可验证面：小地图的映射/放置来自 MinimapPipelineCore，槽位写回来自
// SlotLayoutPipelineCore。JS 模块在 IsEnabled=false / 无 ScrollViewerId 时不加载，测试直接打
// [JSInvokable] 入口，与浏览器回调的时机一致。
[TestClass]
public sealed class PipelineMigrationTests
{
    [TestMethod]
    public void Minimap_RendersMappedNodesFromThePipeline()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();
        var viewport = new SurfaceViewport(graph.Tree, 0d, 0d, 800d, 600d, 0d, 0d);
        MinimapDrawContext? drawn = null;

        var cut = ctx.Render<WorkflowMinimapOverlay>(p => p
            .Add(x => x.Viewport, viewport)
            .Add(x => x.Width, 180d)
            .Add(x => x.Height, 120d)
            .Add(x => x.DrawMinimap, context => builder =>
            {
                drawn = context;
                builder.AddMarkupContent(0, "<g class='custom-minimap'></g>");
            }));

        // 3 个节点 + 1 个由 JS 独占坐标的视口块。
        Assert.AreEqual(graph.Tree.Nodes.Count + 1, cut.FindAll("svg rect").Count);
        Assert.IsNotNull(drawn, "the marker renderer must hand a context to DrawMinimap");
        Assert.AreEqual(graph.Tree.Nodes.Count, drawn!.MappedNodes.Count);
        Assert.IsTrue(cut.Find(".veloxdev-wf-minimap-inner").GetAttribute("style")!.Contains("width:180px"));
    }

    [TestMethod]
    public void MinimapAreaMeasurement_DrivesThePlacementFromCoreMetrics()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();
        var viewport = new SurfaceViewport(graph.Tree, 0d, 0d, 800d, 600d, 0d, 0d);

        var cut = ctx.Render<WorkflowMinimapOverlay>(p => p
            .Add(x => x.Viewport, viewport)
            .Add(x => x.Width, 180d)
            .Add(x => x.Height, 120d));

        cut.Instance.OnMinimapAreaMeasured(400d, 28d, 500d, 640d);
        cut.Instance.OnMinimapScrollRange(7200d, 5400d);

        // 可用区右上角 (400,28)、margin 12、180×120 → left=208, top=40；right 相对 640 宽的浮层 = 252。
        cut.WaitForAssertion(
            () =>
            {
                var style = cut.Find(".veloxdev-wf-minimap-inner").GetAttribute("style");
                StringAssert.Contains(style, "top:40px");
                StringAssert.Contains(style, "right:252px");
            },
            TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void SlotLayoutBatch_WritesAnchorsAndKeepsTheCoreTolerance()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create(connect: false);
        graph.LeftOutput.Anchor = new Anchor(0d, 0d, 2);

        var cut = ctx.Render<WorkflowSlotLayoutBehavior>(p => p
            .Add(x => x.Node, graph.Left)
            .Add(x => x.IsEnabled, false));

        var id = WorkflowRuntimeIds.Get(graph.LeftOutput);

        cut.Instance.OnSlotLayoutBatch(new[] { new[] { id, "210.50", "95.50" } });

        Assert.AreEqual(210.5d, graph.LeftOutput.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(95.5d, graph.LeftOutput.Anchor.Vertical, 1e-9);
        Assert.AreEqual(2, graph.LeftOutput.Anchor.Layer, "the pipeline must preserve the slot layer");

        // 亚像素抖动（< 0.01）不算变化 —— 共享写回规则的阈值在这里生效。
        cut.Instance.OnSlotLayoutBatch(new[] { new[] { id, "210.505", "95.505" } });

        Assert.AreEqual(210.5d, graph.LeftOutput.Anchor.Horizontal, 1e-9);
        Assert.AreEqual(95.5d, graph.LeftOutput.Anchor.Vertical, 1e-9);
    }
}
