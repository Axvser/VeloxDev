using System.Reflection;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Razor.Headless.Tests.Tests;

// Razor 的契约是组件的 [Parameter]；按属性名钉住七个角色。
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

    private static void RequireProperty(Type owner, string property)
        => Assert.IsNotNull(
            owner.GetProperty(property, BindingFlags.Public | BindingFlags.Instance),
            $"missing component parameter: {owner.Name}.{property}");

    [TestMethod]
    public void SevenRoles_ArePresent()
    {
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSurfaceBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowNodeDragBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotConnectionBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowGridDecorator");
        RequireType("VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay");
    }

    [TestMethod]
    public void Components_ExposeTheirParameters()
    {
        RequireProperty(typeof(WorkflowSurfaceBehavior), "Tree");
        RequireProperty(typeof(WorkflowSurfaceBehavior), "IsEnabled");
        RequireProperty(typeof(WorkflowSurfaceBehavior), "ZoomEnabled");
        RequireProperty(typeof(WorkflowSurfaceBehavior), "GridDecorator");
        RequireProperty(typeof(WorkflowSurfaceBehavior), "Minimap");
        RequireProperty(typeof(WorkflowSurfaceBehavior), "LinkMenu");

        RequireProperty(typeof(WorkflowNodeDragBehavior), "Node");
        RequireProperty(typeof(WorkflowNodeDragBehavior), "IsEnabled");

        RequireProperty(typeof(WorkflowSlotConnectionBehavior), "Slot");
        RequireProperty(typeof(WorkflowSlotConnectionBehavior), "Tree");
        RequireProperty(typeof(WorkflowSlotConnectionBehavior), "IsEnabled");

        RequireProperty(typeof(ViewPool), "ItemsSource");
        RequireProperty(typeof(ViewPool), "ItemTemplate");
    }

    [TestMethod]
    public void SlotPartVisibilityContract_IsOptional_AndCachedPartsAreVisible()
    {
        var cached = typeof(WorkflowSlotLayoutBehavior).GetNestedType("CachedSlotPart", BindingFlags.NonPublic);
        Assert.IsNotNull(cached, "CachedSlotPart exists");
        Assert.IsTrue(
            typeof(VeloxDev.WorkflowSystem.ISlotPartVisibility).IsAssignableFrom(cached),
            "the cached part opts into the optional visibility contract");

        var slot = new VeloxDev.WorkflowSystem.SlotDefaultViewModel();
        var instance = Activator.CreateInstance(cached!, new object?[] { slot, 1d, 2d });
        Assert.IsTrue(
            ((VeloxDev.WorkflowSystem.ISlotPartVisibility)instance!).IsVisible,
            "JavaScript only sends parts it measured, so a cached part is visible");
    }
}
