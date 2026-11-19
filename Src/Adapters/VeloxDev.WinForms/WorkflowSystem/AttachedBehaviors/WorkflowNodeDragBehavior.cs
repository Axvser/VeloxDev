using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms workflow node dragging behavior.
/// </summary>
public sealed class WorkflowNodeDragBehavior
{
    private sealed class DragState
    {
        public bool IsEnabled { get; set; }
        public bool IsDragging { get; set; }
        public Point LastPosition { get; set; }
        public Control? CoordinateHost { get; set; }
        public string? CoordinateHostName { get; set; }
        public Type? CoordinateHostType { get; set; }
        public HashSet<Control> HookedControls { get; } = [];
    }

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    /// <summary>
    /// Gets whether workflow node dragging behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).IsEnabled;
    }

    /// <summary>
    /// Sets whether workflow node dragging behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        var state = GetState(element);
        if (state.IsEnabled == value)
        {
            return;
        }

        Detach(element, state);

        state.IsEnabled = value;
        if (value)
        {
            Attach(element, state);
        }
        else
        {
            state.IsDragging = false;
            state.CoordinateHost = null;
        }
    }

    /// <summary>
    /// Gets the configured coordinate host name for drag calculations.
    /// </summary>
    public static string? GetCoordinateHostName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CoordinateHostName;
    }

    /// <summary>
    /// Sets the configured coordinate host name for drag calculations.
    /// </summary>
    public static void SetCoordinateHostName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostName = value;
    }

    /// <summary>
    /// Gets the configured coordinate host type for drag calculations.
    /// </summary>
    public static Type? GetCoordinateHostType(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).CoordinateHostType;
    }

    /// <summary>
    /// Sets the configured coordinate host type for drag calculations.
    /// </summary>
    public static void SetCoordinateHostType(Control element, Type? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostType = value;
    }

    private static void Attach(Control control, DragState state)
    {
        state.IsDragging = false;
        state.CoordinateHost = null;

        // 节点卡片启用拖拽时自动加 WS_CLIPCHILDREN：卡片自绘的圆角边框裁剪内部 TableLayoutPanel/标签/输入框，父窗绘制不会盖住内部控件造成内容闪烁；宿主无需改动。
        NativeWindowStyleHelper.EnsureClipChildren(control);

        HookControlTree(control, control);
    }

    private static void Detach(Control control, DragState state)
    {
        StopDragging(control, releaseCapture: false);
        UnhookControlTree(control, state);
    }

    private static void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control source)
        {
            return;
        }

        var control = ResolveOwnerControl(source);
        if (control is null)
        {
            return;
        }

        var node = ResolveNode(source);
        if (node is null)
        {
            return;
        }

        if (node.Parent?.VirtualLink.IsVisible == true)
        {
            return;
        }

        // 这一笔按下由表面路由 —— 卡片是启用的子控件，画布自己的 MouseDown 收不到它。订阅者在节点自己的
        // InputRelay 上置 PreventDefault 就是「这一次别拖」；不读它，节点上任何按住拖的定制都会和拖动
        // 抢同一串指针事件。
        if (WorkflowSurfaceBehavior.RouteComponentPress(control, node, e.Button, 1)?.PreventDefault == true)
        {
            return;
        }

        var state = GetState(control);
        state.CoordinateHost = ResolveCoordinateHost(control);
        if (state.CoordinateHost is null)
        {
            return;
        }

        state.IsDragging = true;
        state.LastPosition = state.CoordinateHost.PointToClient(Control.MousePosition);
        control.Capture = true;
    }

    private static void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }

        var control = ResolveOwnerControl(source);
        if (control is null)
        {
            return;
        }

        var state = GetState(control);
        if (!state.IsDragging || state.CoordinateHost is null)
        {
            return;
        }

        var node = ResolveNode(source);
        if (node is null)
        {
            return;
        }

        var host = state.CoordinateHost;
        var current = host.PointToClient(Control.MousePosition);
        var dx = current.X - state.LastPosition.X;
        var dy = current.Y - state.LastPosition.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (node.MoveCommand.CanExecute(new Offset(dx, dy)))
        {
            node.MoveCommand.Execute(new Offset(dx, dy));

            // WinForms 的 Invalidate() 只排队重绘请求，WM_PAINT 要等消息循环空闲才合并。拖拽中鼠标消息高频到达，画布重绘一直被推迟，节点旧位置的卡片影像与旧连线几何来不及擦掉，留下拖影。移动后立刻同步重绘坐标宿（画布：网格/连线），连线便能跟上且无残影。
            host.Invalidate();
            host.Update();

            // 同步递归重绘被拖卡片的整棵子树：卡片背景画完后，其内部透明子控件（标题栏、输出行面板、插槽视图）的重绘还排在消息循环里 —— 卡片移动后它们会在输出行上/下的透明缝隙里短暂露出旧背景残影，呈条状闪烁（全屏画布上最明显）。递归重绘把卡片与各层在一帧内合成。
            RedrawTree(control);
        }

        state.LastPosition = current;
    }

    private static void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control source)
        {
            return;
        }

        var control = ResolveOwnerControl(source);
        if (control is null)
        {
            return;
        }

        StopDragging(control);
    }

    private static void OnMouseCaptureChanged(object? sender, EventArgs e)
    {
        if (sender is Control source && ResolveOwnerControl(source) is Control control && !control.Capture)
        {
            StopDragging(control, releaseCapture: false);
        }
    }

    private static Control? ResolveOwnerControl(Control control)
    {
        var current = control;
        while (current is not null)
        {
            if (States.TryGetValue(current, out var state) && state.IsEnabled)
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }

        if (ResolveOwnerControl(source) is Control control)
        {
            StopDragging(control, releaseCapture: false);
        }
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (sender is not Control parent)
        {
            return;
        }

        var owner = ResolveOwnerControl(parent) ?? parent;
        if (!States.TryGetValue(owner, out var state) || !state.IsEnabled || e.Control is not { } added)
        {
            return;
        }

        HookControlTree(owner, added);
    }

    private static void OnControlRemoved(object? sender, ControlEventArgs e)
    {
        if (sender is not Control parent)
        {
            return;
        }

        var owner = ResolveOwnerControl(parent) ?? parent;
        if (!States.TryGetValue(owner, out var state) || e.Control is not { } removed)
        {
            return;
        }

        UnhookControlTree(removed, state);
    }

    private static void HookControlTree(Control owner, Control control)
    {
        HookControl(owner, control);

        foreach (var child in control.Controls.OfType<Control>())
        {
            HookControlTree(owner, child);
        }
    }

    private static void HookControl(Control owner, Control control)
    {
        var state = GetState(owner);
        if (!state.HookedControls.Add(control))
        {
            return;
        }

        control.ControlAdded += OnControlAdded;
        control.ControlRemoved += OnControlRemoved;

        if (!IsDragHandle(control))
        {
            return;
        }

        control.MouseDown += OnMouseDown;
        control.MouseMove += OnMouseMove;
        control.MouseUp += OnMouseUp;
        control.MouseCaptureChanged += OnMouseCaptureChanged;
        control.Disposed += OnDisposed;
    }

    private static void UnhookControlTree(Control control, DragState state)
    {
        foreach (var child in control.Controls.OfType<Control>())
        {
            UnhookControlTree(child, state);
        }

        UnhookControl(control, state);
    }

    private static void UnhookControl(Control control, DragState state)
    {
        if (!state.HookedControls.Remove(control))
        {
            return;
        }

        control.ControlAdded -= OnControlAdded;
        control.ControlRemoved -= OnControlRemoved;
        control.MouseDown -= OnMouseDown;
        control.MouseMove -= OnMouseMove;
        control.MouseUp -= OnMouseUp;
        control.MouseCaptureChanged -= OnMouseCaptureChanged;
        control.Disposed -= OnDisposed;
    }

    private static bool IsDragHandle(Control control)
        => control is not TextBoxBase
            and not ComboBox
            and not ButtonBase
            and not CheckBox
            && ResolveSlot(control) is null;

    private static void StopDragging(Control control, bool releaseCapture = true)
    {
        var state = GetState(control);
        state.IsDragging = false;
        state.CoordinateHost = null;

        if (releaseCapture && control.Capture)
        {
            control.Capture = false;
        }
    }

    private static Control? ResolveCoordinateHost(Control control)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(control, hostName!);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Panel);
        var current = control.Parent;
        while (current is not null)
        {
            if (hostType.IsAssignableFrom(current.GetType()))
            {
                return current;
            }

            current = current.Parent;
        }

        return control.Parent;
    }

    private static Control? ResolveNamedHost(Control control, string hostName)
    {
        var current = control;
        while (current is not null)
        {
            if (string.Equals(current.Name, hostName, StringComparison.Ordinal))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IWorkflowSlotViewModel? ResolveSlot(Control control)
    {
        if (control.Tag is IWorkflowSlotViewModel taggedSlot)
        {
            return taggedSlot;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var propertyName in new[] { "ViewModel", "DataContext", "BindingContext" })
        {
            var property = control.GetType().GetProperty(propertyName, flags);
            if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetValue(control) is IWorkflowSlotViewModel slot)
            {
                return slot;
            }
        }

        return null;
    }

    private static IWorkflowNodeViewModel? ResolveNode(Control control)
    {
        var current = control;
        while (current is not null)
        {
            var node = ResolveNodeFromControl(current);
            if (node is not null)
            {
                return node;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IWorkflowNodeViewModel? ResolveNodeFromControl(Control control)
    {
        if (control.Tag is IWorkflowNodeViewModel taggedNode)
        {
            return taggedNode;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var propertyName in new[] { "ViewModel", "DataContext", "BindingContext" })
        {
            var property = control.GetType().GetProperty(propertyName, flags);
            if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetValue(control) is IWorkflowNodeViewModel node)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Synchronously repaints the control and all its child controls (the transparent compositing chain). WinForms' <see cref="Control.Update"/>
    /// only processes a single window's WM_PAINT synchronously. Transparent child controls (title bar, output rows, slot views) must be
    /// recomposited from the parent background after the parent moves; if their repaint stays queued asynchronously in the message loop,
    /// they briefly show old background residue — during a node drag this appears as bar-shaped flicker above/below the output rows.
    /// Recursive calls ensure the whole subtree is drawn in the same frame.
    /// </summary>
    private static void RedrawTree(Control root)
    {
        if (root is null || root.IsDisposed || !root.IsHandleCreated)
        {
            return;
        }

        // 先同步重绘父级（不透明背景/边框），再逐层重绘子控件，透明合成顺序才正确（子控件从已更新的父背景上合成）。
        root.Invalidate();
        root.Update();

        foreach (Control? child in root.Controls)
        {
            if (child is not null)
            {
                RedrawTree(child);
            }
        }
    }

    private static DragState GetState(Control element)
        => States.GetValue(element, static _ => new DragState());
}
