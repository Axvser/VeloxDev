using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

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

    /// <summary>
    /// Gets whether workflow slot connection behavior is enabled for the specified control.
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
    /// Sets whether workflow slot connection behavior is enabled for the specified control.
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

        element.MouseDown -= OnMouseDown;
        element.Disposed -= OnDisposed;

        state.IsEnabled = value;
        if (value)
        {
            element.MouseDown += OnMouseDown;
            element.Disposed += OnDisposed;
        }
    }

    private static void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control control)
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

        if (!activeConnection.Tree.VirtualLink.IsVisible)
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

        if (!activeConnection.Tree.VirtualLink.IsVisible)
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
        => _activeConnection is not null && !_activeConnection.Tree.VirtualLink.IsVisible;

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
