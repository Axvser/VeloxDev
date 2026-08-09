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

        // 节点卡片启用拖动时自动加 WS_CLIPCHILDREN：卡片自绘圆角边框时裁剪内部
        // TableLayoutPanel/标签/输入框等子控件区域，避免父窗口绘制覆盖内部控件
        // 导致的内容闪烁。宿主无需任何改动。
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

            // WinForms 的 Invalidate() 只把重绘请求排队，WM_PAINT 要等消息循环空闲
            // 才合并处理。拖动期间鼠标消息高频到达，画布重绘被不断延后，节点旧位置
            // 的卡片图像与旧连线几何来不及擦除，形成拖尾残影。移动后立即同步重绘
            // 坐标宿主（画布：网格/连线），保证连线跟手且无残影。
            host.Invalidate();
            host.Update();

            // 递归同步重绘被拖动卡片整棵子树：卡片背景同步绘制后，内部透明子控件
            // （标题头、输出行面板、槽位视图）的重绘仍在消息循环中排队——卡片移动
            // 后它们会短暂显示旧背景残留，在输出行组上下方的透明空白区表现为条形
            // 闪烁（全屏大画布下最明显）。递归重绘让卡片与所有透明层同帧完成合成。
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
        if (!States.TryGetValue(owner, out var state) || !state.IsEnabled)
        {
            return;
        }

        HookControlTree(owner, e.Control);
    }

    private static void OnControlRemoved(object? sender, ControlEventArgs e)
    {
        if (sender is not Control parent)
        {
            return;
        }

        var owner = ResolveOwnerControl(parent) ?? parent;
        if (!States.TryGetValue(owner, out var state))
        {
            return;
        }

        UnhookControlTree(e.Control, state);
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
    /// 同步重绘控件及其全部子控件（透明合成链）。WinForms 的 <see cref="Control.Update"/>
    /// 只同步处理单个窗口的 WM_PAINT；透明子控件（标题头、输出行、槽位视图）在父级
    /// 移动后需要重新从父背景合成，若其重绘留在消息循环中异步排队，会短暂显示旧背景
    /// 残留——拖动节点时表现为卡片内输出行上下方的条形闪烁。递归调用保证整棵子树
    /// 在同一帧完成绘制。
    /// </summary>
    private static void RedrawTree(Control root)
    {
        if (root is null || root.IsDisposed || !root.IsHandleCreated)
        {
            return;
        }

        // 先让父级同步重绘（不透明背景/边框），再逐层同步子控件，保证透明合成
        // 顺序正确（子控件从已更新的父背景上合成自己的内容）。
        root.Invalidate();
        root.Update();

        foreach (Control child in root.Controls)
        {
            RedrawTree(child);
        }
    }

    private static DragState GetState(Control element)
        => States.GetValue(element, static _ => new DragState());
}
