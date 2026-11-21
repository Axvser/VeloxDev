using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a Jalium view's panel membership and bound state for the main view pool.</summary>
/// <remarks>
/// Recycled views stay in <see cref="Panel.Children"/>; <see cref="Deactivate"/> hides them and clears
/// <see cref="FrameworkElement.DataContext"/>, and only <see cref="Destroy"/> removes them.
/// </remarks>
public sealed class ViewContainer : IViewContainer<Panel, FrameworkElement, IWorkflowComponent>
{
    private readonly HashSet<FrameworkElement> _attached = new(ElementReferenceComparer.Instance);

    /// <inheritdoc />
    public void Attach(Panel host, FrameworkElement view)
    {
        if (_attached.Add(view))
        {
            host.Children.Add(view);
        }
    }

    /// <inheritdoc />
    public void Detach(Panel host, FrameworkElement view)
    {
        // 回收的视图留在 Children 里：Deactivate 已经隐藏并清掉 DataContext，摘树会失去复用意义。
    }

    /// <inheritdoc />
    public void Activate(FrameworkElement view, IWorkflowComponent item)
    {
        view.DataContext = item;
        view.Visibility = Visibility.Visible;
    }

    /// <inheritdoc />
    public void Deactivate(FrameworkElement view)
    {
        view.DataContext = null;
        view.Visibility = Visibility.Collapsed;
    }

    /// <inheritdoc />
    public void SetZOrder(FrameworkElement view, int zIndex)
    {
        // 节点/连线模板自己绑 Panel.ZIndex（Anchor.Layer / -1），容器不再写一遍。
    }

    /// <inheritdoc />
    public void Destroy(Panel host, FrameworkElement view)
    {
        if (_attached.Remove(view))
        {
            host.Children.Remove(view);
        }
    }

    private sealed class ElementReferenceComparer : IEqualityComparer<FrameworkElement>
    {
        public static readonly ElementReferenceComparer Instance = new();

        public bool Equals(FrameworkElement? x, FrameworkElement? y) => ReferenceEquals(x, y);

        // 元素身份按引用；不能依赖可能被重写的 Equals/GetHashCode 来判同一实例。
        public int GetHashCode(FrameworkElement obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
