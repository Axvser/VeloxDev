using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WinUI attached behavior that measures a node template's declared slot parts and writes their canvas-local anchors
/// back to the model.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to the node view root and name the parts with <see cref="SlotNamesProperty"/> and
/// <see cref="SlotEnumeratorNamesProperty"/> (comma-separated <c>x:Name</c>s). The behavior owns only the platform
/// half — the visual-tree walk, the <c>TransformToVisual</c> transform, the coordinate-host hook and the UI-thread
/// hand-off; which parts to measure, their order, duplicate suppression and the re-measure schedule belong to
/// <see cref="SlotLayoutPipelineCore"/>.
/// </para>
/// <para>
/// The transform into the coordinate host already inverts the canvas translation, so the locator reports
/// <see cref="SlotCoordinateSpace.CanvasLocal"/> and the pipeline writes the measured center unchanged.
/// </para>
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public SlotLayoutPipelineCore? Pipeline { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public EventHandler<object>? LayoutUpdatedHandler { get; set; }

        // 坐标宿（PART_Canvas）在节点自身子树布局完成后才重排其子元素。WinUI 的 LayoutUpdated 是逐元素的，所以同步在节点自身的 LayoutUpdated 上可能在节点移动前的画布位置量插槽，
        // 而连线端点（绑到量到的锚点）在折叠缩放中就比节点慢一帧 —— 表现为「缩放时连线端点从端口上垂下来」。
        // 挂宿主的 LayoutUpdated 读到的是排完后的几何（WPF 家免疫，因为 WPF 的 LayoutUpdated 整棵树布局趟只触发一次，总是排完之后）。
        public FrameworkElement? CoordinateHost { get; set; }
        public EventHandler<object>? CoordinateHostLayoutHandler { get; set; }

        // 枚举插槽的容器可能还没生成；低优先级重试只保留一份，避免每个布局趟都叠一条队列。
        public bool RetryQueued { get; set; }
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
        new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

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

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetSlotNames(DependencyObject element) => element.GetValue(SlotNamesProperty) as string;
    public static void SetSlotNames(DependencyObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    public static string? GetSlotEnumeratorNames(DependencyObject element) => element.GetValue(SlotEnumeratorNamesProperty) as string;
    public static void SetSlotEnumeratorNames(DependencyObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    public static string? GetCoordinateHostName(DependencyObject element) => element.GetValue(CoordinateHostNameProperty) as string;
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(DependencyObject element) => element.GetValue(CoordinateHostTypeProperty) as Type;
    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control)
        {
            return;
        }

        if (e.NewValue is true)
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
        // LayoutUpdated 的 sender 永远是 null（微软文档），所以用闭包捕获控件；同步写入交给共享批次，在布局 pass 内同步 Flush。
        // 异步 DispatcherQueue 跳转会让插槽锚点（及绑到它的连线）比节点折叠慢一帧。
        if (control.GetValue(StateProperty) is LayoutState state)
        {
            state.LayoutUpdatedHandler = (_, _) => Measure(control, SlotLayoutTrigger.Layout);
            control.LayoutUpdated += state.LayoutUpdatedHandler;
        }
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
        control.SizeChanged -= OnSizeChanged;

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            if (state.LayoutUpdatedHandler is not null)
            {
                control.LayoutUpdated -= state.LayoutUpdatedHandler;
                state.LayoutUpdatedHandler = null;
            }

            DetachCoordinateHost(state);

            if (state.PropertyChangedSource is not null)
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
            new WinUiSlotPartLocator(control, state),
            new WinUiSlotMeasurementHost(control),
            configuration);
    }

    private static void Measure(UserControl control, SlotLayoutTrigger trigger)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(trigger);
        }
    }

    private static void AttachCoordinateHost(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.CoordinateHost is not null)
        {
            return;
        }

        var host = ResolveCoordinateHost(control, control);
        if (host is null)
        {
            return;
        }

        state.CoordinateHost = host;
        state.CoordinateHostLayoutHandler = (_, _) => Measure(control, SlotLayoutTrigger.Layout);
        host.LayoutUpdated += state.CoordinateHostLayoutHandler;
    }

    private static void DetachCoordinateHost(LayoutState state)
    {
        if (state.CoordinateHost is not null && state.CoordinateHostLayoutHandler is not null)
        {
            state.CoordinateHost.LayoutUpdated -= state.CoordinateHostLayoutHandler;
        }

        state.CoordinateHost = null;
        state.CoordinateHostLayoutHandler = null;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            AttachCoordinateHost(control);
            Measure(control, SlotLayoutTrigger.Attachment);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
        {
            // 卸载只丢排队工作，流水线留着：重载回来时同一份配置直接复用。
            DetachCoordinateHost(state);
            state.Pipeline?.CancelPending();
        }
    }

    private static void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        AttachCoordinateHost(control);
        Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UserControl control)
        {
            // SizeChanged 只在布局趟定稿后触发（微软文档）；同步执行，插槽锚点与节点 resize 同帧更新。
            Measure(control, SlotLayoutTrigger.Layout);
        }
    }

    private static void OnNodePropertyChanged(UserControl control, PropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not LayoutState { Pipeline: { } pipeline })
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

    // ── 平台层：可视树遍历与坐标转换 ────────────────────────────────────────

    private sealed class WinUiSlotPartLocator(UserControl owner, LayoutState state) : ISlotPartLocator, ISlotPartIdentityLocator
    {
        // TransformToVisual 到画布已经反转了画布平移，中心就是世界/画布局部坐标，不能再减 ActualOffset。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.CanvasLocal;

        public ISlotPart? FindNamedSlot(string name)
            => owner.FindName(name) is FrameworkElement element ? new WinUiSlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (owner.FindName(name) is not ItemsControl items || items.Items.Count == 0)
            {
                return Array.Empty<ISlotPart>();
            }

            var parts = new List<ISlotPart>(items.Items.Count);
            var missing = false;
            for (var i = 0; i < items.Items.Count; i++)
            {
                if (items.ContainerFromIndex(i) is not DependencyObject container)
                {
                    missing = true;
                    continue;
                }

                // 插槽自身不带控件名：它住在 item template 里，宿主名字作用域够不着，只能沿容器向下找。
                if (FindDescendantWithSlotDataContext(container) is not { } slotView)
                {
                    missing = true;
                    continue;
                }

                parts.Add(new WinUiSlotPart(owner, slotView));

                // 容器已存在但尚未测量 —— 本趟跳过，稍后重试。
                if (slotView.ActualWidth <= 0d || slotView.ActualHeight <= 0d)
                {
                    missing = true;
                }
            }

            if (missing)
            {
                ScheduleRetry();
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
            => global::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowSlotLayoutBehavior.ResolveCoordinateHost(
                owner, owner, hostName, hostType);

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
                    if (items.ContainerFromIndex(i) is not null)
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
                if (owner.FindName(name) is FrameworkElement element)
                {
                    count++;
                    hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element));
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
                    if (items.ContainerFromIndex(i) is { } container)
                    {
                        count++;
                        hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container));
                    }
                }
            }

            slotCount = count;
            return hash;
        }

        // 容器尚未生成或尚未测量：布局完成后重试一次（低优先级），重试跳上重新请求一次无条件测量。
        private void ScheduleRetry()
        {
            if (state.RetryQueued)
            {
                return;
            }

            var queue = owner.DispatcherQueue;
            if (queue is null)
            {
                return;
            }

            state.RetryQueued = true;
            if (!queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                state.RetryQueued = false;
                Measure(owner, SlotLayoutTrigger.Attachment);
            }))
            {
                state.RetryQueued = false;
            }
        }
    }

    private sealed class WinUiSlotPart(UserControl nodeView, FrameworkElement element) : ISlotPart, ISlotPartVisibility
    {
        public ISlotComponent? Slot => element.DataContext as ISlotComponent;

        public bool IsMeasured => element.ActualWidth > 0d && element.ActualHeight > 0d;

        // WinUI 没有有效可见性属性：自己与节点视图都 Visible 才算可见（池化回收会把节点视图 Collapsed）。
        public bool IsVisible
            => element.Visibility == Visibility.Visible && nodeView.Visibility == Visibility.Visible;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var target = relativeTo as UIElement ?? nodeView;

            try
            {
                var center = element.TransformToVisual(target).TransformPoint(
                    new Windows.Foundation.Point(element.ActualWidth / 2d, element.ActualHeight / 2d));
                return new SlotCenter(center.X, center.Y);
            }
            catch (ArgumentException)
            {
                // 布局中途元素可能已脱离可视树；这一趟不报中心，流水线会退回节点局部空间。
                return null;
            }
        }
    }

    private sealed class WinUiSlotMeasurementHost(UserControl owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.ActualWidth, owner.ActualHeight);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
            => owner.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Normal, () => flush()) ?? false;
    }

    private static FrameworkElement? ResolveCoordinateHost(
        UserControl control, UserControl parentHost, string? hostName = null, Type? hostType = null)
    {
        var name = hostName ?? GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var namedHost = ResolveNamedHost(parentHost, name);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var type = hostType ?? GetCoordinateHostType(control) ?? typeof(Canvas);
        return EnumerateSelfAndAncestors(parentHost).OfType<FrameworkElement>().FirstOrDefault(x => type.IsAssignableFrom(x.GetType()));
    }

    private static FrameworkElement? ResolveNamedHost(DependencyObject source, string? hostName)
    {
        return EnumerateSelfAndAncestors(source)
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.DataContext is ISlotComponent)
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

    private static IEnumerable<DependencyObject> EnumerateSelfAndAncestors(DependencyObject source)
    {
        yield return source;
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
