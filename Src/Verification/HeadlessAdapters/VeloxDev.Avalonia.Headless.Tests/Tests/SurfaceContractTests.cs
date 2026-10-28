using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

// 与 WPF 同一份契约：七个角色 + 每个公共附着点。名字一旦漂移，这里先红。
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

    private static void RequireField(Type owner, string field)
        => Assert.IsNotNull(
            owner.GetField(field, BindingFlags.Public | BindingFlags.Static),
            $"missing attached property: {owner.Name}.{field}");

    [TestMethod]
    public void SevenRoles_ArePresent()
    {
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSurfaceBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowCanvasTransformBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowNodeDragBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotConnectionBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay");
    }

    [TestMethod]
    public void Surface_ExposesEveryAttachedPoint()
    {
        var surface = typeof(WorkflowSurfaceBehavior);
        foreach (var field in new[]
                 {
                     "IsEnabledProperty", "ZoomEnabledProperty", "ScrollViewerNameProperty", "CanvasNameProperty",
                     "GridDecoratorNameProperty", "PointerPressSourceNameProperty", "MinimapOverlayNameProperty",
                     "LinkMenuKeyProperty",
                 })
        {
            RequireField(surface, field);
        }

        Assert.IsNotNull(surface.GetMethod("GetIsEnabled", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(surface.GetMethod("SetIsEnabled", BindingFlags.Public | BindingFlags.Static));
        Assert.IsNotNull(surface.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Static));
    }

    [TestMethod]
    public void SupportingRoles_ExposeTheirAttachedPoints()
    {
        RequireField(typeof(WorkflowCanvasTransformBehavior), "TransformProperty");

        RequireField(typeof(ViewPool), "ItemsSourceProperty");
        RequireField(typeof(ViewPool), "TemplateSelectorProperty");

        RequireField(typeof(WorkflowNodeDragBehavior), "IsEnabledProperty");
        RequireField(typeof(WorkflowNodeDragBehavior), "CoordinateHostNameProperty");
        RequireField(typeof(WorkflowNodeDragBehavior), "CoordinateHostTypeProperty");

        RequireField(typeof(WorkflowSlotConnectionBehavior), "IsEnabledProperty");

        RequireField(typeof(WorkflowSlotLayoutBehavior), "IsEnabledProperty");
        RequireField(typeof(WorkflowSlotLayoutBehavior), "SlotNamesProperty");
        RequireField(typeof(WorkflowSlotLayoutBehavior), "SlotEnumeratorNamesProperty");
        RequireField(typeof(WorkflowSlotLayoutBehavior), "CoordinateHostNameProperty");
        RequireField(typeof(WorkflowSlotLayoutBehavior), "CoordinateHostTypeProperty");

        RequireField(typeof(WorkflowEvents), "NodeProperty");
        RequireField(typeof(WorkflowEvents), "SlotProperty");
        RequireField(typeof(WorkflowEvents), "TreeProperty");
    }
}
