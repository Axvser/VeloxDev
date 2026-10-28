using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Tests;

// WinForms 没有附着属性系统，契约由静态 Get/Set 方法承载；这里按方法名钉住七个角色。
[TestClass]
public sealed class SurfaceContractTests
{
    private static readonly Assembly Adapter = typeof(WorkflowTreeView).Assembly;

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
    public void SevenRoles_ArePresent()
    {
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowTreeView");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowCanvasTransformBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowNodeDragBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotConnectionBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay");
    }

    [TestMethod]
    public void Surface_ExposesEveryStaticEntryPoint()
    {
        var surface = typeof(WorkflowSurfaceBehavior);
        foreach (var method in new[]
                 {
                     "GetIsEnabled", "SetIsEnabled", "GetZoomEnabled", "SetZoomEnabled",
                     "GetScrollViewer", "SetScrollViewer", "GetCanvas", "SetCanvas",
                     "GetGridDecorator", "SetGridDecorator", "GetMinimapOverlay", "SetMinimapOverlay",
                     "GetWorkflowTree", "SetWorkflowTree", "Refresh",
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
}
