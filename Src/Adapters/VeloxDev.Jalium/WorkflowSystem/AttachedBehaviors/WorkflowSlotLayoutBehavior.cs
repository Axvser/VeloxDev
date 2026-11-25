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
/// This is where a slot's <see cref="ISlotComponent.Anchor"/> comes from. The template names the controls
/// that hold slots — one name per control in <see cref="SlotNamesProperty"/>, one per
/// <see cref="ItemsControl"/> in <see cref="SlotEnumeratorNamesProperty"/> — and the behavior finds them, reads
/// the slot each one is bound to from its <see cref="FrameworkElement.DataContext"/>, measures its centre and
/// writes the result. Nothing enumerates <c>node.Slots</c>: which slots a card shows is the card's business.
/// </para>
/// <para>
/// The behavior owns only the platform half — the visual-tree walk, the <c>TransformToVisual</c> transform, and the
/// UI-thread hand-off; which parts to measure, their order, duplicate suppression and the re-measure schedule belong
/// to <see cref="SlotLayoutPipelineCore"/>.
/// </para>
/// <para>
/// Positions are measured against <see cref="CoordinateHostNameProperty"/> (usually the surface canvas), because
/// the surface's own translation is part of where a slot lands on screen. Jalium's transform reports scroll space,
/// so the locator declares <see cref="SlotCoordinateSpace.ScrollSpace"/> and the pipeline subtracts
/// <see cref="CanvasLayout.ActualOffset"/>. Getting the host wrong is a silent, uniform offset bug — every port and
/// every link endpoint shifts together.
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
        public SlotLayoutPipelineCore? Pipeline { get; set; }

        public INotifyPropertyChanged? PropertyChangedSource { get; set; }

        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
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

    /// <summary>Re-measures the slots of a node view after the canvas transform or scale changed.</summary>
    /// <remarks>
    /// A transform-only change leaves the stamp's model geometry and measured size untouched, so the layout trigger
    /// cannot see it; a zoom path calls this to force the pass.
    /// </remarks>
    public static void Refresh(FrameworkElement control) => RequestMeasure(control, SlotLayoutTrigger.Viewport);

    /// <summary>Re-measures the slots immediately, for callers that already ran the layout pass.</summary>
    public static void SyncNow(FrameworkElement control)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.Measure();
        }
    }

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
        if (d is not FrameworkElement element || element.GetValue(StateProperty) is not LayoutState)
        {
            return;
        }

        // 配置换了就得换一条流水线：部件名集合在构造时固化成 WatchedPropertyNames。
        RebuildPipeline(element);
        RequestMeasure(element, SlotLayoutTrigger.Configuration);
    }

    private static void Attach(FrameworkElement control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        if (control.DataContext is INotifyPropertyChanged context)
        {
            s_owners.AddOrUpdate(context, control);
        }

        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.SizeChanged += OnSizeChanged;
        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        RequestMeasure(control, SlotLayoutTrigger.Attachment);
    }

    private static void Detach(FrameworkElement control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
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

    private static void RebuildPipeline(FrameworkElement control)
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
            new JaliumSlotPartLocator(control),
            new JaliumSlotMeasurementHost(control),
            configuration);
    }

    private static void RequestMeasure(FrameworkElement control, SlotLayoutTrigger trigger)
    {
        if (control.GetValue(StateProperty) is LayoutState { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(trigger);
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            UpdatePropertyChangedSubscription(control);
            RequestMeasure(control, SlotLayoutTrigger.Attachment);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.GetValue(StateProperty) is LayoutState state)
        {
            // 卸载只丢排队工作，流水线留着：重载回来时同一份配置直接复用。
            state.Pipeline?.CancelPending();
        }
    }

    private static void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement control)
        {
            return;
        }

        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        RequestMeasure(control, SlotLayoutTrigger.Attachment);
    }

    private static void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 缩放折叠每格都改节点 Size；Layout 触发先用指纹挡掉输入没变的节点，再在共享批次里同步落笔。
        if (sender is FrameworkElement control)
        {
            RequestMeasure(control, SlotLayoutTrigger.Layout);
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

        // 只看与插槽位置有关的属性名：标题、颜色这类变更不该重跑一遍测量。
        if (state.Pipeline is { } pipeline
            && (e.PropertyName is null || pipeline.WatchedPropertyNames.Contains(e.PropertyName)))
        {
            pipeline.RequestMeasure(SlotLayoutTrigger.Model);
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

    // ── 平台层：可视树遍历与坐标转换 ────────────────────────────────────────

    private sealed class JaliumSlotPartLocator(FrameworkElement owner) : ISlotPartLocator, ISlotPartIdentityLocator
    {
        // TransformToVisual 到画布得到的是「world + ActualOffset」，所以由流水线减掉布局偏移。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.ScrollSpace;

        public ISlotPart? FindNamedSlot(string name)
            => owner.FindName(name) is FrameworkElement element ? new JaliumSlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (owner.FindName(name) is not ItemsControl itemsControl || itemsControl.Items.Count == 0)
            {
                return Array.Empty<ISlotPart>();
            }

            var parts = new List<ISlotPart>(itemsControl.Items.Count);
            for (var i = 0; i < itemsControl.Items.Count; i++)
            {
                if (itemsControl.ItemContainerGenerator?.ContainerFromIndex(i) is not DependencyObject container)
                {
                    continue;
                }

                // 插槽自身不带控件名：它住在 item template 里，宿主名字作用域够不着，只能沿容器向下找。
                if (FindDescendantWithSlotDataContext(container) is { } slotView)
                {
                    parts.Add(new JaliumSlotPart(owner, slotView));
                }
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
        {
            if (!string.IsNullOrWhiteSpace(hostName) && ResolveNamedAncestor(owner, hostName!) is { } namedHost)
            {
                return namedHost;
            }

            var type = hostType ?? typeof(Canvas);
            if (type.IsInstanceOfType(owner))
            {
                return owner;
            }

            foreach (var ancestor in EnumerateVisualAncestors(owner))
            {
                if (ancestor is FrameworkElement element && type.IsInstanceOfType(element))
                {
                    return element;
                }
            }

            return null;
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
                    if (items.ItemContainerGenerator?.ContainerFromIndex(i) is not null)
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
                    if (items.ItemContainerGenerator?.ContainerFromIndex(i) is { } container)
                    {
                        count++;
                        hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container));
                    }
                }
            }

            slotCount = count;
            return hash;
        }
    }

    private sealed class JaliumSlotPart(FrameworkElement nodeView, FrameworkElement element) : ISlotPart
    {
        public ISlotComponent? Slot => element.DataContext as ISlotComponent;

        public bool IsMeasured => element.ActualWidth > 0d && element.ActualHeight > 0d;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var target = relativeTo as FrameworkElement ?? nodeView;

            // TransformToVisual composes the full chain (Viewbox scale + the node's CanvasTransform),
            // unlike TransformToAncestor, which returns only the element's origin. Transforming the
            // local CENTER is what keeps the anchor on the port when the card is scaled.
            var transform = element.TransformToVisual(target);
            if (transform is null)
            {
                return null;
            }

            var center = transform.Transform(new Point(element.ActualWidth / 2d, element.ActualHeight / 2d));
            return new SlotCenter(center.X, center.Y);
        }
    }

    private sealed class JaliumSlotMeasurementHost(FrameworkElement owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.ActualWidth, owner.ActualHeight);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
        {
            owner.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(flush));
            return true;
        }
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

    private static FrameworkElement? FindDescendantWithSlotDataContext(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(parent, i) is not { } child)
            {
                continue;
            }

            if (child is FrameworkElement { DataContext: ISlotComponent } element)
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
