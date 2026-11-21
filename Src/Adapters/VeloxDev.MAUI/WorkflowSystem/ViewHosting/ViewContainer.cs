using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a MAUI view's layout membership and bound state for the main view pool.</summary>
/// <remarks>
/// Recycled views stay in the host's children. Hiding and unbinding are cheaper in MAUI than removing
/// and re-inserting the view, so <see cref="Detach"/> is intentionally empty.
/// </remarks>
public sealed class ViewContainer : IViewContainer<Layout, View, IWorkflowComponent>
{
    private readonly HashSet<View> _attached = new(ViewReferenceComparer.Instance);
    private readonly ViewArranger? _arranger;

    /// <summary>Creates a container without a geometry layer to notify.</summary>
    public ViewContainer()
    {
    }

    /// <summary>Creates a container that releases <paramref name="arranger"/>'s item subscription when a view leaves the pool.</summary>
    /// <param name="arranger">The geometry layer that subscribed to the item; <see langword="null"/> leaves it untouched.</param>
    public ViewContainer(ViewArranger? arranger)
    {
        _arranger = arranger;
    }

    /// <inheritdoc />
    public void Attach(Layout host, View view)
    {
        if (_attached.Add(view))
        {
            host.Children.Add(view);
        }
    }

    /// <inheritdoc />
    public void Detach(Layout host, View view)
    {
        // 池化视图不摘树：Deactivate 已经隐藏并解绑，移除会触发昂贵的 MAUI 重排。
    }

    /// <inheritdoc />
    public void Activate(View view, IWorkflowComponent item)
    {
        view.BindingContext = item;
        view.IsVisible = true;
    }

    /// <inheritdoc />
    public void Deactivate(View view)
    {
        view.BindingContext = null;
        view.IsVisible = false;
        view.ZIndex = -100;

        // 隐藏后不再需要跟随模型几何；下次物化时 ViewArranger 会重新订阅。
        _arranger?.Release(view);
    }

    /// <inheritdoc />
    public void SetZOrder(View view, int zIndex) => view.ZIndex = zIndex;

    /// <inheritdoc />
    public void Destroy(Layout host, View view)
    {
        _arranger?.Forget(view);

        if (_attached.Remove(view))
        {
            host.Children.Remove(view);
        }
    }

    private sealed class ViewReferenceComparer : IEqualityComparer<View>
    {
        public static readonly ViewReferenceComparer Instance = new();

        public bool Equals(View? x, View? y) => ReferenceEquals(x, y);

        public int GetHashCode(View obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
