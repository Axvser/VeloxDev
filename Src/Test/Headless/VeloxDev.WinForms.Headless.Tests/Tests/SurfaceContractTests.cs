using System.Reflection;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WorkflowSystem.ViewHosting;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 没有附着属性系统，契约由静态 Get/Set 方法承载；这里按方法名钉住表面与辅助角色。
[TestClass]
public sealed class SurfaceContractTests
{
    private static readonly Assembly Adapter = typeof(WorkflowSurfaceBehavior).Assembly;

    private static Type RequireType(string name)
    {
        var type = Adapter.GetType(name);
        Assert.IsNotNull(type, $"missing workflow role: {name}");
        return type!;
    }

    private static void RequireMethod(Type owner, string method)
        => Assert.IsNotNull(
            owner.GetMethod(method, BindingFlags.Public | BindingFlags.Static),
            $"missing public API: {owner.Name}.{method}");

    [TestMethod]
    public void Surface_ExposesTheWholeLifecycleAsStaticEntryPoints()
    {
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSurfaceBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowCanvasTransformBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool");
        RequireType("VeloxDev.WorkflowSystem.Attachments.WorkflowNodeAttachment");
        RequireType("VeloxDev.WorkflowSystem.Attachments.WorkflowSlotAttachment");
        RequireType("VeloxDev.WorkflowSystem.Attachments.WorkflowLinkAttachment");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowNodeDragBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotConnectionBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior");
    }

    [TestMethod]
    public void Surface_ExposesEveryAttachedEntryPoint()
    {
        var surface = typeof(WorkflowSurfaceBehavior);
        foreach (var method in new[]
                 {
                     "GetIsEnabled", "SetIsEnabled", "GetZoomEnabled", "SetZoomEnabled",
                     "GetScrollViewerName", "SetScrollViewerName", "GetCanvasName", "SetCanvasName",
                     "GetGridDecoratorName", "SetGridDecoratorName",
                     "GetMinimapOverlayName", "SetMinimapOverlayName",
                     "GetPointerPressSourceName", "SetPointerPressSourceName",
                     "GetNodeViewType", "SetNodeViewType", "GetLinkViewType", "SetLinkViewType",
                     "GetTemplateSelector", "SetTemplateSelector",
                     "GetWorkflowTree", "SetWorkflowTree",
                     "GetLinkMenuBuilder", "SetLinkMenuBuilder",
                     "Refresh",
                 })
        {
            RequireMethod(surface, method);
        }
    }

    [TestMethod]
    public void SupportingRoles_ExposeTheirStaticEntryPoints()
    {
        RequireMethod(typeof(WorkflowCanvasTransformBehavior), "GetTransform");
        RequireMethod(typeof(WorkflowCanvasTransformBehavior), "SetTransform");

        RequireMethod(typeof(ViewPool), "SetItemsSource");
        RequireMethod(typeof(ViewPool), "SetTemplateSelector");

        RequireMethod(typeof(WorkflowNodeDragBehavior), "SetIsEnabled");
        RequireMethod(typeof(WorkflowNodeDragBehavior), "SetCoordinateHostName");
        RequireMethod(typeof(WorkflowNodeDragBehavior), "SetCoordinateHostType");

        RequireMethod(typeof(WorkflowSlotConnectionBehavior), "SetIsEnabled");

        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "SetIsEnabled");
        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "SetSlotNames");
        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "SetSlotEnumeratorNames");
    }

    [TestMethod]
    public void ViewContainer_RestoresTheContextTagWhenAViewIsReactivatedForTheSameItem()
    {
        // 池回收只把视图藏起来并清掉 Tag；再次给同一个 item 时附件的 setter 会 ReferenceEquals 早退。
        // Tag 不补回来，WorkflowNodeDragBehavior / WorkflowSlotLayoutBehavior 的 ResolveNode 就再也找不到节点。
        var graph = TestGraph.Create(connect: false);
        using var host = new Panel();
        using var view = new Panel();
        var container = new ViewContainer();

        container.Attach(host, view);
        container.Activate(view, graph.Left);
        Assert.AreSame(graph.Left, view.Tag);

        container.Deactivate(view);
        Assert.IsNull(view.Tag);

        container.Activate(view, graph.Left);
        Assert.AreSame(graph.Left, view.Tag);
    }
}
