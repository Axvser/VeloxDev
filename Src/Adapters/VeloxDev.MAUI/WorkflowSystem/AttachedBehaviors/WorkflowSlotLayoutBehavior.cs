using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// MAUI attached behavior that measures a node template's declared slot parts and writes their canvas-local
/// anchors back to the model.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to the node view root and name the parts with <see cref="SetSlotNames"/> and
/// <see cref="SetSlotEnumeratorNames"/> (comma-separated names). The behavior owns only the platform half —
/// the visual-tree walk, the screen→canvas center measurement, and the UI-thread hand-off; which parts to
/// measure, their order, duplicate suppression and the re-measure schedule belong to
/// <see cref="SlotLayoutPipelineCore"/>.
/// </para>
/// <para>
/// MAUI measures a slot center by accumulating layout positions up to the coordinate host, which excludes the
/// canvas <c>TranslationX/Y</c> render transform, so the locator reports
/// <see cref="SlotCoordinateSpace.CanvasLocal"/>.
/// </para>
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior
{
    private sealed class LayoutState
    {
        public SlotLayoutPipelineCore? Pipeline { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }

        /// <summary>
        /// WinUI-native LayoutUpdated hook (Windows). Object-typed so LayoutState compiles on every TFM;
        /// only touched inside #if WINDOWS blocks. LayoutUpdated is the end-of-layout pass signal the other
        /// workflow families sync on; MAUI's managed SizeChanged fires mid-arrange and reads a transient frame.
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
        null,
        propertyChanged: OnConfigurationChanged);

    public static readonly BindableProperty SlotEnumeratorNamesProperty = BindableProperty.CreateAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null,
        propertyChanged: OnConfigurationChanged);

    public static readonly BindableProperty CoordinateHostNameProperty = BindableProperty.CreateAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        null,
        propertyChanged: OnConfigurationChanged);

    public static readonly BindableProperty CoordinateHostTypeProperty = BindableProperty.CreateAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowSlotLayoutBehavior),
        null,
        propertyChanged: OnConfigurationChanged);

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

    /// <summary>Re-measures a node view after the canvas zoom or scroll transform changed.</summary>
    /// <param name="control">The node view root the behavior is attached to.</param>
    public static void Refresh(ContentView control) => Measure(control, SlotLayoutTrigger.Viewport);

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

    private static void OnConfigurationChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control || control.GetValue(StateProperty) is not LayoutState)
        {
            return;
        }

        // 配置换了就得换一条流水线：部件名集合在构造时固化成 WatchedPropertyNames。
        RebuildPipeline(control);
        UpdatePropertyChangedSubscription(control);
        Measure(control, SlotLayoutTrigger.Configuration);
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

        RebuildPipeline(control);
        UpdatePropertyChangedSubscription(control);
        Measure(control, SlotLayoutTrigger.Attachment);
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
            if (state.PropertyChangedSource is not null
                && state.PropertyChangedHandler is not null)
            {
                state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            }

            state.Pipeline?.Stop();
            state.Pipeline = null;
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
                // 在 LayoutUpdated 里同步执行：该子树布局已完成，插槽中心是最终值。共享批次先比指纹，
                // 几何稳定后的重复趟直接跳过；流水线的脏检查再挡掉一次无变化的写入 —— 没有重排链，也不会卡死。
                if (control.GetValue(StateProperty) is not null)
                {
                    Measure(control, SlotLayoutTrigger.Layout);
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
            Measure(control, SlotLayoutTrigger.Attachment);
        }
    }

    private static void OnUnloaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            // 卸载只丢排队工作，流水线留着：重载回来时同一份配置直接复用。
            if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } current })
            {
                current.CancelPending();
            }
        }
    }

    private static void OnNodeResized(object? sender, EventArgs e)
    {
        // 仅非 Windows 兜底。Windows 不用这个钩子：托管 SizeChanged 在 arrange 期间触发，量到的锚点会过冲最终几何约 10ms
        // （见 git 历史：每格端点跳动）；Windows 改用平台 LayoutUpdated 同步。
        if (sender is ContentView control)
        {
            Measure(control, SlotLayoutTrigger.Layout);
        }
    }

    private static void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control)
        {
            return;
        }

        RebuildPipeline(control);
        UpdatePropertyChangedSubscription(control);
        Measure(control, SlotLayoutTrigger.Attachment);
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

        if (state.Pipeline is not { } pipeline
            || control.BindingContext is not INotifyPropertyChanged notify)
        {
            return;
        }

        // 用直接捕获 ContentView 的 lambda：节点 VM 只是普通 INotifyPropertyChanged（不是 BindableObject/Element），无法从 sender 沿可视树找到对应视图。
        // 只看与插槽位置有关的属性名：标题、颜色这类变更不该重跑一遍测量。
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is not null && pipeline.WatchedPropertyNames.Contains(e.PropertyName))
            {
                pipeline.RequestMeasure(SlotLayoutTrigger.Model);
            }
        };
        state.PropertyChangedSource = notify;
        state.PropertyChangedHandler = handler;
        notify.PropertyChanged += handler;
    }

    private static void RebuildPipeline(ContentView control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        state.Pipeline?.Stop();
        state.Pipeline = null;

        if (control.BindingContext is not INodeComponent node)
        {
            return;
        }

        var configuration = new SlotLayoutConfiguration(
            GetSlotNames(control),
            GetSlotEnumeratorNames(control),
            GetCoordinateHostName(control),
            GetCoordinateHostType(control));

        state.Pipeline = new SlotLayoutPipelineCore(
            node,
            control,
            new MauiSlotPartLocator(control),
            new MauiSlotMeasurementHost(control),
            configuration);
    }

    private static void Measure(ContentView control, SlotLayoutTrigger trigger)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(trigger);
        }
    }

    // ── 平台层：可视树遍历、坐标测量与坐标宿解析 ─────────────────────────────

    private sealed class MauiSlotPartLocator(ContentView owner) : ISlotPartLocator, ISlotPartIdentityLocator
    {
        // 以画布（坐标宿）为基准累加布局位置得到的中心已排除画布 TranslationX/Y，所以是画布/世界坐标。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.CanvasLocal;

        public ISlotPart? FindNamedSlot(string name)
            => owner.FindByName<VisualElement>(name) is { } element ? new MauiSlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (owner.FindByName<Layout>(name) is not { } itemsLayout)
            {
                return Array.Empty<ISlotPart>();
            }

            var parts = new List<ISlotPart>();
            foreach (var slotView in FindDescendants<VisualElement>(itemsLayout))
            {
                if (slotView.BindingContext is ISlotComponent)
                {
                    parts.Add(new MauiSlotPart(owner, slotView));
                }
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
        {
            var type = hostType ?? typeof(AbsoluteLayout);
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                var namedHost = ResolveNamedHost(owner, hostName!);
                if (namedHost is not null)
                {
                    return namedHost;
                }
            }

            return EnumerateSelfAndAncestors(owner)
                .OfType<VisualElement>()
                .FirstOrDefault(x => type.IsAssignableFrom(x.GetType()));
        }

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            // 指纹里只数 FindEnumeratedSlots 能看见的部件（BindingContext 是 ISlotComponent 的后代）：
            // 数 layout.Children.Count 会让「插槽视图还没生成」那一趟得到一个看似完整的指纹，锚点停在 NaN。
            var count = 0;

            foreach (var name in slotNames)
            {
                if (!string.IsNullOrWhiteSpace(name) && owner.FindByName<VisualElement>(name) is not null)
                {
                    count++;
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (string.IsNullOrWhiteSpace(name) || owner.FindByName<Layout>(name) is not { } layout)
                {
                    continue;
                }

                foreach (var slotView in FindDescendants<VisualElement>(layout))
                {
                    if (slotView.BindingContext is ISlotComponent)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        // 与 CountSlots 同一趟遍历，额外哈希已实现的元素身份：同一模型槽位换一批容器时数量不变，
        // 只有它能发现容器重建。无分配。
        public int ComputeSlotIdentity(
            IReadOnlyList<string> slotNames,
            IReadOnlyList<string> enumeratorNames,
            out int slotCount)
        {
            var count = 0;
            var hash = 17;

            foreach (var name in slotNames)
            {
                if (!string.IsNullOrWhiteSpace(name) && owner.FindByName<VisualElement>(name) is { } element)
                {
                    count++;
                    hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element));
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (string.IsNullOrWhiteSpace(name) || owner.FindByName<Layout>(name) is not { } layout)
                {
                    continue;
                }

                foreach (var slotView in FindDescendants<VisualElement>(layout))
                {
                    if (slotView.BindingContext is ISlotComponent)
                    {
                        count++;
                        hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slotView));
                    }
                }
            }

            slotCount = count;
            return hash;
        }
    }

    private sealed class MauiSlotPart(ContentView nodeView, VisualElement element) : ISlotPart, ISlotPartVisibility
    {
        public ISlotComponent? Slot => element.BindingContext as ISlotComponent;

        public bool IsMeasured => element.Width > 0d && element.Height > 0d;

        public bool IsVisible => element.IsVisible && nodeView.IsVisible;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var target = relativeTo as VisualElement ?? nodeView;
            return GetCenterRelativeTo(element, target) is { } center
                ? new SlotCenter(center.X, center.Y)
                : null;
        }
    }

    private sealed class MauiSlotMeasurementHost(ContentView owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.Width, owner.Height);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
        {
            // 用 IDispatcherTimer（约一帧延迟）取代嵌套 BeginInvoke：MAUI 没有 WPF 的 DispatcherPriority.Render，
            // 原来的两级派发是依赖队列顺序的脆弱 hack。共享批次保证一次请求潮只创建一个定时器。
            var timer = owner.Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);  // ≈1 frame
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                try
                {
                    timer.Stop();
                    flush();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // MAUI 不捕获 IDispatcherTimer.Tick 的异常，它会未处理地冒到 WinUI 的 UnhandledException；这是已知的 MAUI/WinUI 缺口（dotnet/maui #12245、#10341）。
                    System.Diagnostics.Debug.WriteLine(
                        $"[WorkflowSlotLayout] Sync error: {ex.Message}");
                }
            };
            timer.Start();
            return true;
        }
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
