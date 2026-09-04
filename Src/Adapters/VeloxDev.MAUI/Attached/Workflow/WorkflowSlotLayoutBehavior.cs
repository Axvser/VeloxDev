using System.Collections.Generic;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotLayoutBehavior
{
    /// <summary>≈1 frame; lets a native layout pass land between ticks.</summary>
    private const int SettleIntervalMs = 16;

    /// <summary>First-tick latency for post-arrange triggers (SizeChanged). The node
    /// frame is already final when this is armed, so a sub-frame wait just lets the
    /// current UI callback (the arrange pass that follows SizeChanged) finish before
    /// the tick — enough to land the measurement in the same rendered frame instead
    /// of waiting out the full 16ms settle cadence.</summary>
    private const int PostArrangeIntervalMs = 4;

    /// <summary>Upper bound on consecutive re-checks while geometry keeps moving,
    /// so a pathological infinite cascade cannot spin forever. Every fresh trigger
    /// (property change / real resize) resets the budget, so real zoom cascades
    /// never hit the cap.</summary>
    private const int MaxSettleAttempts = 16;

    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }

        /// <summary>True while a settle chain is armed (coalesces burst triggers).</summary>
        public bool Settling { get; set; }

        /// <summary>Consecutive re-checks in the current settle chain.</summary>
        public int SettleAttempts { get; set; }

        public HashSet<string> SlotPropertyNames { get; } = [];

        /// <summary>
        /// Coalescing single-shot timer that drives the slot-measure settle chain.
        /// Replaces nested BeginInvoke which had fragile ordering dependencies on
        /// the dispatcher queue across different MAUI platforms.
        /// </summary>
        public IDispatcherTimer? SyncTimer { get; set; }
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

        // SizeChanged fires from MAUI's native arrange only when the view's
        // allocated size actually changes — i.e. the exact moment ViewManager's
        // SetLayoutBounds collapse takes effect after a real layout pass. Unlike
        // the node VM property change (raised pre-layout), this is the post-arrange
        // signal, so re-arming the settle chain here catches collapses whose native
        // layout landed after the property-change tick had already measured stale
        // geometry — the slot-drift source during zoom.
        control.SizeChanged += OnNodeResized;

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(ContentView control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.BindingContextChanged -= OnBindingContextChanged;
        control.SizeChanged -= OnNodeResized;

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            if (state.SyncTimer is not null)
            {
                state.SyncTimer.Stop();
                state.SyncTimer = null;
            }

            if (state.PropertyChangedSource is not null
                && state.PropertyChangedHandler is not null)
            {
                state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            }
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

    private static void OnNodeResized(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            // Post-native-arrange trigger: refresh the settle budget (and start a
            // chain if none is running) so the measurement chases the collapse that
            // actually landed, rather than the pre-layout property change.
            ScheduleSync(control, postArrange: true);
        }
    }

    private static void OnUnloaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is LayoutState state)
        {
            state.Settling = false;
            if (state.SyncTimer is not null)
            {
                state.SyncTimer.Stop();
                state.SyncTimer = null;
            }
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

    private static void UpdatePropertyChangedSubscription(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.PropertyChangedSource is not null && state.PropertyChangedHandler is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        if (control.BindingContext is INotifyPropertyChanged notify)
        {
            // Use a lambda that directly captures the ContentView. The node VM is a plain
            // INotifyPropertyChanged (not a BindableObject / Element), so we cannot walk
            // the visual tree from the sender to find the associated view.
            PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName is not null && state.SlotPropertyNames.Contains(e.PropertyName))
                {
                    ScheduleSync(control);
                }
            };
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = handler;
            notify.PropertyChanged += handler;
        }
    }

    private static void ScheduleSync(ContentView control, bool postArrange = false)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        // Every trigger (node VM property change, real resize via SizeChanged,
        // drag) refreshes the settle budget so an in-flight chain keeps chasing
        // the layout instead of giving up mid-cascade.
        state.Settling = true;
        state.SettleAttempts = 0;

        if (state.SyncTimer is not null)
        {
            return; // one coalescing timer already running; the chain observes the reset budget
        }

        // Use an IDispatcherTimer (≈1-frame cadence) instead of nested BeginInvoke.
        // MAUI lacks WPF's DispatcherPriority.Render, so the original two-level
        // dispatch was a fragile ordering hack that depended on queue ordering.
        // A per-control coalescing timer:
        //   • Naturally waits for the layout pass between ticks
        //   • Eliminates race with ViewManager.ApplyLayout (SetLayoutBounds)
        //   • Works consistently across Android/iOS/Windows
        //   • Multiple rapid requests coalesce into a single settle chain
        // The closure is short-lived (single-shot timer), so allocation impact is
        // negligible compared to the performance gain from eliminating the race.
        // postArrange triggers (node actually resized) can measure sooner because the
        // layout has already run; plain property changes must wait a full frame for it.
        var timer = control.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(postArrange ? PostArrangeIntervalMs : SettleIntervalMs);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => TickSync(control, timer);
        state.SyncTimer = timer;
        timer.Start();
    }

    /// <summary>One settle tick: measure the slots, then re-arm for the next frame
    /// while a pass actually changed geometry. MAUI's layout is platform-async and
    /// can cascade over several passes (collapse → ApplyScale child reflow), so a
    /// single fixed-delay measurement can capture a mid-cascade frame and then never
    /// refresh again — the anchors stay stale until the next unrelated trigger (the
    /// observed multi-frame slot drift during zoom). Re-checking until the geometry
    /// stops moving guarantees the anchors converge to the final post-zoom layout
    /// within a frame or two of the last pass instead of lingering on a stale write.</summary>
    private static void TickSync(ContentView control, IDispatcherTimer timer)
    {
        try
        {
            timer.Stop();

            if (control.GetValue(StateProperty) is not LayoutState state)
            {
                return;
            }

            var modified = Sync(control);

            if (modified > 0 && state.Settling && state.SettleAttempts < MaxSettleAttempts)
            {
                // Layout is still cascading (anchors moved since the last write).
                // Re-check next frame so the measurement converges to the final geometry.
                state.SettleAttempts++;
                state.SyncTimer = timer;
                timer.Start();
                return;
            }

            state.Settling = false;
            state.SyncTimer = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // IDispatcherTimer.Tick exceptions are NOT caught by MAUI
            // and bubble unhandled to WinUI's UnhandledException event.
            // This is a known MAUI/WinUI gap (dotnet/maui #12245, #10341).
            System.Diagnostics.Debug.WriteLine(
                $"[WorkflowSlotLayout] Sync error: {ex.Message}");

            if (control.GetValue(StateProperty) is LayoutState state)
            {
                state.Settling = false;
                state.SyncTimer = null;
            }
        }
    }

    /// <returns>The number of slot anchors this pass actually changed (0 = converged).</returns>
    private static int Sync(ContentView control)
    {
        if (control.BindingContext is not IWorkflowNodeViewModel node)
        {
            return 0;
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

        var modified = 0;
        foreach (var slotName in slotNames)
        {
            if (SyncNamedSlot(parentHost, control, coordinateHost, node, slotName))
            {
                modified++;
            }
        }

        foreach (var enumeratorName in enumeratorNames)
        {
            if (SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName))
            {
                modified++;
            }
        }

        return modified;
    }

    /// <returns>True when the slot anchor was actually updated.</returns>
    private static bool SyncNamedSlot(ContentView parentHost, ContentView host, VisualElement? coordinateHost, IWorkflowNodeViewModel node, string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
        {
            return false;
        }

        if (parentHost.FindByName<VisualElement>(controlName) is VisualElement slotControl)
        {
            return SyncSlot(host, coordinateHost, slotControl, node);
        }

        return false;
    }

    /// <returns>True when at least one slot anchor was actually updated.</returns>
    private static bool SyncSlotEnumerator(ContentView parentHost, ContentView host, VisualElement? coordinateHost, IWorkflowNodeViewModel node, string enumeratorName)
    {
        if (parentHost.FindByName<Layout>(enumeratorName) is not Layout itemsLayout)
        {
            return false;
        }

        var modified = false;
        foreach (var slotView in FindDescendants<VisualElement>(itemsLayout).Where(static x => x.BindingContext is IWorkflowSlotViewModel))
        {
            if (SyncSlot(host, coordinateHost, slotView, node))
            {
                modified = true;
            }
        }

        return modified;
    }

    /// <returns>True when the slot anchor was actually updated.</returns>
    private static bool SyncSlot(ContentView host, VisualElement? coordinateHost, VisualElement control, IWorkflowNodeViewModel node)
    {
        if (control.BindingContext is not IWorkflowSlotViewModel slot || control.Width <= 0 || control.Height <= 0)
        {
            return false;
        }

        Anchor newAnchor;
        if (coordinateHost is not null)
        {
            var centerOnCanvas = GetCenterRelativeTo(control, coordinateHost);
            if (centerOnCanvas is null)
            {
                return false;
            }

            // MAUI measures the center relative to the canvas (coordinate host) by summing
            // layout positions, which excludes the canvas TranslationX render transform — so
            // the result is already canvas/world space and needs no ActualOffset subtraction.
            // (Core's SlotAnchorFromVisualCenter is for adapters that measure in screen space.)
            newAnchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(
                centerOnCanvas.Value.X, centerOnCanvas.Value.Y, slot.Anchor.Layer);
        }
        else
        {
            var center = GetCenterRelativeTo(control, host);
            if (center is null)
            {
                return false;
            }

            // SlotAnchorFromNode: anchor = nodeAnchor + local offset (no coordinate host).
            newAnchor = WorkflowSurfaceMath.SlotAnchorFromNode(
                node.Anchor.Horizontal, node.Anchor.Vertical,
                center.Value.X, center.Value.Y, slot.Anchor.Layer);
        }

        // Dirty check: skip if the anchor value hasn't changed.
        // This prevents the infinite cycle:
        //   SyncSlot → slot.Anchor setter → PropertyChanged → ApplyLayout
        //   → MAUI layout → SizeChanged/X/Y → ScheduleSync → SyncSlot ...
        if (slot.Anchor.Horizontal == newAnchor.Horizontal && slot.Anchor.Vertical == newAnchor.Vertical)
        {
            return false;
        }

        slot.Anchor = newAnchor;
        return true;
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

    private static VisualElement? ResolveNamedHost(Element control, string? hostName)
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
        double x = GetLeftInParent(element);
        double y = GetTopInParent(element);
        Element? current = element.Parent;
        while (current is VisualElement visual)
        {
            x += GetLeftInParent(visual);
            y += GetTopInParent(visual);
            current = visual.Parent;
        }

        return new Point(x, y);
    }

    private static double GetLeftInParent(VisualElement element)
    {
        if (element.Parent is AbsoluteLayout)
        {
            var bounds = AbsoluteLayout.GetLayoutBounds(element);
            if (!double.IsNaN(bounds.X))
            {
                return bounds.X;
            }
        }

        return element.X;
    }

    private static double GetTopInParent(VisualElement element)
    {
        if (element.Parent is AbsoluteLayout)
        {
            var bounds = AbsoluteLayout.GetLayoutBounds(element);
            if (!double.IsNaN(bounds.Y))
            {
                return bounds.Y;
            }
        }

        return element.Y;
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
        foreach (var child in ((Microsoft.Maui.IVisualTreeElement)parent).GetVisualChildren().OfType<Element>())
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
