using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotLayoutBehavior : AvaloniaObject
{
    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public bool SyncPending { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = [];
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> SlotNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotNames");

    public static readonly AttachedProperty<string?> SlotEnumeratorNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotEnumeratorNames");

    public static readonly AttachedProperty<string?> CoordinateHostNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("CoordinateHostName");

    public static readonly AttachedProperty<Type?> CoordinateHostTypeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, Type?>("CoordinateHostType");

    private static readonly AttachedProperty<LayoutState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, LayoutState?>("State");

    static WorkflowSlotLayoutBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<UserControl>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetSlotNames(AvaloniaObject element) => element.GetValue(SlotNamesProperty);

    public static void SetSlotNames(AvaloniaObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    public static string? GetSlotEnumeratorNames(AvaloniaObject element) => element.GetValue(SlotEnumeratorNamesProperty);

    public static void SetSlotEnumeratorNames(AvaloniaObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    public static string? GetCoordinateHostName(AvaloniaObject element) => element.GetValue(CoordinateHostNameProperty);

    public static void SetCoordinateHostName(AvaloniaObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(AvaloniaObject element) => element.GetValue(CoordinateHostTypeProperty);

    public static void SetCoordinateHostType(AvaloniaObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        control.DataContextChanged += OnDataContextChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        control.PropertyChanged += OnControlPropertyChanged;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(UserControl control)
    {
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        control.DataContextChanged -= OnDataContextChanged;
        control.LayoutUpdated -= OnLayoutUpdated;
        control.PropertyChanged -= OnControlPropertyChanged;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;

        control.ClearValue(StateProperty);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            ScheduleSync(control);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
            state.SyncPending = false;
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not UserControl control)
            return;

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is UserControl control)
            ScheduleSync(control);
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && e.Property == Visual.BoundsProperty)
            ScheduleSync(control);
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not StyledElement element)
            return;

        if (element.GetValue(StateProperty) is not LayoutState state
            || (e.PropertyName is null || !state.SlotPropertyNames.Contains(e.PropertyName)))
            return;

        var control = element as UserControl;
        if (control is null && element is Visual visual)
            control = visual.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);

        if (control is not null)
            ScheduleSync(control);
    }

    private static void UpdatePropertyChangedSubscription(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
            return;

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
            state.PropertyChangedSource = null;
        }

        if (control.DataContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            notify.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private static void ScheduleSync(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.SyncPending)
            return;

        state.SyncPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (control.GetValue(StateProperty) is not LayoutState currentState)
                return;

            currentState.SyncPending = false;
            Sync(control);
        }, DispatcherPriority.Render);
    }

    private static void Sync(UserControl control)
    {
        if (control.DataContext is not IWorkflowNodeViewModel node)
            return;

        var parentHost = control;
        var coordinateHost = ResolveCoordinateHost(control, parentHost);
        var slotNames = GetAllSlotNames(control);
        var enumeratorNames = GetAllSlotEnumeratorNames(control);

        // Rebuild the set of property names that should trigger ScheduleSync on change.
        if (control.GetValue(StateProperty) is LayoutState state)
        {
            state.SlotPropertyNames.Clear();
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Anchor));
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Size));
            // Control names (e.g. "PART_OutputSlots") differ from ViewModel property
            // names ("OutputSlots"). Add both the full control name and the
            // PART_-stripped form so OnPropertyChanged("OutputSlots") is matched.
            foreach (var name in slotNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                    state.SlotPropertyNames.Add(name.Substring(5));
            }
            foreach (var name in enumeratorNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                    state.SlotPropertyNames.Add(name.Substring(5));
            }
            // Always include fallback defaults for standard property names,
            // covering both direct ViewModel properties and SlotEnumerator members.
            state.SlotPropertyNames.Add("InputSlot");
            state.SlotPropertyNames.Add("OutputSlot");
            state.SlotPropertyNames.Add("OutputSlots");
        }

        foreach (var slotName in slotNames)
            SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);

        foreach (var enumeratorName in enumeratorNames)
            SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
    }

    private static void SyncNamedSlot(UserControl parentHost, UserControl host, Control? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return;

        var slotControl = parentHost.FindControl<Control>(controlName!);
        if (slotControl is not null)
            SyncSlot(host, coordinateHost, slotControl, node);
    }

    private static void SyncSlotEnumerator(UserControl parentHost, UserControl host, Control? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        var itemsControl = parentHost.FindControl<ItemsControl>(enumeratorName);
        if (itemsControl is null || itemsControl.ItemCount == 0)
            return;

        for (int i = 0; i < itemsControl.ItemCount; i++)
        {
            var container = itemsControl.ContainerFromIndex(i);
            var slotView = container?.GetVisualDescendants().OfType<Control>().FirstOrDefault(static x => x.DataContext is IWorkflowSlotViewModel);
            if (slotView is not null)
                SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlot(UserControl host, Control? coordinateHost, Control control, IWorkflowNodeViewModel node)
    {
        if (control.DataContext is not IWorkflowSlotViewModel slot || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return;

        if (coordinateHost is not null)
        {
            var centerOnCanvas = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), coordinateHost);
            if (centerOnCanvas is not null)
            {
                var actualOffset = GetActualOffset(node.Parent);
                slot.Anchor = new Anchor(
                    centerOnCanvas.Value.X - actualOffset.Horizontal,
                    centerOnCanvas.Value.Y - actualOffset.Vertical,
                    slot.Anchor.Layer);
                return;
            }
        }

        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), host);
        if (center is null)
            return;

        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.Value.X,
            node.Anchor.Vertical + center.Value.Y,
            slot.Anchor.Layer);
    }

    private static Control? ResolveCoordinateHost(UserControl control, UserControl parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(parentHost, hostName);
            if (namedHost is not null)
                return namedHost;
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        return parentHost.GetVisualAncestors()
            .OfType<Control>()
            .Prepend(parentHost)
            .FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static Control? ResolveNamedHost(Control control, string? hostName)
    {
        if (hostName is null)
            return null;

        if (control.Name == hostName)
            return control;

        if (control.GetVisualAncestors().OfType<Control>().FirstOrDefault(x => x.Name == hostName) is { } ancestor)
            return ancestor;

        return null;
    }

    private static string[] GetAllSlotNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotNames(control))
            .Distinct(StringComparer.Ordinal)];

    private static string[] GetAllSlotEnumeratorNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotEnumeratorNames(control))
            .Distinct(StringComparer.Ordinal)];

    private static string[] EnumerateConfiguredNames(string? names)
        => string.IsNullOrWhiteSpace(names)
            ? []
            : names!.Split(',')
                .Select(static x => x.Trim())
                .Where(static x => x.Length > 0)
                .ToArray();

    private static Offset GetActualOffset(IWorkflowTreeViewModel? tree)
    {
        if (tree is null)
            return new Offset();

        return tree.Layout.ActualOffset;
    }
}
