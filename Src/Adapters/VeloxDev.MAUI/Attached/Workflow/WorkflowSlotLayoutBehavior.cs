using System.Collections.Generic;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotLayoutBehavior
{
    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public bool SyncPending { get; set; }
        public HashSet<string> SlotPropertyNames { get; } = [];

        /// <summary>
        /// Coalescing timer that fires once per frame after layout settles.
        /// Replaces nested BeginInvoke which had fragile ordering dependencies
        /// on the dispatcher queue across different MAUI platforms.
        /// </summary>
        public IDispatcherTimer? SyncTimer { get; set; }

        /// <summary>
        /// WinUI-native LayoutUpdated hook (Windows). Object-typed so LayoutState compiles on
        /// every TFM; only touched inside #if WINDOWS blocks. LayoutUpdated is the end-of-layout
        /// pass signal the other workflow families sync on; MAUI's managed SizeChanged fires
        /// mid-arrange and reads a transient frame.
        /// </summary>
        public object? NativeLayoutElement { get; set; }
        public EventHandler<object>? NativeLayoutUpdatedHandler { get; set; }
        public bool ResizeFallbackActive { get; set; }
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

#if WINDOWS
        // Sync on the node's WinUI-native LayoutUpdated — the true end-of-layout-pass signal
        // (the one WinUI/WPF families sync on). MAUI's managed SizeChanged fires DURING the
        // arrange, before the node's inner slot layout settles, so a zoom collapse measured
        // there reads a transient overshoot that the links (bound to those anchors) paint for
        // a frame — the residual per-notch endpoint pop. LayoutUpdated runs after the whole
        // subtree is arranged, giving final geometry in the same frame the node lands. The
        // platform element is created when the handler attaches, so hook on HandlerChanged too.
        control.HandlerChanged += OnNodeHandlerChanged;
        TryInstallResizeSignal(control);
#else
        // Non-Windows MAUI: no framework LayoutUpdated exposed; keep the managed SizeChanged
        // resize signal used before. (Windows no longer subscribes: it mis-measures in-arrange.)
        control.SizeChanged += OnNodeResized;
#endif

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(ContentView control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.BindingContextChanged -= OnBindingContextChanged;
#if WINDOWS
        control.HandlerChanged -= OnNodeHandlerChanged;
        UnhookNativeLayoutUpdated(control);
#else
        control.SizeChanged -= OnNodeResized;
#endif

        if (control.GetValue(StateProperty) is LayoutState state)
        {
#if WINDOWS
            // The managed SizeChanged was only ever a fallback until the native LayoutUpdated
            // hook attached — remove it if a detach happens while it is still the active signal.
            if (state.ResizeFallbackActive)
            {
                control.SizeChanged -= OnNodeResized;
                state.ResizeFallbackActive = false;
            }
#endif
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

#if WINDOWS
    private static void OnNodeHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            TryInstallResizeSignal(control);
        }
    }

    /// <summary>
    /// Prefers the node's WinUI-native LayoutUpdated as the resize signal; falls back to the
    /// managed SizeChanged until (and unless) the native element is available. LayoutUpdated is
    /// the end-of-layout-pass event the WinUI/WPF families sync on: the node subtree is fully
    /// arranged, so slot centers read FINAL geometry. MAUI's managed SizeChanged fires DURING the
    /// arrange — a zoom collapse measured there reads a transient overshoot that the links bound
    /// to those anchors paint for a frame (the residual per-notch endpoint pop). The fallback is
    /// kept only so Windows can never silently lose resize sync if the platform element isn't a
    /// FrameworkElement.
    /// </summary>
    private static void TryInstallResizeSignal(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.NativeLayoutUpdatedHandler is null
            && control.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native)
        {
            state.NativeLayoutElement = native;
            state.NativeLayoutUpdatedHandler = (_, _) =>
            {
                // Sync synchronously inside LayoutUpdated: layout has completed for this subtree,
                // so slot centers read final. The dirty check in SyncSlot makes repeated passes a
                // no-op once the geometry settles — no re-arm chain, nothing to wedge.
                if (control.GetValue(StateProperty) is not null)
                {
                    Sync(control);
                }
            };
            native.LayoutUpdated += state.NativeLayoutUpdatedHandler;

            // Post-pass signal installed — drop the managed fallback so it can't write its
            // mid-arrange measurement anymore.
            if (state.ResizeFallbackActive)
            {
                control.SizeChanged -= OnNodeResized;
                state.ResizeFallbackActive = false;
            }
            return;
        }

        // Platform element not materialized yet (HandlerChanged fires as the handler attaches)
        // or not a FrameworkElement — keep the managed SizeChanged fallback in the meantime.
        if (!state.ResizeFallbackActive && state.NativeLayoutUpdatedHandler is null)
        {
            control.SizeChanged += OnNodeResized;
            state.ResizeFallbackActive = true;
        }
    }

    private static void UnhookNativeLayoutUpdated(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.NativeLayoutElement is Microsoft.UI.Xaml.FrameworkElement native
            && state.NativeLayoutUpdatedHandler is not null)
        {
            native.LayoutUpdated -= state.NativeLayoutUpdatedHandler;
        }

        state.NativeLayoutElement = null;
        state.NativeLayoutUpdatedHandler = null;
    }
