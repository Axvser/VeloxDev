using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinForms workflow slot layout synchronization behavior.
/// </summary>
public sealed class WorkflowSlotLayoutBehavior
{
    private sealed class LayoutState
    {
        public bool IsEnabled { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public bool SyncPending { get; set; }
        public string? SlotNames { get; set; }
        public string? SlotEnumeratorNames { get; set; }
        public string? CoordinateHostName { get; set; }
        public Type? CoordinateHostType { get; set; }
        public string? ParentHostName { get; set; }
        public string? LayoutPropertyName { get; set; } = "Layout";
        public string? ActualOffsetPropertyName { get; set; } = "ActualOffset";
    }

    private static readonly ConditionalWeakTable<Control, LayoutState> States = new();

    /// <summary>
    /// Gets whether workflow slot layout behavior is enabled for the specified control.
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
    /// Sets whether workflow slot layout behavior is enabled for the specified control.
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

        Detach(element);
        state.IsEnabled = value;
        if (value)
        {
            Attach(element);
        }
    }

    /// <summary>
    /// Gets the configured named slot controls.
    /// </summary>
    public static string? GetSlotNames(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).SlotNames;
    }

    /// <summary>
    /// Sets the configured named slot controls.
    /// </summary>
    public static void SetSlotNames(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).SlotNames = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured slot enumerator member names.
    /// </summary>
    public static string? GetSlotEnumeratorNames(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).SlotEnumeratorNames;
    }

    /// <summary>
    /// Sets the configured slot enumerator member names.
    /// </summary>
    public static void SetSlotEnumeratorNames(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).SlotEnumeratorNames = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured coordinate host name.
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
    /// Sets the configured coordinate host name.
    /// </summary>
    public static void SetCoordinateHostName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostName = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured coordinate host type.
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
    /// Sets the configured coordinate host type.
    /// </summary>
    public static void SetCoordinateHostType(Control element, Type? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).CoordinateHostType = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured parent host name.
    /// </summary>
    public static string? GetParentHostName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ParentHostName;
    }

    /// <summary>
    /// Sets the configured parent host name.
    /// </summary>
    public static void SetParentHostName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ParentHostName = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured layout property name.
    /// </summary>
    public static string? GetLayoutPropertyName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).LayoutPropertyName;
    }

    /// <summary>
    /// Sets the configured layout property name.
    /// </summary>
    public static void SetLayoutPropertyName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).LayoutPropertyName = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Gets the configured actual offset property name.
    /// </summary>
    public static string? GetActualOffsetPropertyName(Control element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return GetState(element).ActualOffsetPropertyName;
    }

    /// <summary>
    /// Sets the configured actual offset property name.
    /// </summary>
    public static void SetActualOffsetPropertyName(Control element, string? value)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        GetState(element).ActualOffsetPropertyName = value;
        ScheduleSync(element);
    }

    /// <summary>
    /// Requests the control to refresh layout-related workflow visuals.
    /// </summary>
    public static void Refresh(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        ScheduleSync(control);
    }

    private static void Attach(Control control)
    {
        var state = GetState(control);
        control.HandleCreated += OnHandleCreated;
        control.VisibleChanged += OnVisibleChanged;
        control.Layout += OnLayout;
        control.SizeChanged += OnSizeChanged;
        control.ControlAdded += OnControlCollectionChanged;
        control.ControlRemoved += OnControlCollectionChanged;
        control.Disposed += OnDisposed;
        UpdatePropertyChangedSubscription(control, state);
        ScheduleSync(control);
    }

    private static void Detach(Control control)
    {
        if (!States.TryGetValue(control, out var state))
        {
            return;
        }

        control.HandleCreated -= OnHandleCreated;
        control.VisibleChanged -= OnVisibleChanged;
        control.Layout -= OnLayout;
        control.SizeChanged -= OnSizeChanged;
        control.ControlAdded -= OnControlCollectionChanged;
        control.ControlRemoved -= OnControlCollectionChanged;
        control.Disposed -= OnDisposed;

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        state.SyncPending = false;
    }

    private static void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnVisibleChanged(object? sender, EventArgs e)
    {
        if (sender is Control control && control.Visible)
        {
            ScheduleSync(control);
        }
    }

    private static void OnLayout(object? sender, LayoutEventArgs e)
    {
        if (sender is Control control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnControlCollectionChanged(object? sender, ControlEventArgs e)
    {
        if (sender is Control control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            Detach(control);
        }
    }

    private static void OnNodePropertyChanged(Control control, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IWorkflowNodeViewModel.Anchor)
            and not nameof(IWorkflowNodeViewModel.Size)
            and not "InputSlot"
            and not "OutputSlot"
            and not "OutputSlots")
        {
            return;
        }

        ScheduleSync(control);
    }

    private static void UpdatePropertyChangedSubscription(Control control, LayoutState state)
    {
        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        var dataSource = ResolveDataSource(control);
        if (dataSource is INotifyPropertyChanged notify)
        {
            PropertyChangedEventHandler handler = (_, e) => OnNodePropertyChanged(control, e);
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = handler;
            notify.PropertyChanged += handler;
        }
    }

    private static void ScheduleSync(Control control)
    {
        if (!States.TryGetValue(control, out var state) || !state.IsEnabled || state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;
        Action sync = () =>
        {
            if (!States.TryGetValue(control, out var currentState) || control.IsDisposed)
            {
                return;
            }

            currentState.SyncPending = false;
            Sync(control);
        };

        if (control.IsHandleCreated)
        {
            control.BeginInvoke(sync);
            return;
        }

        state.SyncPending = false;
    }

    private static void Sync(Control control)
    {
        var node = ResolveNode(control);
        if (node is null)
        {
            return;
        }

        var parentHost = ResolveParentHost(control);
        var coordinateHost = ResolveCoordinateHost(control, parentHost);

        var slotNames = GetAllSlotNames(control);
        var enumeratorNames = GetAllSlotEnumeratorNames(control);
        if (slotNames.Length == 0 && enumeratorNames.Length == 0)
        {
            foreach (var slotView in EnumerateDescendants(control).Where(x => ResolveSlot(x) is not null))
            {
                SyncSlot(control, coordinateHost, slotView, node);
            }

            return;
        }

        foreach (var slotName in slotNames)
        {
            SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);
        }

        foreach (var enumeratorName in enumeratorNames)
        {
            SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
        }
    }

    private static void SyncNamedSlot(Control parentHost, Control host, Control? coordinateHost, IWorkflowNodeViewModel node, string slotName)
    {
        var slotControl = FindControlByName(parentHost, slotName);
        if (slotControl is not null)
        {
            SyncSlot(host, coordinateHost, slotControl, node);
        }
    }

    private static void SyncSlotEnumerator(Control parentHost, Control host, Control? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        var itemsHost = FindControlByName(parentHost, enumeratorName);
        if (itemsHost is null)
        {
            return;
        }

        foreach (var slotView in EnumerateDescendants(itemsHost).Where(x => ResolveSlot(x) is not null))
        {
            SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlot(Control host, Control? coordinateHost, Control slotControl, IWorkflowNodeViewModel node)
    {
        var slot = ResolveSlot(slotControl);
        if (slot is null || slotControl.Width <= 0 || slotControl.Height <= 0)
        {
            return;
        }

        var center = new System.Drawing.Point(slotControl.Width / 2, slotControl.Height / 2);
        var screenPoint = slotControl.PointToScreen(center);

        if (coordinateHost is not null)
        {
            var coordinatePoint = coordinateHost.PointToClient(screenPoint);
            var actualOffset = GetActualOffset(host, node.Parent);
            slot.Anchor = new Anchor(
                coordinatePoint.X - actualOffset.Horizontal,
                coordinatePoint.Y - actualOffset.Vertical,
                slot.Anchor.Layer);
            return;
        }

        var localPoint = host.PointToClient(screenPoint);
        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + localPoint.X,
            node.Anchor.Vertical + localPoint.Y,
            slot.Anchor.Layer);
    }

    private static Control? ResolveCoordinateHost(Control control, Control parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = FindControlByName(parentHost, hostName);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Panel);
        var current = parentHost;
        while (current is not null)
        {
            if (hostType.IsAssignableFrom(current.GetType()))
            {
                return current;
            }

            current = current.Parent;
        }

        return parentHost;
    }

    private static Control ResolveParentHost(Control control)
    {
        var hostName = GetParentHostName(control);
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return control;
        }

        return FindControlByName(control, hostName) ?? control;
    }

    private static string[] GetAllSlotNames(Control control)
        => EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] GetAllSlotEnumeratorNames(Control control)
        => EnumerateConfiguredNames(GetSlotEnumeratorNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> EnumerateConfiguredNames(string? names)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return Enumerable.Empty<string>();
        }

        return names.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
    }

    private static Control? FindControlByName(Control root, string name)
    {
        foreach (var control in EnumerateSelfAndDescendants(root))
        {
            if (string.Equals(control.Name, name, StringComparison.Ordinal))
            {
                return control;
            }
        }

        return null;
    }

    private static IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
    {
        yield return root;
        foreach (var descendant in EnumerateDescendants(root))
        {
            yield return descendant;
        }
    }

    private static IEnumerable<Control> EnumerateDescendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static object? ResolveDataSource(Control control)
    {
        if (control.DataBindings.Count > 0)
        {
            foreach (Binding binding in control.DataBindings)
            {
                if (binding.DataSource is not null)
                {
                    return binding.DataSource;
                }
            }
        }

        return ResolveValue(control, "ViewModel") ?? ResolveValue(control, "DataContext") ?? ResolveValue(control, "BindingContext") ?? control.Tag;
    }

    private static IWorkflowNodeViewModel? ResolveNode(Control control)
    {
        var current = control;
        while (current is not null)
        {
            var node = ResolveValue(current, "ViewModel") as IWorkflowNodeViewModel
                ?? ResolveValue(current, "DataContext") as IWorkflowNodeViewModel
                ?? ResolveValue(current, "BindingContext") as IWorkflowNodeViewModel
                ?? current.Tag as IWorkflowNodeViewModel;

            if (node is not null)
            {
                return node;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IWorkflowSlotViewModel? ResolveSlot(Control control)
    {
        return control.Tag as IWorkflowSlotViewModel
            ?? ResolveValue(control, "ViewModel") as IWorkflowSlotViewModel
            ?? ResolveValue(control, "DataContext") as IWorkflowSlotViewModel
            ?? ResolveValue(control, "BindingContext") as IWorkflowSlotViewModel;
    }

    private static object? ResolveValue(Control control, string propertyName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = control.GetType().GetProperty(propertyName, flags);
        if (property?.CanRead != true || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetValue(control);
    }

    private static Offset GetActualOffset(Control control, IWorkflowTreeViewModel? tree)
    {
        if (tree is null)
        {
            return new Offset();
        }

        var layoutPropertyName = GetLayoutPropertyName(control);
        if (string.IsNullOrWhiteSpace(layoutPropertyName))
        {
            return new Offset();
        }

        var layoutProperty = tree.GetType().GetProperty(layoutPropertyName, BindingFlags.Instance | BindingFlags.Public);
        var layout = layoutProperty?.GetValue(tree);
        if (layout is null)
        {
            return new Offset();
        }

        var actualOffsetPropertyName = GetActualOffsetPropertyName(control);
        if (string.IsNullOrWhiteSpace(actualOffsetPropertyName))
        {
            return new Offset();
        }

        var actualOffsetProperty = layout.GetType().GetProperty(actualOffsetPropertyName, BindingFlags.Instance | BindingFlags.Public);
        return actualOffsetProperty?.GetValue(layout) is Offset offset ? offset : new Offset();
    }

    private static LayoutState GetState(Control element)
        => States.GetValue(element, static _ => new LayoutState());
}
