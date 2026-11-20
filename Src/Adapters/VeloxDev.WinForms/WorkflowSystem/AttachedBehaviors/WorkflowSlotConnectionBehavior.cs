using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms workflow slot connection behavior.
/// </summary>
public sealed class WorkflowSlotConnectionBehavior
{
    private sealed class ActiveConnectionState
    {
        public Control SenderControl { get; set; } = null!;
        public Control SurfaceControl { get; set; } = null!;
        public IWorkflowSlotViewModel SenderSlot { get; set; } = null!;
        public IWorkflowTreeViewModel Tree { get; set; } = null!;
        public Point StartScreenPoint { get; set; }
        public Anchor StartAnchor { get; set; } = new Anchor();
    }

    private sealed class SlotConnectionState
    {
        public bool IsEnabled { get; set; }
    }

    private static readonly ConditionalWeakTable<Control, SlotConnectionState> States = new();
    private static ConnectionMessageFilter? _messageFilter;
    private static ActiveConnectionState? _activeConnection;

    /// <summary>Whether the slot participates in drag-to-connect.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        new PropertyMetadata(false, OnIsEnabledPropertyChanged));

    /// <summary>
    /// Gets whether workflow slot connection behavior is enabled for the specified control.
    /// </summary>
    public static bool GetIsEnabled(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return (bool)IsEnabledProperty.GetValue(element)!;
    }

