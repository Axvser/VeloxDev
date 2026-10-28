using Bunit;
using Microsoft.AspNetCore.Components;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// Razor 适配器的 C# 半边可以在 bUnit 里直接驱动：组件的 [JSInvokable] 方法就是浏览器回调的入口，
// 不需要真的 JS 模块。IsEnabled=false 保证 OnAfterRender 不 import 模块。
[TestClass]
public sealed class WorkflowAdapterHeadlessTests
{
    [TestMethod]
    public void Surface_RendersTheCanvasHost()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();

        var cut = ctx.Render<WorkflowSurfaceBehavior>(p => p
            .Add(x => x.Tree, graph.Tree)
            .Add(x => x.IsEnabled, false)
            .Add(x => x.ChildContent, canvas => builder => builder.AddMarkupContent(0, "<div id='node'></div>")));

        Assert.IsNotNull(cut.Find(".veloxdev-wf-surface"));
        Assert.IsNotNull(cut.Find(".veloxdev-wf-canvas"));
    }

    [TestMethod]
    public void SurfaceScroll_WritesTheViewport()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();

        var cut = ctx.Render<WorkflowSurfaceBehavior>(p => p
            .Add(x => x.Tree, graph.Tree)
            .Add(x => x.IsEnabled, false));

        cut.Instance.OnSurfaceScroll(120d, 80d, 800d, 600d, 8000d, 6000d, 0d, 0d);

        Assert.AreEqual(120d, graph.Tree.Helper.Viewport.Horizontal, 1e-9);
        Assert.AreEqual(80d, graph.Tree.Helper.Viewport.Vertical, 1e-9);
        Assert.AreEqual(
            graph.Tree.Helper.Viewport.Horizontal,
            graph.Tree.Layout.ViewportOffset.Horizontal,
            1e-9);
    }

    [TestMethod]
    public async Task WheelZoom_ZoomsIn_AndClamps()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();

        var cut = ctx.Render<WorkflowSurfaceBehavior>(p => p
            .Add(x => x.Tree, graph.Tree)
            .Add(x => x.IsEnabled, false));

        var before = graph.Tree.Layout.Scale.Horizontal;

        await cut.Instance.OnWheelZoom(120, 0d, 0d, 800d, 600d, 8000d, 6000d);

        Assert.AreEqual(before / 1.1, graph.Tree.Layout.Scale.Horizontal, 1e-9);

        for (var i = 0; i < 40; i++)
        {
            await cut.Instance.OnWheelZoom(120, 0d, 0d, 800d, 600d, 8000d, 6000d);
        }

        Assert.AreEqual(0.1d, graph.Tree.Layout.Scale.Horizontal, 1e-9);
    }

    [TestMethod]
    public void NodeDrag_ExecutesMoveCommand()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();

        var cut = ctx.Render<WorkflowNodeDragBehavior>(p => p
            .Add(x => x.Node, graph.Left)
            .Add(x => x.IsEnabled, false));

        cut.Instance.OnNodeDrag(25d, 10d);

        cut.WaitForAssertion(
            () => Assert.AreEqual(65d, graph.Left.Anchor.Horizontal, 1e-6),
            TimeSpan.FromSeconds(2));
        Assert.AreEqual(50d, graph.Left.Anchor.Vertical, 1e-6);
    }

    [TestMethod]
    public void SlotConnection_StartThenEnd_CreatesTheLink()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create(connect: false);

        var cut = ctx.Render<WorkflowSlotConnectionBehavior>(p => p
            .Add(x => x.Slot, graph.LeftOutput)
            .Add(x => x.Tree, graph.Tree)
            .Add(x => x.IsEnabled, false));

        cut.Instance.OnSlotConnectionStart(200d, 85d);

        cut.WaitForAssertion(
            () => Assert.IsTrue(graph.Tree.VirtualLink.IsVisible, "the sender must be armed before the release"),
            TimeSpan.FromSeconds(2));

        cut.Instance.OnSlotConnectionEnd(WorkflowRuntimeIds.Get(graph.RightInput));

        cut.WaitForAssertion(
            () => Assert.AreEqual(1, graph.Tree.Links.Count),
            TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void ViewPool_RendersOneItemPerVisibleItem()
    {
        using var ctx = new BunitContext();
        var graph = TestGraph.Create();

        var cut = ctx.Render<ViewPool>(p => p
            .Add(x => x.ItemsSource, graph.Tree.Helper.VisibleItems)
            .Add(x => x.ItemTemplate, item => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "pool-item");
                builder.CloseElement();
            }));

        Assert.AreEqual(
            graph.Tree.Helper.VisibleItems.Count,
            cut.FindAll(".veloxdev-wf-pool-item").Count);
    }
}
