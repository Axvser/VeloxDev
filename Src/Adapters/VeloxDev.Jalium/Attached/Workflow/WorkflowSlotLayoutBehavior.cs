using System.Collections.Generic;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Threading;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Attached to a node view: measures each slot control center relative to a coordinate
/// host, subtracts the tree's Layout.ActualOffset, and writes it back to slot.Anchor (the
/// coordinates links render from). Re-syncs on layout/property changes.</summary>
public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public FrameworkElement? Owner { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public INotifyPropertyChanged? LayoutNotify { get; set; }
        public PropertyChangedEventHandler? LayoutChangedHandler { get; set; }
        public bool SyncPending { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = new();
    }

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty SlotNamesProperty = DependencyProperty.RegisterAttached(
        "SlotNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CoordinateHostTypeProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(LayoutState),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetSlotNames(DependencyObject element) => (string?)element.GetValue(SlotNamesProperty);
    public static void SetSlotNames(DependencyObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    public static string? GetSlotEnumeratorNames(DependencyObject element) => (string?)element.GetValue(SlotEnumeratorNamesProperty);
    public static void SetSlotEnumeratorNames(DependencyObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    public static string? GetCoordinateHostName(DependencyObject element) => (string?)element.GetValue(CoordinateHostNameProperty);
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(DependencyObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);
    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement control)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(FrameworkElement control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState { Owner = control });
        control.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        control.AddHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
        control.DataContextChanged += OnDataContextChanged;
        control.IsVisibleChanged += OnIsVisibleChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(FrameworkElement control)
    {
        control.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        control.RemoveHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
        control.DataContextChanged -= OnDataContextChanged;
        control.IsVisibleChanged -= OnIsVisibleChanged;
        control.LayoutUpdated -= OnLayoutUpdated;

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            if (state.PropertyChangedSource is not null)
            {
                state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            }

            if (state.LayoutNotify is not null)
            {
                state.LayoutNotify.PropertyChanged -= state.LayoutChangedHandler;
            }
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.GetValue(StateProperty) is LayoutState state)
        {
            state.SyncPending = false;
        }
    }

    private static void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement control && Equals(e.NewValue, true))
        {
            ScheduleSync(control);
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnNodePropertyChanged(FrameworkElement control, PropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not LayoutState state
            || (e.PropertyName is not null && !state.SlotPropertyNames.Contains(e.PropertyName)))
        {
            return;
        }

        ScheduleSync(control);
    }

    private static void UpdatePropertyChangedSubscription(FrameworkElement control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        if (control.DataContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = (_, e) => OnNodePropertyChanged(control, e);
            notify.PropertyChanged += state.PropertyChangedHandler;
        }

        // Re-sync slot anchors when the tree's layout offset changes (pan / auto-grow). The node's
        // Anchor stays in world coordinates during panning, so without this the slot.Anchor used by
        // LinkView would go stale and links would detach from the nodes.
        SubscribeToLayout(control, state);
    }

    private static void SubscribeToLayout(FrameworkElement control, LayoutState state)
    {
        if (state.LayoutNotify is not null)
        {
            state.LayoutNotify.PropertyChanged -= state.LayoutChangedHandler;
            state.LayoutNotify = null;
            state.LayoutChangedHandler = null;
        }

        if (control.DataContext is IWorkflowNodeViewModel node
            && node.Parent is { } tree
            && tree.Layout is INotifyPropertyChanged layoutNotify)
        {
            state.LayoutNotify = layoutNotify;
            state.LayoutChangedHandler = (_, e) =>
            {
                if (e.PropertyName is "ActualOffset" or "ActualSize")
                {
                    ScheduleSync(control);
                }
            };
            layoutNotify.PropertyChanged += state.LayoutChangedHandler;
        }
    }

    private static void ScheduleSync(FrameworkElement control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;
        control.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (control.GetValue(StateProperty) is not LayoutState currentState)
            {
                return;
            }

            currentState.SyncPending = false;
            Sync(control);
        }));
    }

    private static void Sync(FrameworkElement control)
    {
        if (control.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        var parentHost = control;
        var coordinateHost = ResolveCoordinateHost(control, parentHost);
        var slotNames = GetAllSlotNames(control);
        var enumeratorNames = GetAllSlotEnumeratorNames(control);

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            state.SlotPropertyNames.Clear();
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Anchor));
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Size));
            foreach (var name in slotNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                {
                    state.SlotPropertyNames.Add(name.Substring(5));
                }
            }
            foreach (var name in enumeratorNames)
            {
                state.SlotPropertyNames.Add(name);
                if (name.StartsWith("PART_"))
                {
                    state.SlotPropertyNames.Add(name.Substring(5));
                }
            }
            state.SlotPropertyNames.Add("InputSlot");
            state.SlotPropertyNames.Add("OutputSlot");
            state.SlotPropertyNames.Add("OutputSlots");
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

    private static void SyncNamedSlot(FrameworkElement parentHost, FrameworkElement host, FrameworkElement? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
        {
            return;
        }

        if (parentHost.FindName(controlName) is FrameworkElement slotControl)
        {
            SyncSlot(host, coordinateHost, slotControl, node);
        }
    }

    private static void SyncSlotEnumerator(FrameworkElement parentHost, FrameworkElement host, FrameworkElement? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        if (parentHost.FindName(enumeratorName) is not ItemsControl itemsControl || itemsControl.Items.Count == 0)
        {
            return;
        }

        for (var i = 0; i < itemsControl.Items.Count; i++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not DependencyObject container)
            {
                continue;
            }

            var slotView = FindDescendantWithSlotDataContext(container);
            if (slotView is not null)
            {
                SyncSlot(host, coordinateHost, slotView, node);
            }
        }
    }

    private static void SyncSlot(FrameworkElement host, FrameworkElement? coordinateHost, FrameworkElement control, IWorkflowNodeViewModel node)
    {
        if (control.DataContext is not IWorkflowSlotViewModel slot || control.ActualWidth <= 0 || control.ActualHeight <= 0)
        {
            return;
        }

        // Authoritative calculation model (Jalium NodeEditorDemo): port center = node.Anchor + the
        // slot's local position within the node view (Canvas.Left/Top + half-size). Pure model math —
        // no TranslatePoint / render-transform / canvas dependence, so it never goes stale on pan.
        var left = Canvas.GetLeft(control);
        var top = Canvas.GetTop(control);
        double localX = (double.IsNaN(left) ? 0 : left) + control.ActualWidth / 2;
        double localY = (double.IsNaN(top) ? 0 : top) + control.ActualHeight / 2;
        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            node.Anchor.Horizontal, node.Anchor.Vertical, localX, localY, slot.Anchor.Layer);
    }

    private static FrameworkElement? ResolveCoordinateHost(FrameworkElement control, FrameworkElement parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(parentHost, hostName);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        if (hostType.IsAssignableFrom(parentHost.GetType()))
        {
            return parentHost;
        }

        return EnumerateVisualAncestors(parentHost)
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static FrameworkElement? ResolveNamedHost(FrameworkElement control, string? hostName)
    {
        if (control.Name == hostName)
        {
            return control;
        }

        return EnumerateVisualAncestors(control)
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    private static string[] GetAllSlotNames(FrameworkElement control)
        => EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] GetAllSlotEnumeratorNames(FrameworkElement control)
        => EnumerateConfiguredNames(GetSlotEnumeratorNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> EnumerateConfiguredNames(string? names)
        => string.IsNullOrWhiteSpace(names)
            ? Enumerable.Empty<string>()
            : names!.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static Offset GetActualOffset(IWorkflowTreeViewModel? tree)
    {
        if (tree is null)
        {
            return new Offset();
        }

        return tree.Layout.ActualOffset;
    }

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { DataContext: IWorkflowSlotViewModel })
            {
                return (FrameworkElement)child;
            }

            var descendant = FindDescendantWithSlotDataContext(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> EnumerateVisualAncestors(DependencyObject source)
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