    /// <summary>
    /// Sets whether workflow slot connection behavior is enabled for the specified control.
    /// </summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        IsEnabledProperty.SetValue(element, value);
    }

    private static void OnIsEnabledPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control || e.NewValue is not bool value)
        {
            return;
        }

        var state = GetState(control);
        if (state.IsEnabled == value)
        {
            return;
        }

        control.MouseDown -= OnMouseDown;
        control.MouseUp -= OnMouseUp;
        control.Disposed -= OnDisposed;

        state.IsEnabled = value;
        if (value)
        {
            control.MouseDown += OnMouseDown;
            control.MouseUp += OnMouseUp;
            control.Disposed += OnDisposed;
        }
    }

    private static void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        if (HasInactiveConnection())
        {
            ClearActiveConnection();
        }

        var slot = ResolveSlot(control);
        if (slot is null)
        {
            return;
        }

        // 每个按钮都过表面路由 —— 订阅者在插槽自己的 InputRelay 上置 PreventDefault 就是
        // 「这一笔别连」，无论它是开始还是完成；右键/中键的动作也走同一套 Core 输入事件。
        var press = WorkflowSurfaceBehavior.RouteComponentPress(control, slot, e.Button, e.Clicks);

        if (e.Button != MouseButtons.Left || press?.PreventDefault == true)
        {
            return;
        }

        if (_activeConnection is not null)
        {
            TryCompleteConnection(control, slot);
            return;
        }

        if (slot.Parent?.Parent is not IWorkflowTreeViewModel tree)
        {
            return;
        }

        if (!slot.SendConnectionCommand.CanExecute(null))
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);

        var surface = FindSurfaceControl(control);
        _activeConnection = new ActiveConnectionState
        {
            SenderControl = control,
            SurfaceControl = surface,
            SenderSlot = slot,
            Tree = tree,
            StartScreenPoint = Cursor.Position,
            StartAnchor = slot.Anchor,
        };

        EnsureMessageFilter();
        WorkflowSurfaceBehavior.Refresh(surface);
    }

    private static void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var slot = ResolveSlot(control);
        if (slot is not null)
        {
            WorkflowSurfaceBehavior.RouteComponentRelease(control, slot, e.Button, e.Clicks);
        }
    }

    private static void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        if (ReferenceEquals(_activeConnection?.SenderControl, control))
        {
            CancelConnection(_activeConnection);
        }
    }

    private static void TryCompleteConnection(Control control, IWorkflowSlotViewModel slot)
    {
        var activeConnection = _activeConnection;
        if (activeConnection is null)
        {
            return;
        }

        if (ReferenceEquals(activeConnection.SenderControl, control) ||
            ReferenceEquals(activeConnection.SenderSlot, slot))
        {
            return;
        }

        if (!slot.ReceiveConnectionCommand.CanExecute(null))
        {
            return;
        }

        slot.ReceiveConnectionCommand.Execute(null);
        var surface = activeConnection.SurfaceControl;
        ClearActiveConnection();
        WorkflowSurfaceBehavior.Refresh(surface);
    }

    private static void EnsureMessageFilter()
    {
        if (_messageFilter is not null)
        {
            return;
        }

        _messageFilter = new ConnectionMessageFilter();
        Application.AddMessageFilter(_messageFilter);
    }

    private static void DetachMessageFilter()
    {
        if (_messageFilter is null)
        {
            return;
        }

        Application.RemoveMessageFilter(_messageFilter);
        _messageFilter = null;
    }

    private static void UpdatePointer()
    {
        var activeConnection = _activeConnection;
        if (activeConnection is null)
        {
            ClearActiveConnection();
            return;
        }

        // VirtualLink 是可写的模型属性；宿主可能在拖拽途中清空它（换文档/卸载会话）。
        if (activeConnection.Tree.VirtualLink is not { IsVisible: true })
        {
            ClearActiveConnection();
            return;
        }

        var current = Cursor.Position;
        var pointer = new Anchor(
            activeConnection.StartAnchor.Horizontal + (current.X - activeConnection.StartScreenPoint.X),
            activeConnection.StartAnchor.Vertical + (current.Y - activeConnection.StartScreenPoint.Y),
            activeConnection.StartAnchor.Layer);

        if (activeConnection.Tree.SetPointerCommand.CanExecute(pointer))
        {
            activeConnection.Tree.SetPointerCommand.Execute(pointer);
        }

        // 拖拽中指针下的候选接收口：几何命中不看可视树，本家靠 WindowFromPoint 找目标的那一套只在松手时用。
        activeConnection.Tree.StandardSetPreviewReceiver(
            SlotHitTestEx.HitTest(activeConnection.Tree, pointer.Horizontal, pointer.Vertical));

        WorkflowSurfaceBehavior.Refresh(activeConnection.SurfaceControl);
    }

    private static void TryCompleteFromMouseUp()
    {
        var activeConnection = _activeConnection;
        if (activeConnection is null)
        {
            ClearActiveConnection();
            return;
        }

        if (activeConnection.Tree.VirtualLink is not { IsVisible: true })
        {
            ClearActiveConnection();
            return;
        }

        var targetControl = GetControlAtScreenPoint(Cursor.Position);
        while (targetControl is not null)
        {
            if (GetIsEnabled(targetControl))
            {
                var slot = ResolveSlot(targetControl);
                if (slot is not null)
                {
                    if (ReferenceEquals(activeConnection.SenderControl, targetControl) ||
                        ReferenceEquals(activeConnection.SenderSlot, slot))
                    {
                        return;
                    }

                    if (slot.ReceiveConnectionCommand.CanExecute(null))
                    {
                        slot.ReceiveConnectionCommand.Execute(null);
                        var surface = activeConnection.SurfaceControl;
                        ClearActiveConnection();
                        WorkflowSurfaceBehavior.Refresh(surface);
                        return;
                    }
                }

                CancelConnection(activeConnection);
                return;
            }

            targetControl = targetControl.Parent;
        }

        CancelConnection(activeConnection);
    }

    private static void CancelConnection(ActiveConnectionState activeConnection)
    {
        if (activeConnection.Tree.ResetVirtualLinkCommand.CanExecute(null))
        {
            activeConnection.Tree.ResetVirtualLinkCommand.Execute(null);
        }

        var surface = activeConnection.SurfaceControl;
        ClearActiveConnection();
        if (!surface.IsDisposed)
        {
            WorkflowSurfaceBehavior.Refresh(surface);
        }
    }

    private static IWorkflowSlotViewModel? ResolveSlot(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.Tag is IWorkflowSlotViewModel taggedSlot)
            {
                return taggedSlot;
            }

            foreach (var propertyName in new[] { "ViewModel", "DataContext", "BindingContext" })
            {
                var property = current.GetType().GetProperty(propertyName, flags);
                if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (property.GetValue(current) is IWorkflowSlotViewModel slot)
                {
                    return slot;
                }
            }
        }

        return null;
    }

    private static Control FindSurfaceControl(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        Control? current = control;
        while (current is not null)
        {
            if (WorkflowSurfaceBehavior.GetIsEnabled(current))
            {
                return current;
            }

            current = current.Parent;
        }

        return control.TopLevelControl ?? control;
    }

    private static Control? GetControlAtScreenPoint(Point screenPoint)
    {
        var handle = NativeMethods.WindowFromPoint(screenPoint);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        return Control.FromChildHandle(handle) ?? Control.FromHandle(handle);
    }

    private sealed class ConnectionMessageFilter : IMessageFilter
    {
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONUP = 0x0202;

        public bool PreFilterMessage(ref Message m)
        {
            if (_activeConnection is null)
            {
                return false;
            }

            switch (m.Msg)
            {
                case WM_MOUSEMOVE:
                    UpdatePointer();
                    break;
                case WM_LBUTTONUP:
                    TryCompleteFromMouseUp();
                    break;
            }

            return false;
        }
    }

    private static bool HasInactiveConnection()
        => _activeConnection is not null
           && _activeConnection.Tree.VirtualLink is not { IsVisible: true };

    private static void ClearActiveConnection()
    {
        _activeConnection = null;
        DetachMessageFilter();
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(Point point);
    }

    private static SlotConnectionState GetState(Control element)
        => States.GetValue(element, static _ => new SlotConnectionState());
}
