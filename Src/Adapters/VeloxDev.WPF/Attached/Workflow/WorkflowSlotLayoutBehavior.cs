using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public UserControl? Owner { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public bool SyncPending { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = [];
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
        if (d is not UserControl control)
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

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState
        {
            Owner = control,
        });
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.IsVisibleChanged += OnIsVisibleChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.IsVisibleChanged -= OnIsVisibleChanged;
        control.LayoutUpdated -= OnLayoutUpdated;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
        {
            state.SyncPending = false;
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && Equals(e.NewValue, true))
        {
            ScheduleSync(control);
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is UserControl control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnNodePropertyChanged(UserControl control, PropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not LayoutState state
            || 
           (e.PropertyName is not null && !state.SlotPropertyNames.Contains(e.PropertyName)))
        {
            return;
        }

        ScheduleSync(control);
    }

    private static void UpdatePropertyChangedSubscription(UserControl control)
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
    }

    private static void ScheduleSync(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;
        control.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (control.GetValue(StateProperty) is not LayoutState currentState)
            {
                return;
            }

            currentState.SyncPending = false;
            Sync(control);
        }), DispatcherPriority.Render);
    }

    private static void Sync(UserControl control)
    {
        if (control.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

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
        {
            SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);
        }

        foreach (var enumeratorName in enumeratorNames)
        {
            SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
        }
    }

    private static void SyncNamedSlot(UserControl parentHost, UserControl host, FrameworkElement? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
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

    private static void SyncSlotEnumerator(UserControl parentHost, UserControl host, FrameworkElement? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
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

    private static void SyncSlot(UserControl host, FrameworkElement? coordinateHost, FrameworkElement control, IWorkflowNodeViewModel node)
    {
        if (control.DataContext is not IWorkflowSlotViewModel slot || control.ActualWidth <= 0 || control.ActualHeight <= 0)
        {
            return;
        }

        if (coordinateHost is not null)
        {
            var centerOnCanvas = control.TranslatePoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2), coordinateHost);
            var actualOffset = GetActualOffset(node.Parent);
            slot.Anchor = new Anchor(
                centerOnCanvas.X - actualOffset.Horizontal,
                centerOnCanvas.Y - actualOffset.Vertical,
                slot.Anchor.Layer);
            return;
        }

        var center = control.TranslatePoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2), host);
        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.X,
            node.Anchor.Vertical + center.Y,
            slot.Anchor.Layer);
    }

    private static FrameworkElement? ResolveCoordinateHost(UserControl control, UserControl parentHost)
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

    private static string[] GetAllSlotNames(UserControl control)
        => EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] GetAllSlotEnumeratorNames(UserControl control)
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
            if (child is FrameworkElement { DataContext: IWorkflowSlotViewModel } element)
            {
                return element;
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
}
