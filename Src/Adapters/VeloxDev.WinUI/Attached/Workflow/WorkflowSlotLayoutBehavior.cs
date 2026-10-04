using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
        public bool SyncPending { get; set; }
        public bool Syncing { get; set; }
        public EventHandler<object>? LayoutUpdatedHandler { get; set; }
        // 坐标宿（PART_Canvas）在节点自身子树布局完成后才重排其子元素。WinUI 的 LayoutUpdated 是逐元素的，所以同步在节点自身的 LayoutUpdated 上可能在节点移动前的画布位置量插槽，
        // 而连线端点（绑到量到的锚点）在折叠缩放中就比节点慢一帧 —— 表现为「缩放时连线端点从端口上垂下来」。
        // 挂宿主的 LayoutUpdated 读到的是排完后的几何（WPF 家免疫，因为 WPF 的 LayoutUpdated 整棵树布局趟只触发一次，总是排完之后）。
        public FrameworkElement? CoordinateHost { get; set; }
        public EventHandler<object>? CoordinateHostLayoutHandler { get; set; }
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

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        // LayoutUpdated 的 sender 永远是 null（微软文档），所以用闭包捕获控件并同步执行（布局后、同帧）—— 异步 DispatcherQueue 跳转会让插槽锚点（及绑到它的连线）比节点折叠慢一帧。
        if (control.GetValue(StateProperty) is LayoutState state)
        {
            state.LayoutUpdatedHandler = (_, _) => Sync(control);
            control.LayoutUpdated += state.LayoutUpdatedHandler;
        }
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
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
        }

        control.ClearValue(StateProperty);
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
        state.CoordinateHostLayoutHandler = (_, _) => Sync(control);
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
            ScheduleSync(control);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
        {
            DetachCoordinateHost(state);
            state.SyncPending = false;
        }
    }

    private static void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UserControl control)
        {
            // SizeChanged 只在布局趟定稿后触发（微软文档）；同步执行，插槽锚点与节点 resize 同帧更新。
            Sync(control);
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
        control.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (control.GetValue(StateProperty) is not LayoutState currentState)
            {
                return;
            }

            currentState.SyncPending = false;
            Sync(control);
        });
    }

    private static void Sync(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state || state.Syncing)
        {
            return;
        }

        // LayoutUpdated 可能在测量中嵌套触发；加防护，避免 OnLayoutUpdated 里的同步 Sync 重入。防护对异常安全（try/finally）：
        // 布局中途 TransformToVisual 抛异常绝不能把 Syncing 留在置位状态，否则之后每次 Sync 都被压掉，插槽锚点（及绑到它的连线）永久失效。
        state.Syncing = true;
        try
        {
            if (control.DataContext is not IWorkflowNodeViewModel node)
            {
                return;
            }

            var parentHost = control;
            var coordinateHost = ResolveCoordinateHost(control, parentHost);
            var slotNames = GetAllSlotNames(control);
            var enumeratorNames = GetAllSlotEnumeratorNames(control);

            // 重建「变化时该触发 ScheduleSync」的属性名集合。
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

            foreach (var slotName in slotNames)
            {
                SyncNamedSlot(parentHost, control, coordinateHost, node, slotName);
            }

            foreach (var enumeratorName in enumeratorNames)
            {
                SyncSlotEnumerator(parentHost, control, coordinateHost, node, enumeratorName);
            }
        }
        finally
        {
            state.Syncing = false;
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

        bool anyMissing = false;
        for (var i = 0; i < itemsControl.Items.Count; i++)
        {
            if (itemsControl.ContainerFromIndex(i) is not DependencyObject container)
            {
                anyMissing = true;
                continue;
            }

            var slotView = FindDescendantWithSlotDataContext(container);
            if (slotView is null)
            {
                anyMissing = true;
                continue;
            }

            // 容器已存在但尚未测量 —— 稍后重试。
            if (slotView.ActualWidth <= 0 || slotView.ActualHeight <= 0)
            {
                anyMissing = true;
                continue;
            }

            SyncSlot(host, coordinateHost, slotView, node);
        }

        // 容器尚未生成或测量 —— 布局完成后重试。
        if (anyMissing)
        {
            parentHost.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                SyncSlotEnumerator(parentHost, host, coordinateHost, node, enumeratorName);
            });
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
            var centerOnCanvas = control.TransformToVisual(coordinateHost).TransformPoint(new Windows.Foundation.Point(control.ActualWidth / 2, control.ActualHeight / 2));
            // coordinateHost 是画布，变换已反转画布平移 —— 中心是世界/画布局部坐标，不能减 ActualOffset。
            slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(
                centerOnCanvas.X, centerOnCanvas.Y, slot.Anchor.Layer);
            return;
        }

        var center = control.TransformToVisual(host).TransformPoint(new Windows.Foundation.Point(control.ActualWidth / 2, control.ActualHeight / 2));
        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            node.Anchor.Horizontal, node.Anchor.Vertical, center.X, center.Y, slot.Anchor.Layer);
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
        return EnumerateSelfAndAncestors(parentHost).OfType<FrameworkElement>().FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static FrameworkElement? ResolveNamedHost(DependencyObject source, string? hostName)
    {
        return EnumerateSelfAndAncestors(source)
            .OfType<FrameworkElement>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    private static string[] GetAllSlotNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotNames(control)).Distinct(StringComparer.Ordinal)];

    private static string[] GetAllSlotEnumeratorNames(UserControl control)
        => [.. EnumerateConfiguredNames(GetSlotEnumeratorNames(control)).Distinct(StringComparer.Ordinal)];

    private static IEnumerable<string> EnumerateConfiguredNames(string? configuredNames)
    {
        if (string.IsNullOrWhiteSpace(configuredNames))
        {
            yield break;
        }

        foreach (var name in configuredNames.Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.DataContext is IWorkflowSlotViewModel)
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

    private static Offset GetActualOffset(IWorkflowTreeViewModel? tree)
    {
        if (tree is null)
        {
            return new Offset();
        }

        return tree.Layout.ActualOffset;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
            {
                return result;
            }

            var descendant = FindDescendant<T>(child);
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
