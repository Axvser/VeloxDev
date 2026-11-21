using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns an Avalonia view's panel membership and bound state for the main view pool.</summary>
public sealed class ViewContainer : IViewContainer<Panel, Control, IWorkflowComponent>
{
    private readonly HashSet<Control> _attached = new(ControlReferenceComparer.Instance);

    /// <inheritdoc />
    public void Attach(Panel host, Control view)
    {
        if (_attached.Add(view))
        {
            host.Children.Add(view);
        }
    }

    /// <inheritdoc />
    public void Detach(Panel host, Control view)
    {
        // 池化视图不摘树：Deactivate 已经隐藏并清掉 DataContext。
    }

    /// <inheritdoc />
    public void Activate(Control view, IWorkflowComponent item)
    {
        view.DataContext = item;
        view.IsVisible = true;
    }

    /// <inheritdoc />
    public void Deactivate(Control view)
    {
        view.DataContext = null;
        view.IsVisible = false;
    }

    /// <inheritdoc />
    public void SetZOrder(Control view, int zIndex)
    {
        // 节点/连线模板自己绑 ZIndex（Anchor.Layer / -1），容器不再写一遍。
    }

    /// <inheritdoc />
    public void Destroy(Panel host, Control view)
    {
        if (_attached.Remove(view))
        {
            host.Children.Remove(view);
        }
    }

    private sealed class ControlReferenceComparer : IEqualityComparer<Control>
    {
        public static readonly ControlReferenceComparer Instance = new();

        public bool Equals(Control? x, Control? y) => ReferenceEquals(x, y);

        public int GetHashCode(Control obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
