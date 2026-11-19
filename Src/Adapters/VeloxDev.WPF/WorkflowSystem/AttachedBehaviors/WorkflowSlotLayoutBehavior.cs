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
        public bool Syncing { get; set; }
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
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        RequestSync(control, force: true);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.IsVisibleChanged -= OnIsVisibleChanged;
        control.LayoutUpdated -= OnLayoutUpdated;
        control.SizeChanged -= OnSizeChanged;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
        }

        CancelSync(control);
        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            RequestSync(control, force: true);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            CancelSync(control);
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        RequestSync(control, force: true);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && Equals(e.NewValue, true))
        {
            RequestSync(control, force: true);
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // 布局后同步回写（与 WinUI 家一致）：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新，而不是迟一个 BeginInvoke(Render) 节拍（插槽漂移/闪烁的来源）。
        // 整棵树每次布局都会走到这里，所以先用指纹挡掉「输入没变」的节点，再进共享批次同步执行。
        if (sender is UserControl control)
        {
            RequestSync(control, force: false);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UserControl control)
        {
            // 缩放折叠每格都改节点 Size。LayoutUpdated 可能在节点 resize（及其 Viewbox 缩放的插槽子元素）落定之前的中间测量/排布趟触发；SizeChanged 只在节点布局达到本次更新的最终尺寸后触发。
            // 在这里同步让插槽锚点保持在节点最终的、同帧几何上。
            RequestSync(control, force: false);
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

        RequestSync(control, force: true);
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

    // 同一棵树的节点共用一个批次：布局 pass 的同步回写走 Flush（同帧），其余触发走 Schedule（到 Render 优先级合并）。
    // 指纹只看模型几何、实测尺寸与插槽控件数 —— 宿主每帧布局时，没动过的节点在这里就被挡掉了。
    private static void RequestSync(UserControl control, bool force)
    {
        if (control.GetValue(StateProperty) is null
            || control.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        var dispatcher = control.Dispatcher;
        var batch = WorkflowLayoutBatch.For(OwnerOf(control, node), action =>
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Render);
            return true;
        });
        var work = new Action(() => Sync(control));

        if (force)
        {
            batch.Request(control, work);
            batch.Schedule();
            return;
        }

        var stamp = WorkflowLayoutStamp.Capture(
            node, control.ActualWidth, control.ActualHeight, CountSlotControls(control));
        if (batch.Request(control, stamp, work))
        {
            batch.Flush();
        }
    }

    private static void CancelSync(UserControl control)
    {
        object owner = control.DataContext is IWorkflowNodeViewModel { Parent: { } tree } ? tree : control;
        WorkflowLayoutBatch.For(owner).Cancel(control);
    }

    private static object OwnerOf(UserControl control, IWorkflowNodeViewModel node)
        => node.Parent ?? (object)control;

    // 指纹里的插槽数：容器数量变了（枚举插槽增删、模板重建）必须重测，哪怕节点外框尺寸没变。
    private static int CountSlotControls(UserControl control)
    {
        var count = 0;

        foreach (var name in GetAllSlotNames(control))
        {
            if (!string.IsNullOrWhiteSpace(name) && control.FindName(name) is FrameworkElement)
            {
                count++;
            }
        }

        foreach (var name in GetAllSlotEnumeratorNames(control))
        {
            if (!string.IsNullOrWhiteSpace(name) && control.FindName(name) is ItemsControl items)
            {
                count += items.Items.Count;
            }
        }

        return count;
    }

    private static void Sync(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        if (state.Syncing)
        {
            return;
        }

        if (control.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        state.Syncing = true;
        try
        {
            var parentHost = control;
            var coordinateHost = ResolveCoordinateHost(control, parentHost);
            var slotNames = GetAllSlotNames(control);
            var enumeratorNames = GetAllSlotEnumeratorNames(control);

            // 重建「变化时该触发同步」的属性名集合。
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
            // 相对画布做 TranslatePoint 得到画布局部（world + ActualOffset），因为 ActualOffset 是逐节点渲染变换；减掉它得到世界坐标。
            var centerOnCanvas = control.TranslatePoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2), coordinateHost);
            var layout = node.Parent?.Layout;
            if (layout is not null)
            {
                slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(
                    centerOnCanvas.X, centerOnCanvas.Y, slot.Anchor.Layer, layout);
                return;
            }
        }

        var center = control.TranslatePoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2), host);
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
