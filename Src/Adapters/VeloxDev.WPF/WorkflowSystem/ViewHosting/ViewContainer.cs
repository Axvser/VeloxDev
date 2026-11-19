using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a WPF view's panel membership and bound state for the main view pool.</summary>
public sealed class ViewContainer : IViewContainer<Panel, FrameworkElement, IWorkflowViewModel>
{
    private readonly HashSet<FrameworkElement> _attached = new(FrameworkElementReferenceComparer.Instance);

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
        // 池化视图不摘树：Deactivate 已经隐藏并清掉 DataContext。
    }

    /// <inheritdoc />
    public void Activate(FrameworkElement view, IWorkflowViewModel item)
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

    // 视觉元素按引用身份去重：同一个视图复用时不能依赖 Equals。
    private sealed class FrameworkElementReferenceComparer : IEqualityComparer<FrameworkElement>
    {
        public static readonly FrameworkElementReferenceComparer Instance = new();

        public bool Equals(FrameworkElement? x, FrameworkElement? y) => ReferenceEquals(x, y);

        public int GetHashCode(FrameworkElement obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
