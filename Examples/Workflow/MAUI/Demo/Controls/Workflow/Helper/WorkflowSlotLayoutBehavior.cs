using Demo.ViewModels;
using Demo.Workflow;
using System.ComponentModel;
using System.Reflection;
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class WorkflowSlotLayoutBehavior
{
    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public bool SyncPending { get; set; }
    }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotLayoutBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static readonly BindableProperty SlotNamesProperty = BindableProperty.CreateAttached(
        "SlotNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static readonly BindableProperty SlotEnumeratorNamesProperty = BindableProperty.CreateAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static readonly BindableProperty CoordinateHostNameProperty = BindableProperty.CreateAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static readonly BindableProperty CoordinateHostTypeProperty = BindableProperty.CreateAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static readonly BindableProperty ParentHostNameProperty = BindableProperty.CreateAttached(
        "ParentHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static readonly BindableProperty LayoutPropertyNameProperty = BindableProperty.CreateAttached(
        "LayoutPropertyName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        "Layout");

    public static readonly BindableProperty ActualOffsetPropertyNameProperty = BindableProperty.CreateAttached(
        "ActualOffsetPropertyName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        "ActualOffset");

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(LayoutState),
        typeof(WorkflowSlotLayoutBehavior),
        null);

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static string? GetSlotNames(BindableObject element) => (string?)element.GetValue(SlotNamesProperty);
    public static void SetSlotNames(BindableObject element, string? value) => element.SetValue(SlotNamesProperty, value);
    public static string? GetSlotEnumeratorNames(BindableObject element) => (string?)element.GetValue(SlotEnumeratorNamesProperty);
    public static void SetSlotEnumeratorNames(BindableObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);
    public static string? GetCoordinateHostName(BindableObject element) => (string?)element.GetValue(CoordinateHostNameProperty);
    public static void SetCoordinateHostName(BindableObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);
    public static Type? GetCoordinateHostType(BindableObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);
    public static void SetCoordinateHostType(BindableObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);
    public static string? GetParentHostName(BindableObject element) => (string?)element.GetValue(ParentHostNameProperty);
    public static void SetParentHostName(BindableObject element, string? value) => element.SetValue(ParentHostNameProperty, value);
    public static string? GetLayoutPropertyName(BindableObject element) => (string?)element.GetValue(LayoutPropertyNameProperty);
    public static void SetLayoutPropertyName(BindableObject element, string? value) => element.SetValue(LayoutPropertyNameProperty, value);
    public static string? GetActualOffsetPropertyName(BindableObject element) => (string?)element.GetValue(ActualOffsetPropertyNameProperty);
    public static void SetActualOffsetPropertyName(BindableObject element, string? value) => element.SetValue(ActualOffsetPropertyNameProperty, value);

    public static void Refresh(ContentView control) => ScheduleSync(control);

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control)
        {
            return;
        }

        if (newValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(ContentView control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.BindingContextChanged += OnBindingContextChanged;
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(ContentView control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.BindingContextChanged -= OnBindingContextChanged;
        control.SizeChanged -= OnSizeChanged;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnUnloaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is LayoutState state)
        {
            state.SyncPending = false;
        }
    }

    private static void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BindableObject bindable)
        {
            return;
        }

        var control = FindAncestor(bindable);
        if (control is null)
        {
            return;
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or "InputSlot"
            or "OutputSlot"
            or "OutputSlots")
        {
            ScheduleSync(control);
        }
    }

    private static ContentView? FindAncestor(BindableObject bindable)
    {
        Element? current = bindable as Element;
        while (current is not null)
        {
            if (current is ContentView view && GetIsEnabled(view))
            {
                return view;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void UpdatePropertyChangedSubscription(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
            state.PropertyChangedSource = null;
        }

        if (control.BindingContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            notify.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private static void ScheduleSync(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (control.GetValue(StateProperty) is not LayoutState currentState)
            {
                return;
            }

            currentState.SyncPending = false;
            Sync(control);
        });
    }

    private static void Sync(ContentView control)
    {
        if (control.BindingContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        var parentHost = ResolveParentHost(control);
        var coordinateHost = ResolveCoordinateHost(control, parentHost);

        foreach (var slotName in GetAllSlotNames(control))
        {
            SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);
        }

        foreach (var enumeratorName in GetAllSlotEnumeratorNames(control))
        {
            SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
        }
    }

    private static void SyncNamedSlot(ContentView parentHost, ContentView host, VisualElement? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
        {
            return;
        }

        if (parentHost.FindByName<VisualElement>(controlName) is VisualElement slotControl)
        {
            SyncSlot(host, coordinateHost, slotControl, node);
        }
    }

    private static void SyncSlotEnumerator(ContentView parentHost, ContentView host, VisualElement? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        if (parentHost.FindByName<Layout>(enumeratorName) is not Layout itemsLayout)
        {
            return;
        }

        foreach (var slotView in FindDescendants<SlotView>(itemsLayout))
        {
            SyncSlot(host, coordinateHost, slotView, node);
        }
    }

    private static void SyncSlot(ContentView host, VisualElement? coordinateHost, VisualElement control, IWorkflowNodeViewModel node)
    {
        if (control.BindingContext is not IWorkflowSlotViewModel slot || control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        if (coordinateHost is not null)
        {
            var centerOnCanvas = GetCenterRelativeTo(control, coordinateHost);
            if (centerOnCanvas is null)
            {
                return;
            }

            slot.Anchor = new Anchor(
                centerOnCanvas.Value.X,
                centerOnCanvas.Value.Y,
                slot.Anchor.Layer);
            return;
        }

        var center = GetCenterRelativeTo(control, host);
        if (center is null)
        {
            return;
        }

        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.Value.X,
            node.Anchor.Vertical + center.Value.Y,
            slot.Anchor.Layer);
    }

    private static VisualElement? ResolveCoordinateHost(ContentView control, ContentView parentHost)
    {
        var hostName = GetCoordinateHostName(control);
        var hostType = GetCoordinateHostType(control) ?? typeof(AbsoluteLayout);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(parentHost, hostName);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        return EnumerateSelfAndAncestors(parentHost)
            .OfType<VisualElement>()
            .FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static ContentView ResolveParentHost(ContentView control)
    {
        var hostName = GetParentHostName(control);
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return control;
        }

        var namedHost = ResolveNamedHost(control, hostName);
        return namedHost as ContentView ?? control;
    }

    private static VisualElement? ResolveNamedHost(Element control, string hostName)
    {
        foreach (var current in EnumerateSelfAndAncestors(control))
        {
            if (current is VisualElement visual)
            {
                var named = visual.FindByName<VisualElement>(hostName);
                if (named is not null)
                {
                    return named;
                }
            }
        }

        return null;
    }

    private static string[] GetAllSlotNames(ContentView control)
        => EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] GetAllSlotEnumeratorNames(ContentView control)
        => EnumerateConfiguredNames(GetSlotEnumeratorNames(control)).Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> EnumerateConfiguredNames(string? names)
        => string.IsNullOrWhiteSpace(names)
            ? Enumerable.Empty<string>()
            : names.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static Offset GetActualOffset(ContentView control, IWorkflowTreeViewModel? tree)
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

        var property = tree.GetType().GetProperty(layoutPropertyName, BindingFlags.Public | BindingFlags.Instance);
        var layout = property?.GetValue(tree);
        if (layout is null)
        {
            return new Offset();
        }

        var actualOffsetPropertyName = GetActualOffsetPropertyName(control);
        if (string.IsNullOrWhiteSpace(actualOffsetPropertyName))
        {
            return new Offset();
        }

        var actualOffsetProperty = layout.GetType().GetProperty(actualOffsetPropertyName, BindingFlags.Public | BindingFlags.Instance);
        return actualOffsetProperty?.GetValue(layout) is Offset offset ? offset : new Offset();
    }

    private static Point? GetCenterRelativeTo(VisualElement element, VisualElement relativeTo)
    {
        var screenCenter = GetLocationOnScreen(element);
        var relativeOrigin = GetLocationOnScreen(relativeTo);
        if (screenCenter is null || relativeOrigin is null)
        {
            return null;
        }

        var center = screenCenter.Value;
        var origin = relativeOrigin.Value;

        return new Point(
            center.X - origin.X + (element.Width / 2),
            center.Y - origin.Y + (element.Height / 2));
    }

    private static Point? GetLocationOnScreen(VisualElement element)
    {
        double x = element.X;
        double y = element.Y;
        Element? current = element.Parent;
        while (current is VisualElement visual)
        {
            x += visual.X;
            y += visual.Y;
            current = visual.Parent;
        }

        return new Point(x, y);
    }

    private static IEnumerable<Element> EnumerateSelfAndAncestors(Element source)
    {
        for (Element? current = source; current is not null; current = current.Parent)
        {
            yield return current;
        }
    }

    private static IEnumerable<T> FindDescendants<T>(Element parent) where T : Element
    {
        foreach (var child in parent.LogicalChildren)
        {
            if (child is T result)
            {
                yield return result;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
