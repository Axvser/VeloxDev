using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a WinUI view's panel membership and bound state for the Core view pool.</summary>
public sealed class ViewContainer : IViewContainer<Panel, FrameworkElement, IWorkflowViewModel>
{
    private readonly HashSet<FrameworkElement> _attached = new(FrameworkElementReferenceComparer.Instance);
    private readonly ViewLayoutTracker _layout;

    // 订阅表必须与 ViewArranger 是同一份，否则 Deactivate 退订不到 Arranger 挂上的处理器。
    internal ViewContainer(ViewLayoutTracker layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

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
        // 先退订再做可见性与 DataContext 变更：回收后旧 item 的属性变更不能再写到这个视图上。
        _layout.Release(view);
        view.DataContext = null;
        view.Visibility = Visibility.Collapsed;
    }

    /// <inheritdoc />
    public void SetZOrder(FrameworkElement view, int zIndex)
    {
        // 节点 z 序由 ViewArranger 按 Anchor.Layer 命令式写入；这里再写一次会把它清成
        // ViewLayerPolicy 的常量 0。模板自身绑定的 ZIndex 也不该被容器覆盖。
    }

    /// <inheritdoc />
    public void Destroy(Panel host, FrameworkElement view)
    {
        _layout.Release(view);
        if (_attached.Remove(view))
        {
            host.Children.Remove(view);
        }
    }

    private sealed class FrameworkElementReferenceComparer : IEqualityComparer<FrameworkElement>
    {
        public static readonly FrameworkElementReferenceComparer Instance = new();

        public bool Equals(FrameworkElement? x, FrameworkElement? y) => ReferenceEquals(x, y);

        public int GetHashCode(FrameworkElement obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
