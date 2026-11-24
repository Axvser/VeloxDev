using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

/// <summary>
/// Contracts of the surface-part registry: a part is re-resolved on every surface pass, and only an actual
/// replacement may move the subscriptions that part owns — old is unsubscribed before the new one is subscribed.
/// </summary>
[TestClass]
public class SurfacePartRegistryTests
{
    [TestMethod]
    public void Set_TheSameReference_DoesNotNotify()
    {
        var registry = new SurfacePartRegistry();
        var part = new object();
        var changes = 0;
        registry.OnChanged(WorkflowSurfacePart.Canvas, (_, _) => changes++);

        registry.Set(WorkflowSurfacePart.Canvas, part);
        registry.Set(WorkflowSurfacePart.Canvas, part);

        Assert.AreEqual(1, changes, "the second pass resolved the same control and must not churn a subscription");
    }

    [TestMethod]
    public void Set_ADifferentReference_ReportsOldAndNew()
    {
        var registry = new SurfacePartRegistry();
        var first = new object();
        var second = new object();
        (object?, object?)? seen = null;
        registry.OnChanged(WorkflowSurfacePart.Canvas, (oldValue, newValue) => seen = (oldValue, newValue));

        registry.Set(WorkflowSurfacePart.Canvas, first);
        registry.Set(WorkflowSurfacePart.Canvas, second);

        Assert.AreSame(second, seen!.Value.Item2);
        Assert.AreSame(first, seen!.Value.Item1);
    }

    [TestMethod]
    public void Binding_Replacement_DetachesOldBeforeAttachingNew()
    {
        var registry = new SurfacePartRegistry();
        var log = new List<string>();
        var first = new object();
        var second = new object();
        using var binding = new SurfacePartBinding<object>(
            registry,
            WorkflowSurfacePart.ScrollViewer,
            value => log.Add($"attach:{Tag(value)}"),
            value => log.Add($"detach:{Tag(value)}"));

        registry.Set(WorkflowSurfacePart.ScrollViewer, first);
        registry.Set(WorkflowSurfacePart.ScrollViewer, second);

        CollectionAssert.AreEqual(
            new[] { "attach:1", "detach:1", "attach:2" }, log.ToArray(),
            "the old part must be unsubscribed before the new one is subscribed");
        Assert.AreSame(second, binding.Current);

        string Tag(object value) => ReferenceEquals(value, first) ? "1" : "2";
    }

    [TestMethod]
    public void Binding_AttachesThePartRegisteredBeforeItWasCreated()
    {
        var registry = new SurfacePartRegistry();
        var part = new object();
        registry.Set(WorkflowSurfacePart.MinimapOverlay, part);

        object? attached = null;
        using var binding = new SurfacePartBinding<object>(
            registry, WorkflowSurfacePart.MinimapOverlay, value => attached = value, _ => { });

        Assert.AreSame(part, attached, "a binding created after resolution must adopt the current part");
        Assert.AreSame(part, binding.Current);
    }

    [TestMethod]
    public void Binding_Dispose_DetachesTheCurrentPartAndStopsListening()
    {
        var registry = new SurfacePartRegistry();
        var part = new object();
        var events = new List<string>();
        var binding = new SurfacePartBinding<object>(
            registry,
            WorkflowSurfacePart.PointerPressSource,
            _ => events.Add("attach"),
            _ => events.Add("detach"));

        registry.Set(WorkflowSurfacePart.PointerPressSource, part);
        binding.Dispose();
        registry.Set(WorkflowSurfacePart.PointerPressSource, new object());

        CollectionAssert.AreEqual(new[] { "attach", "detach" }, events.ToArray());
        Assert.IsNull(binding.Current);
    }

    [TestMethod]
    public void Clear_DetachesEveryPart_AndIsIdempotent()
    {
        var registry = new SurfacePartRegistry();
        var log = new List<string>();
        using var scroll = new SurfacePartBinding<object>(
            registry, WorkflowSurfacePart.ScrollViewer, _ => log.Add("scroll+"), _ => log.Add("scroll-"));
        using var minimap = new SurfacePartBinding<object>(
            registry, WorkflowSurfacePart.MinimapOverlay, _ => log.Add("minimap+"), _ => log.Add("minimap-"));
        registry.Set(WorkflowSurfacePart.ScrollViewer, new object());
        registry.Set(WorkflowSurfacePart.MinimapOverlay, new object());

        registry.Clear();
        registry.Clear();

        CollectionAssert.AreEqual(
            new[] { "scroll+", "minimap+", "scroll-", "minimap-" }, log.ToArray(),
            "every part is detached once and a second clear is a no-op");
        Assert.IsNull(registry.Get(WorkflowSurfacePart.ScrollViewer));
        Assert.IsNull(registry.Get(WorkflowSurfacePart.MinimapOverlay));
    }

    [TestMethod]
    public void OnChanged_Unsubscribe_StopsNotifications()
    {
        var registry = new SurfacePartRegistry();
        var changes = 0;
        var subscription = registry.OnChanged(WorkflowSurfacePart.GridDecorator, (_, _) => changes++);

        registry.Set(WorkflowSurfacePart.GridDecorator, new object());
        subscription.Dispose();
        registry.Set(WorkflowSurfacePart.GridDecorator, new object());

        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void TypedReads_ReturnTheRegisteredPartOrNull()
    {
        var registry = new SurfacePartRegistry();
        var canvas = new object();
        registry.Set(WorkflowSurfacePart.Canvas, canvas);

        Assert.AreSame(canvas, registry.Get<object>(WorkflowSurfacePart.Canvas));
        Assert.IsTrue(registry.TryGet<object>(WorkflowSurfacePart.Canvas, out var found));
        Assert.AreSame(canvas, found);
        Assert.IsNull(registry.Get<string>(WorkflowSurfacePart.Canvas), "a part of another type reads as absent");
        Assert.IsFalse(registry.TryGet<string>(WorkflowSurfacePart.Canvas, out _));
        Assert.IsNull(registry.Get<object>(WorkflowSurfacePart.ScrollViewer));
    }
}
