using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Easel.Visuals;
using VeloxDev.WorkflowSystem;
using VisualPanel = Easel.Visuals.Controls.Panel;
using XamlVisibility = Easel.Controls.Visibility;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a visual view's canvas membership and bound state for the main view pool.</summary>
/// <remarks>
/// <para>
/// A deactivated view stays a child of the canvas, hidden and unbound, so recycling never re-adds it. The first
/// attach places the view by the z-order its template already wrote: a link (<c>Panel.ZIndex = -1</c>) lands below
/// the node cards instead of painting over them. The container never writes that property itself.
/// </para>
/// <para>
/// A newly created view receives its item only after it has been parented, so an <c>ElementName</c> or ancestor
/// binding can resolve up the tree; a recycled view is already parented and binds immediately.
/// </para>
/// <para>
/// A <see cref="Destroy"/>d view is removed from the canvas and disposed; <see cref="ReleaseAll(VisualPanel)"/>
/// removes and disposes whatever is left when the whole pipeline is torn down.
/// </para>
/// </remarks>
public sealed class ViewContainer : IViewContainer<VisualPanel, VisualElement, IWorkflowComponent>
{
    private readonly HashSet<VisualElement> _attached = new(ReferenceComparer.Instance);
    private readonly Dictionary<VisualElement, IWorkflowComponent> _pending = new(ReferenceComparer.Instance);

    /// <inheritdoc />
    public void Attach(VisualPanel host, VisualElement view)
    {
        if (!_attached.Add(view))
        {
            return;
        }

        AddAtZOrder(host, view);

        // 新视图在 Activate 时还没进树，绑定要等挂上父级后再写：ElementName/祖先绑定只能沿父链解析。
        if (TryTakePending(view, out var item))
        {
            Apply(view, item);
        }
    }

    /// <inheritdoc />
    public void Detach(VisualPanel host, VisualElement view)
    {
        // 池化视图留在画布上只隐藏：移除会让下一次复用时被迫重挂，绑定也要重走一遍。
    }

    /// <inheritdoc />
    public void Activate(VisualElement view, IWorkflowComponent item)
    {
        if (_attached.Contains(view))
        {
            _pending.Remove(view);
            Apply(view, item);
            return;
        }

        // 新视图等 Attach 挂上父级后再绑定；此时它还没进树，早写 DataContext 会让 ElementName 绑定解析失败。
        _pending[view] = item;
    }

    /// <inheritdoc />
    public void Deactivate(VisualElement view)
    {
        _pending.Remove(view);
        view.DataContext = null;
        view.Visibility = XamlVisibility.Collapsed;
    }

    /// <inheritdoc />
    public void SetZOrder(VisualElement view, int zIndex)
    {
        // 模板自己绑 Panel.ZIndex（节点 Anchor.Layer、连线 -1），容器回写会和绑定互相覆盖。
    }

    /// <inheritdoc />
    public void Destroy(VisualPanel host, VisualElement view)
    {
        _pending.Remove(view);
        if (_attached.Remove(view))
        {
            host.RemoveChild(view);
            view.Dispose();
        }
    }

    /// <summary>Removes and disposes every view this container attached to <paramref name="host"/>.</summary>
    /// <param name="host">The canvas the views belong to.</param>
    public void ReleaseAll(VisualPanel host)
    {
        foreach (var view in _attached)
        {
            host.RemoveChild(view);
            view.Dispose();
        }

        _attached.Clear();
        _pending.Clear();
    }

    private static void Apply(VisualElement view, IWorkflowComponent item)
    {
        view.DataContext = item;
        view.Visibility = XamlVisibility.Visible;
    }

    private bool TryTakePending(VisualElement view, out IWorkflowComponent item)
    {
        if (_pending.TryGetValue(view, out item!))
        {
            _pending.Remove(view);
            return true;
        }

        item = null!;
        return false;
    }

    // 首次挂载按模板已写好的 ZIndex 插位：负值（连线）落在节点前面，同值保持先后顺序。
    private static void AddAtZOrder(VisualPanel host, VisualElement view)
    {
        var zIndex = VisualPanel.GetZIndex(view);
        var index = host.Children.Count;
        for (var candidate = 0; candidate < host.Children.Count; candidate++)
        {
            if (host.Children[candidate] is VisualElement sibling && VisualPanel.GetZIndex(sibling) > zIndex)
            {
                index = candidate;
                break;
            }
        }

        if (index == host.Children.Count)
        {
            host.Add(view);
            return;
        }

        // 走 Children.Insert 就绕过了 VisualElement.Add 的隐式样式与排版失效，这里补上这两步。
        host.Children.Insert(index, view);
        view.RefreshStyle();
        host.InvalidateMeasure();
    }

    // 视觉元素按引用身份去重：同一个视图复用时不能依赖 Equals。
    private sealed class ReferenceComparer : IEqualityComparer<VisualElement>
    {
        public static readonly ReferenceComparer Instance = new();

        public bool Equals(VisualElement? x, VisualElement? y) => ReferenceEquals(x, y);

        public int GetHashCode(VisualElement obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
