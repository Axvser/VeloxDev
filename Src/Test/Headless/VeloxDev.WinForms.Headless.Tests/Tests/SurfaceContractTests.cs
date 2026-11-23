using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms has no attached-property system on plain controls; the contract is carried by the static Get/Set
// methods. This pins the roles the visual backend exposes: the workflow-free pool, the surface camera/parts, and
// the three per-view behaviors that replace the deleted content monolith.
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
    public void Surface_ExposesTheVisualRolesThatReplacedTheMonolith()
    {
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSurfaceBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowNodeDragBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotConnectionBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay");
    }

    [TestMethod]
    public void ViewPool_ExposesTheItemSourceTemplateAndSelector()
    {
        var pool = typeof(ViewPool);
        foreach (var method in new[]
                 {
                     "GetItemsSource", "SetItemsSource",
                     "GetTemplateSelector", "SetTemplateSelector",
                     "GetItemTemplate", "SetItemTemplate",
                     "Refresh",
                 })
        {
            RequireMethod(pool, method);
        }
    }

    [TestMethod]
    public void Surface_ExposesPartsCameraAndInputEntryPoints()
    {
        var surface = typeof(WorkflowSurfaceBehavior);
        foreach (var method in new[]
                 {
                     "GetIsEnabled", "SetIsEnabled",
                     "GetZoomEnabled", "SetZoomEnabled",
                     "GetScrollViewerName", "SetScrollViewerName",
                     "GetCanvasName", "SetCanvasName",
                     "GetGridDecoratorName", "SetGridDecoratorName",
                     "GetMinimapOverlayName", "SetMinimapOverlayName",
                     "GetVisualTemplateSelector", "SetVisualTemplateSelector",
                     "GetWorkflowTree", "SetWorkflowTree",
                     "GetZoom", "GetPan", "SetPan",
                     "SurfaceToWorld", "WorldToSurface",
                     "GetWorldViewport", "SetWorldViewport",
                     "ZoomAt", "ProcessWheel", "Refresh",
                 })
        {
            RequireMethod(surface, method);
        }
    }

    [TestMethod]
    public void PerViewBehaviors_ExposeTheirSwitchAndCoordinateHost()
    {
        foreach (var type in new[]
                 {
                     typeof(WorkflowSlotLayoutBehavior),
                     typeof(WorkflowNodeDragBehavior),
                     typeof(WorkflowSlotConnectionBehavior),
                 })
        {
            RequireMethod(type, "GetIsEnabled");
            RequireMethod(type, "SetIsEnabled");
            RequireMethod(type, "Ensure");
        }

        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "GetSlotNames");
        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "SetSlotEnumeratorNames");
        RequireMethod(typeof(WorkflowSlotLayoutBehavior), "SetCoordinateHostName");
        RequireMethod(typeof(WorkflowNodeDragBehavior), "SetCoordinateHostName");
    }
}
