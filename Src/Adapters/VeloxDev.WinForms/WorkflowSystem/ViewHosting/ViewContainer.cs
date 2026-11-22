using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

using VeloxDev.WorkflowSystem.Attachments;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Owns a WinForms view's membership in its host control and its role-based binding.</summary>
/// <remarks>
/// A pooled control stays in <see cref="Control.Controls"/> while it is recycled; deactivation only hides it and
/// clears the context, so the next item can reuse the same window without a handle round-trip. Binding itself
/// belongs to the node and link attachments and to the slot view's <c>ViewModel</c> property, which already know
/// how to release their subscriptions. Because
/// WinForms has no z-order property, the container also keeps link views behind node views when a view is attached.
/// </remarks>
public sealed class ViewContainer : IViewContainer<Control, Control, IWorkflowComponent>
{
    private static readonly string[] ContextPropertyNames = ["ViewModel", "DataContext", "BindingContext"];

    private readonly HashSet<Control> attached = new(ControlReferenceComparer.Instance);
    private readonly HashSet<Control> linkViews = new(ControlReferenceComparer.Instance);

    /// <inheritdoc />
    public void Attach(Control host, Control view)
    {
        // 池化的控件可能还在 Controls 里：重复 Add 会让 WinForms 把它重新排到末尾，所以只在第一次挂。
        if (!attached.Add(view))
        {
            return;
        }

        host.Controls.Add(view);

        // 这家没有 Panel.ZIndex，Controls 的末位在最下面。池是异步物化的，可见集变化时 ArrangeLinkViews
        // 跑在视图生成之前，所以「节点在上、连线在下」只能在这里当场定下来。
        if (linkViews.Contains(view))
        {
            view.SendToBack();
        }
        else
        {
            view.BringToFront();
        }
    }

    /// <inheritdoc />
    public void Detach(Control host, Control view)
    {
        // 回收不摘树：Deactivate 已经隐藏并清掉 Tag，下次物化直接复用这个子窗口。
    }

    /// <inheritdoc />
    public void Activate(Control view, IWorkflowComponent item)
    {
        if (item is ILinkComponent)
        {
            linkViews.Add(view);
        }
        else
        {
            linkViews.Remove(view);
        }

        ApplyBinding(view, item);

        // Deactivate 清掉了 Tag，而附件在「同一视图再次绑回同一 item」时走 ReferenceEquals 早退，
        // 不会把 Tag 写回。这里无条件恢复：ResolveNode / ResolveSlot 与表面的可见性重投影都靠它。
        view.Tag = item;
        view.Visible = true;
    }

    /// <inheritdoc />
    public void Deactivate(Control view)
    {
        view.Visible = false;
        view.Tag = null;
    }

    /// <inheritdoc />
    public void SetZOrder(Control view, int zIndex)
    {
        // WinForms 没有 Panel.ZIndex；层级在 Attach 里按角色定好，ArrangeLinkViews 仍会补一次连线的 SendToBack。
    }

    /// <inheritdoc />
    public void Destroy(Control host, Control view)
    {
        linkViews.Remove(view);

        if (attached.Remove(view))
        {
            host.Controls.Remove(view);
        }
    }

    private static void ApplyBinding(Control view, IWorkflowComponent item)
    {
        // 连线视图由适配器那份「附加」收绑：订阅、雕刻、命中契约都归它，控件本身只是用户画的视图。
        if (item is ILinkComponent link && WorkflowLinkAttachment.For(view) is { } linkAttachment)
        {
            linkAttachment.Bind(link);
            return;
        }

        // 节点卡片同理：绑定、摆位、折叠与模型事件都归附加。
        if (item is INodeComponent node && WorkflowNodeAttachment.For(view) is { } nodeAttachment)
        {
            nodeAttachment.Node = node;
            return;
        }

        // 插槽视图同理：绑定、状态着色、通道事件与拖拽连线都归附加。
        if (item is ISlotComponent slot && WorkflowSlotAttachment.For(view) is { } slotAttachment)
        {
            slotAttachment.Slot = slot;
            return;
        }

        // 其余角色：WinForms 没有 DataContext；沿用 Tag 作上下文的约定，并对三个常见属性名做尽力而为的接线。
        view.Tag = item;

        foreach (var propertyName in ContextPropertyNames)
        {
            var property = view.GetType().GetProperty(propertyName);
            if (property is null || !property.CanWrite || !property.PropertyType.IsInstanceOfType(item))
            {
                continue;
            }

            try
            {
                property.SetValue(view, item);
            }
            catch
            {
                // 尽力而为的上下文接线；视图可能需要设计器设置。
            }
        }
    }

    private sealed class ControlReferenceComparer : IEqualityComparer<Control>
    {
        public static readonly ControlReferenceComparer Instance = new();

        public bool Equals(Control? x, Control? y) => ReferenceEquals(x, y);

        public int GetHashCode(Control obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
