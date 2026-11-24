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
/// <summary>
/// WPF attached behavior that measures a node template's declared slot parts and writes their canvas-local anchors
/// back to the model.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to the node view root and name the parts with <see cref="SlotNamesProperty"/> and
/// <see cref="SlotEnumeratorNamesProperty"/> (comma-separated <c>x:Name</c>s). The behavior owns only the platform
/// half — the visual-tree walk, the <c>TranslatePoint</c> transform, and the UI-thread hand-off; which parts to
/// measure, their order, duplicate suppression and the re-measure schedule belong to
/// <see cref="SlotLayoutPipelineCore"/>.
/// </para>
/// <para>
/// WPF's <c>TranslatePoint</c> into the canvas includes the node's per-node offset transform, so the locator reports
/// <see cref="SlotCoordinateSpace.ScrollSpace"/> and the pipeline subtracts <see cref="CanvasLayout.ActualOffset"/>.
/// </para>
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public SlotLayoutPipelineCore? Pipeline { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
    }

    /// <summary>Whether the behavior measures the node view's declared slot parts.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Comma-separated names of visual elements that each render one slot.</summary>
    public static readonly DependencyProperty SlotNamesProperty = DependencyProperty.RegisterAttached(
        "SlotNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Comma-separated names of visual items controls whose realized items render slots.</summary>
    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Name of the element that supplies the coordinate space for slot anchors.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Element type used when the coordinate-host name does not resolve; defaults to <see cref="Canvas"/>.</summary>
    public static readonly DependencyProperty CoordinateHostTypeProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(LayoutState),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null));

    /// <summary>Reads whether slot measuring is enabled on <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether slot measuring is enabled on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the named single-slot parts from <paramref name="element"/>.</summary>
    public static string? GetSlotNames(DependencyObject element) => (string?)element.GetValue(SlotNamesProperty);

    /// <summary>Sets the named single-slot parts on <paramref name="element"/>.</summary>
    public static void SetSlotNames(DependencyObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    /// <summary>Reads the named slot enumerator parts from <paramref name="element"/>.</summary>
    public static string? GetSlotEnumeratorNames(DependencyObject element) => (string?)element.GetValue(SlotEnumeratorNamesProperty);

    /// <summary>Sets the named slot enumerator parts on <paramref name="element"/>.</summary>
    public static void SetSlotEnumeratorNames(DependencyObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    /// <summary>Reads the coordinate-host name from <paramref name="element"/>.</summary>
    public static string? GetCoordinateHostName(DependencyObject element) => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Sets the coordinate-host name on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Reads the coordinate-host type from <paramref name="element"/>.</summary>
    public static Type? GetCoordinateHostType(DependencyObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);

    /// <summary>Sets the coordinate-host type on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    /// <summary>Re-measures a node view after the canvas zoom or scroll transform changed.</summary>
    /// <remarks>
    /// A transform-only change leaves the stamp's model geometry and measured size untouched, so the layout trigger
    /// cannot see it; a zoom path calls this to force the pass.
    /// </remarks>
    public static void Refresh(UserControl control)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(SlotLayoutTrigger.Viewport);
        }
    }

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

    private static void OnConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control || control.GetValue(StateProperty) is not LayoutState)
        {
            return;
        }

        // 配置换了就得换一条流水线：部件名集合在构造时固化成 WatchedPropertyNames。
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Configuration);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.IsVisibleChanged += OnIsVisibleChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.IsVisibleChanged -= OnIsVisibleChanged;
        control.LayoutUpdated -= OnLayoutUpdated;
        control.SizeChanged -= OnSizeChanged;

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            if (state.PropertyChangedSource is not null && state.PropertyChangedHandler is not null)
            {
                state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            }

            state.Pipeline?.Stop();
            state.Pipeline = null;
        }

        control.ClearValue(StateProperty);
    }

    private static void RebuildPipeline(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        state.Pipeline?.Stop();
        state.Pipeline = null;

        if (control.DataContext is not INodeComponent node)
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
            new WpfSlotPartLocator(control),
            new WpfSlotMeasurementHost(control),
            configuration);
    }

    private static void Measure(UserControl control, SlotLayoutTrigger trigger)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(trigger);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            Measure(control, SlotLayoutTrigger.Attachment);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
        {
            // 卸载只丢排队工作，流水线留着：重载回来时同一份配置直接复用。
            state.Pipeline?.CancelPending();
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && Equals(e.NewValue, true))
        {
            Measure(control, SlotLayoutTrigger.Attachment);
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // 布局后同步回写：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新。
        // 整棵树每次布局都会走到这里，所以交给流水线先用指纹挡掉「输入没变」的节点，再进共享批次同步执行。
        if (sender is UserControl control)
        {
            Measure(control, SlotLayoutTrigger.Layout);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 缩放折叠每格都改节点 Size。LayoutUpdated 可能在节点 resize（及其 Viewbox 缩放的插槽子元素）落定之前的
        // 中间测量/排布趟触发；SizeChanged 只在节点布局达到本次更新的最终尺寸后触发，指纹里的尺寸也会随之变化。
        if (sender is UserControl control)
        {
            Measure(control, SlotLayoutTrigger.Layout);
        }
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not UserControl control
            || control.GetValue(StateProperty) is not LayoutState { Pipeline: { } pipeline })
        {
            return;
        }

        // 只看与插槽位置有关的属性名：标题、颜色这类变更不该重跑一遍测量。
        if (e.PropertyName is not null && !pipeline.WatchedPropertyNames.Contains(e.PropertyName))
        {
            return;
        }

        pipeline.RequestMeasure(SlotLayoutTrigger.Model);
    }

    private static void UpdatePropertyChangedSubscription(UserControl control)
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

        if (control.DataContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = (_, e) => OnNodePropertyChanged(control, e);
            notify.PropertyChanged += state.PropertyChangedHandler;
        }
    }

    // ── 平台层：可视树遍历与坐标转换 ────────────────────────────────────────

    private sealed class WpfSlotPartLocator(UserControl owner) : ISlotPartLocator
    {
        // TranslatePoint 到画布得到的是「world + ActualOffset」，所以由流水线减掉布局偏移。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.ScrollSpace;

        public ISlotPart? FindNamedSlot(string name)
            => owner.FindName(name) is FrameworkElement element ? new WpfSlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (owner.FindName(name) is not ItemsControl items || items.Items.Count == 0)
            {
                return Array.Empty<ISlotPart>();
            }

            var parts = new List<ISlotPart>(items.Items.Count);
            for (var i = 0; i < items.Items.Count; i++)
            {
                if (items.ItemContainerGenerator.ContainerFromIndex(i) is not DependencyObject container)
                {
                    continue;
                }

                // 插槽自身不带控件名：它住在 item template 里，宿主名字作用域够不着，只能沿容器向下找。
                if (FindDescendantWithSlotDataContext(container) is { } slotView)
                {
                    parts.Add(new WpfSlotPart(owner, slotView));
                }
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
        {
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                var namedHost = ResolveNamedHost(owner, hostName!);
                if (namedHost is not null)
                {
                    return namedHost;
                }
            }

            var type = hostType ?? typeof(Canvas);
            if (type.IsAssignableFrom(owner.GetType()))
            {
                return owner;
            }

            return EnumerateVisualAncestors(owner)
                .OfType<FrameworkElement>()
                .FirstOrDefault(x => type.IsAssignableFrom(x.GetType()));
        }

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            // 指纹里只数 FindEnumeratedSlots 能看见的部件：数 items.Items.Count 会让「容器还没生成」那一趟
            // 得到一个看似完整的指纹，那一趟被当成测过，锚点停在 NaN，整条连线不画。
            var count = 0;

            foreach (var name in slotNames)
            {
                if (owner.FindName(name) is FrameworkElement)
                {
                    count++;
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (owner.FindName(name) is not ItemsControl items)
                {
                    continue;
                }

                for (var i = 0; i < items.Items.Count; i++)
                {
                    if (items.ItemContainerGenerator.ContainerFromIndex(i) is not null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }

    private sealed class WpfSlotPart(UserControl nodeView, FrameworkElement element) : ISlotPart
    {
        public ISlotComponent? Slot => element.DataContext as ISlotComponent;

        public bool IsMeasured => element.ActualWidth > 0d && element.ActualHeight > 0d;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var target = relativeTo as UIElement ?? nodeView;
            var center = element.TranslatePoint(
                new Point(element.ActualWidth / 2d, element.ActualHeight / 2d), target);
            return new SlotCenter(center.X, center.Y);
        }
    }

    private sealed class WpfSlotMeasurementHost(UserControl owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.ActualWidth, owner.ActualHeight);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
        {
            owner.Dispatcher.BeginInvoke(flush, DispatcherPriority.Render);
            return true;
        }
    }

    private static FrameworkElement? ResolveNamedHost(FrameworkElement control, string hostName)
    {
        if (control.Name == hostName)
        {
            return control;
        }

        return EnumerateVisualAncestors(control)
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { DataContext: ISlotComponent } element)
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
