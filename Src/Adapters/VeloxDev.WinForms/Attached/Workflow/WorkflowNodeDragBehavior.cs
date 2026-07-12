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

        var current = state.CoordinateHost.PointToClient(Control.MousePosition);
        var dx = current.X - state.LastPosition.X;
        var dy = current.Y - state.LastPosition.Y;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        if (node.MoveCommand.CanExecute(new Offset(dx, dy)))
        {
            node.MoveCommand.Execute(new Offset(dx, dy));
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

    private static DragState GetState(Control element)
        => States.GetValue(element, static _ => new DragState());
}
