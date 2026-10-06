using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that measures a node card's slot controls and writes each one's position back into the model.
/// </summary>
/// <remarks>
/// <para>
/// This is where a slot's <see cref="IWorkflowSlotViewModel.Anchor"/> comes from. The template names the controls
/// that hold slots — one name per control in <see cref="SlotNamesProperty"/>, one per
/// <see cref="ItemsControl"/> in <see cref="SlotEnumeratorNamesProperty"/> — and this behavior finds them, reads
/// the slot each one is bound to from its <see cref="FrameworkElement.DataContext"/>, measures its centre and
/// writes the result. Nothing enumerates <c>node.Slots</c>: which slots a card shows is the card's business.
/// </para>
/// <para>
/// Positions are measured against <see cref="CoordinateHostNameProperty"/> (usually the surface canvas), because
/// the surface's own translation is part of where a slot lands on screen. Getting the host wrong is a silent,
/// uniform offset bug — every port and every link endpoint shifts together.
/// </para>
/// <para>
/// A reuse-mode <see cref="ItemsControl"/> hands its containers out lazily, so the enumerator branch reads
/// containers through <see cref="ItemContainerGenerator.ContainerFromIndex"/> and then walks down the visual tree
/// to the element that actually carries the slot; the slot itself never carries the control name, because it lives
/// inside the item template, out of the host's name scope.
/// </para>
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior : DependencyObject
{
    private sealed class LayoutState
    {
        public FrameworkElement? Owner { get; set; }

        public bool Syncing { get; set; }

        public bool SyncPending { get; set; }

        public INotifyPropertyChanged? PropertyChangedSource { get; set; }

        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }

        public HashSet<string> SlotPropertyNames { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The attached property that turns slot measuring on for a node view.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The attached property listing the names of the controls that hold single slots, comma separated.</summary>
    public static readonly DependencyProperty SlotNamesProperty = DependencyProperty.RegisterAttached(
        "SlotNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>The attached property listing the names of the <see cref="ItemsControl"/>s that hold enumerated slots, comma separated.</summary>
    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>The attached property naming the element that slot positions are measured against.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>The attached property giving the type of element that slot positions are measured against.</summary>
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

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => element.GetValue(IsEnabledProperty) is true;

    /// <summary>Sets the <c>IsEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the <c>SlotNames</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetSlotNames(DependencyObject element) => (string?)element.GetValue(SlotNamesProperty);

    /// <summary>Sets the <c>SlotNames</c> attached property on <paramref name="element"/>.</summary>
    public static void SetSlotNames(DependencyObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    /// <summary>Reads the <c>SlotEnumeratorNames</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetSlotEnumeratorNames(DependencyObject element) => (string?)element.GetValue(SlotEnumeratorNamesProperty);

    /// <summary>Sets the <c>SlotEnumeratorNames</c> attached property on <paramref name="element"/>.</summary>
    public static void SetSlotEnumeratorNames(DependencyObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    /// <summary>Reads the <c>CoordinateHostName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetCoordinateHostName(DependencyObject element) => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Sets the <c>CoordinateHostName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Reads the <c>CoordinateHostType</c> attached property from <paramref name="element"/>.</summary>
    public static Type? GetCoordinateHostType(DependencyObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);

    /// <summary>Sets the <c>CoordinateHostType</c> attached property on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (e.NewValue is true)
        {
            Attach(element);
            return;
        }

        Detach(element);
    }

    private static void OnConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && element.GetValue(StateProperty) is LayoutState)
        {
            ScheduleSync(element);
        }
    }

    private static void Attach(FrameworkElement control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState { Owner = control });
        if (control.DataContext is INotifyPropertyChanged context)
        {
            s_owners.AddOrUpdate(context, control);
        }
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void Detach(FrameworkElement control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.SizeChanged -= OnSizeChanged;

        if (control.GetValue(StateProperty) is LayoutState state && state.PropertyChangedSource is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            UpdatePropertyChangedSubscription(control);
            ScheduleSync(control);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.GetValue(StateProperty) is LayoutState state)
        {
            state.SyncPending = false;
        }
    }

    private static void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        ScheduleSync(control);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            ScheduleSync(control);
        }
    }

    private static void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ⚠ sender 是**模型**（谁变更谁是 sender），不是宿主控件 —— 宿主得从状态里拿。
        // 早先这里写成 `sender is FrameworkElement control`，于是每次都在第一行返回：
        // 节点移动后槽锚点从不重测，连线整条冻在拖动前的几何上。
        if (sender is not INotifyPropertyChanged model || !AttachmentOf(model, out var control, out var state))
        {
            return;
        }

        // 只看与插槽位置有关的属性名：模型侧改别的（标题、颜色）不该重跑一遍测量。
        if (e.PropertyName is null || state.SlotPropertyNames.Contains(e.PropertyName))
        {
            ScheduleSync(control);
        }
    }

    // 模型 → 它挂在哪张卡上。`PropertyChanged` 的 sender 是模型，所以这个回指是必需的。
    private static readonly ConditionalWeakTable<INotifyPropertyChanged, FrameworkElement> s_owners = new();

    private static bool AttachmentOf(INotifyPropertyChanged model, out FrameworkElement control, out LayoutState state)
    {
        if (s_owners.TryGetValue(model, out var owner)
            && owner.GetValue(StateProperty) is LayoutState found)
        {
            control = owner;
            state = found;
            return true;
        }

        control = null!;
        state = null!;
        return false;
    }

    private static void UpdatePropertyChangedSubscription(FrameworkElement control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        var source = control.DataContext as INotifyPropertyChanged;
        if (ReferenceEquals(source, state.PropertyChangedSource))
        {
            return;
        }

        if (state.PropertyChangedSource is not null && state.PropertyChangedHandler is not null)
        {
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;
            state.PropertyChangedSource = null;
            state.PropertyChangedHandler = null;
        }

        if (source is not null)
        {
            state.PropertyChangedHandler = OnNodePropertyChanged;
            state.PropertyChangedSource = source;
            s_owners.AddOrUpdate(source, control);
            source.PropertyChanged += state.PropertyChangedHandler;
        }
    }

    // 排到布局之后同步：直接量会读到还没摆好的几何。
    private static void ScheduleSync(FrameworkElement control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
        {
            return;
        }

        // 已经在队里了就丢掉这一次：同一拍里的重复请求没有新信息。
        if (state.SyncPending)
        {
            return;
        }

        state.SyncPending = true;
        control.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            state.SyncPending = false;
            Sync(control);
        }));
    }

    private static void Sync(FrameworkElement host)
    {
        if (host.GetValue(StateProperty) is not LayoutState state || state.Syncing)
        {
            return;
        }

        if (host.DataContext is not IWorkflowNodeViewModel node)
        {
            return;
        }

        state.Syncing = true;
        try
        {
            var coordinateHost = ResolveCoordinateHost(host);
            var slotNames = EnumerateConfiguredNames(GetSlotNames(host));
            var enumeratorNames = EnumerateConfiguredNames(GetSlotEnumeratorNames(host));

            RebuildWatchedNames(state, slotNames, enumeratorNames);

            foreach (var slotName in slotNames)
            {
                if (host.FindName(slotName) is FrameworkElement slotControl)
                {
                    SyncSlot(host, coordinateHost, slotControl, node);
                }
            }

            foreach (var enumeratorName in enumeratorNames)
            {
                SyncSlotEnumerator(host, coordinateHost, node, enumeratorName);
            }
        }
        finally
        {
            state.Syncing = false;
        }
    }

    // 重建「变化时该重跑同步」的属性名集合：控件名（PART_OutputSlots）与 view-model 属性名（OutputSlots）都收，
    // 否则 OnPropertyChanged("OutputSlots") 匹配不上。
    private static void RebuildWatchedNames(LayoutState state, IEnumerable<string> slotNames, IEnumerable<string> enumeratorNames)
    {
        state.SlotPropertyNames.Clear();
        state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Anchor));
        state.SlotPropertyNames.Add(nameof(IWorkflowNodeViewModel.Size));

        foreach (var name in slotNames)
        {
            state.SlotPropertyNames.Add(name);
            if (name.StartsWith("PART_", StringComparison.Ordinal))
            {
                state.SlotPropertyNames.Add(name[5..]);
            }
        }

        foreach (var name in enumeratorNames)
        {
            state.SlotPropertyNames.Add(name);
            if (name.StartsWith("PART_", StringComparison.Ordinal))
            {
                state.SlotPropertyNames.Add(name[5..]);
            }
        }

        state.SlotPropertyNames.Add("InputSlot");
        state.SlotPropertyNames.Add("OutputSlot");
        state.SlotPropertyNames.Add("OutputSlots");
    }

    private static void SyncSlotEnumerator(
        FrameworkElement host,
        FrameworkElement? coordinateHost,
        IWorkflowNodeViewModel node,
        string enumeratorName)
    {
        if (host.FindName(enumeratorName) is not ItemsControl itemsControl || itemsControl.Items.Count == 0)
        {
            return;
        }

        for (var i = 0; i < itemsControl.Items.Count; i++)
        {
            if (itemsControl.ItemContainerGenerator?.ContainerFromIndex(i) is not DependencyObject container)
            {
                continue;
            }

            if (FindDescendantWithSlotDataContext(container) is { } slotView)
            {
                SyncSlot(host, coordinateHost, slotView, node);
            }
        }
    }

    private static void SyncSlot(
        FrameworkElement host,
        FrameworkElement? coordinateHost,
        FrameworkElement control,
        IWorkflowNodeViewModel node)
    {
        if (control.DataContext is not IWorkflowSlotViewModel slot)
        {
            return;
        }

        if (control.ActualWidth <= 0d || control.ActualHeight <= 0d)
        {
            return;
        }

        var center = new Point(control.ActualWidth / 2d, control.ActualHeight / 2d);

        if (coordinateHost is not null)
        {
            // Jalium 的 TransformToAncestor 交回的是**元素在祖先坐标里的原点**（实测：不是 WPF 那个
            // GeneralTransform），所以中心要自己加半个尺寸。
            //
            // 世界位移现在发布在**宿主的附着属性**上、由节点/连线模板绑进各自的 RenderTransform
            // （见 WorkflowSurfaceBehavior.CanvasTransform）—— 位移在坐标宿主之内，所以这里与其余六家
            // 用同一条契约：减去 ActualOffset。
            var origin = control.TransformToAncestor(coordinateHost);
            var layout = node.Parent?.Layout;
            if (layout is not null)
            {
                slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(
                    origin.X + center.X, origin.Y + center.Y, slot.Anchor.Layer, layout);
                return;
            }
        }

        var hostOrigin = control.TransformToAncestor(host);
        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            node.Anchor.Horizontal,
            node.Anchor.Vertical,
            hostOrigin.X + center.X,
            hostOrigin.Y + center.Y,
            slot.Anchor.Layer);
    }

    private static FrameworkElement? ResolveCoordinateHost(FrameworkElement control)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName) && ResolveNamedAncestor(control, hostName!) is { } namedHost)
        {
            return namedHost;
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        if (hostType.IsInstanceOfType(control))
        {
            return control;
        }

        foreach (var ancestor in EnumerateVisualAncestors(control))
        {
            if (ancestor is FrameworkElement element && hostType.IsInstanceOfType(element))
            {
                return element;
            }
        }

        return null;
    }

    // 按名字找宿主：只沿视觉树比 Name。名字作用域在这里帮不上忙 —— 节点视图住在 DataTemplate 里，
    // 而坐标宿主（表面的画布）在模板之外。
    private static FrameworkElement? ResolveNamedAncestor(FrameworkElement control, string hostName)
    {
        if (control.Name == hostName)
        {
            return control;
        }

        foreach (var ancestor in EnumerateVisualAncestors(control))
        {
            if (ancestor is FrameworkElement element && element.Name == hostName)
            {
                return element;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateConfiguredNames(string? names)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            yield break;
        }

        foreach (var name in names!.Split(','))
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(parent, i) is not { } child)
            {
                continue;
            }

            if (child is FrameworkElement { DataContext: IWorkflowSlotViewModel } element)
            {
                return element;
            }

            if (FindDescendantWithSlotDataContext(child) is { } descendant)
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
