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
        // 在节点的 WinUI 原生 LayoutUpdated 上同步 —— 那才是布局趟真正的结束信号（WinUI/WPF 几家都用它）。
        // MAUI 托管 SizeChanged 在 arrange 期间触发，此时节点内部插槽布局尚未稳定，缩放在那里量到的是瞬时过冲，连线会按它多画一帧 —— 每格缩放残留的端点跳动。
        // LayoutUpdated 在整个子树排完后运行，节点落位同帧就有最终几何。平台元素在 handler 挂接时创建，所以也在 HandlerChanged 上挂钩。
        control.HandlerChanged += OnNodeHandlerChanged;
        TryInstallResizeSignal(control);
#else
        // 非 Windows MAUI：框架不暴露 LayoutUpdated，沿用之前的托管 SizeChanged 尺寸信号（Windows 不再订阅：arrange 期间会量错）。
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
            // 托管 SizeChanged 只是原生 LayoutUpdated 挂钩前的兜底；若摘除时它仍是活动信号，就移除它。
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
                // 在 LayoutUpdated 里同步执行：该子树布局已完成，插槽中心是最终值。SyncSlot 的脏检查让几何稳定后的重复趟变成空操作 —— 没有重排链，也不会卡死。
                if (control.GetValue(StateProperty) is not null)
                {
                    Sync(control);
                }
            };
            native.LayoutUpdated += state.NativeLayoutUpdatedHandler;

            // 布局后信号已装好 —— 丢掉托管兜底，免得它再写入 arrange 中途的测量。
            if (state.ResizeFallbackActive)
            {
                control.SizeChanged -= OnNodeResized;
                state.ResizeFallbackActive = false;
            }
            return;
        }

        // 平台元素还没出现……或不是 FrameworkElement —— 暂时保留托管 SizeChanged 兜底。
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
        // 仅非 Windows 兜底。Windows 不用这个钩子：托管 SizeChanged 在 arrange 期间触发，量到的锚点会过冲最终几何约 10ms
        // （见 git 历史：每格端点跳动）；Windows 改用平台 LayoutUpdated 同步。
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
            // 用直接捕获 ContentView 的 lambda：节点 VM 只是普通 INotifyPropertyChanged（不是 BindableObject/Element），无法从 sender 沿可视树找到对应视图。
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

        // 用 IDispatcherTimer（约一帧延迟）取代嵌套 BeginInvoke：MAUI 没有 WPF 的 DispatcherPriority.Render，原来的两级派发是依赖队列顺序的脆弱 hack。
        // 逐控件的合并定时器：在两次 tick 之间自然等布局趟；消除与 ViewManager.ApplyLayout（SetLayoutBounds）的竞争；
        // 跨 Android/iOS/Windows 表现一致；多个快速请求合并成一次 Sync。闭包短命（单次定时器），分配开销可忽略。
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
                // MAUI 不捕获 IDispatcherTimer.Tick 的异常，它会未处理地冒到 WinUI 的 UnhandledException；这是已知的 MAUI/WinUI 缺口（dotnet/maui #12245、#10341）。
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

        // 重建「变化时该触发 ScheduleSync」的属性名集合。
        if (control.GetValue(StateProperty) is LayoutState state)
        {
            state.SlotPropertyNames.Clear();
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Anchor));
            state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Size));
            // 控件名（如 "PART_OutputSlots"）与 ViewModel 属性名（"OutputSlots"）不同；两者都加，
            // OnPropertyChanged("OutputSlots") 才匹配得上。
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
            // 标准属性名一律带兜底默认值，覆盖 ViewModel 直接属性与 SlotEnumerator 成员。
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

            // MAUI 以画布（坐标宿）为基准、靠累加布局位置量中心，这排除了画布的 TranslationX 渲染变换 —— 结果已是画布/世界坐标，不必再减 ActualOffset。
            // （Core 的 SlotAnchorFromVisualCenter 是给按屏幕空间测量的适配器用的。）
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

            // SlotAnchorFromNode：anchor = nodeAnchor + 局部偏移（无坐标宿）。
            newAnchor = WorkflowSurfaceMath.SlotAnchorFromNode(
                node.Anchor.Horizontal, node.Anchor.Vertical,
                center.Value.X, center.Value.Y, slot.Anchor.Layer);
        }

        // 脏检查：锚点值没变就跳过，避免无限循环 SyncSlot → slot.Anchor setter → PropertyChanged → ApplyLayout → MAUI 布局 → SizeChanged/X/Y → ScheduleSync → SyncSlot ……
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