#endif

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
            if (state.SyncTimer is not null)
            {
                state.SyncTimer.Stop();
            }
        }
    }

    private static void OnNodeResized(object? sender, EventArgs e)
    {
        // Non-Windows fallback only. On Windows this hook is NOT used: MAUI's managed
        // SizeChanged fires DURING the arrange, before the node's inner slots settle, so
        // the measured anchors overshoot the final geometry for ~10ms (see git history:
        // the per-notch endpoint pop). Windows syncs on the platform LayoutUpdated instead.
        if (sender is ContentView control)
        {
            Sync(control);
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

    private static void ScheduleSync(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;

        // Use an IDispatcherTimer (≈1-frame delay) instead of nested BeginInvoke.
        // MAUI lacks WPF's DispatcherPriority.Render, so the original two-level
        // dispatch was a fragile ordering hack that depended on queue ordering.
        // A per-control coalescing timer:
        //   • Naturally waits for the layout pass between ticks
        //   • Eliminates race with ViewManager.ApplyLayout (SetLayoutBounds)
        //   • Works consistently across Android/iOS/Windows
        //   • Multiple rapid requests coalesce into a single Sync call
        // The closure is short-lived (single-shot timer), so allocation impact is
        // negligible compared to the performance gain from eliminating the race.
        var timer = control.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);  // ≈1 frame
        timer.IsRepeating = false;
        timer.Tick += (s, e) =>
        {
            try
            {
                timer.Stop();
                if (control.GetValue(StateProperty) is not LayoutState currentState)
                    return;

                currentState.SyncPending = false;
                currentState.SyncTimer = null;
                Sync(control);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // IDispatcherTimer.Tick exceptions are NOT caught by MAUI
                // and bubble unhandled to WinUI's UnhandledException event.
                // This is a known MAUI/WinUI gap (dotnet/maui #12245, #10341).
                System.Diagnostics.Debug.WriteLine(
                    $"[WorkflowSlotLayout] Sync error: {ex.Message}");
            }
        };
        timer.Start();
        state.SyncTimer = timer;
    }

    private static void Sync(ContentView control)
    {
        if (control.BindingContext is not IWorkflowNodeViewModel node)
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

        foreach (var slotView in FindDescendants<VisualElement>(itemsLayout).Where(static x => x.BindingContext is IWorkflowSlotViewModel))
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

        Anchor newAnchor;
        if (coordinateHost is not null)
        {
            var centerOnCanvas = GetCenterRelativeTo(control, coordinateHost);
            if (centerOnCanvas is null)
            {
                return;
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
                return;
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
            return;

        slot.Anchor = newAnchor;
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
