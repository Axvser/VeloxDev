using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Avalonia attached behavior that measures a node template's declared slot parts and writes their canvas-local
/// anchors back to the model.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to the node view root and name the parts with <see cref="SlotNamesProperty"/> and
/// <see cref="SlotEnumeratorNamesProperty"/> (comma-separated names). The behavior owns only the platform half —
/// the visual-tree walk, the <c>TranslatePoint</c> transform, and the UI-thread hand-off; which parts to measure,
/// their order, duplicate suppression and the re-measure schedule belong to <see cref="SlotLayoutPipelineCore"/>.
/// </para>
/// <para>
/// Avalonia's <c>TranslatePoint</c> into the coordinate host includes the canvas translation, so the locator
/// reports <see cref="SlotCoordinateSpace.ScrollSpace"/> and the pipeline subtracts
/// <see cref="CanvasLayout.ActualOffset"/>.
/// </para>
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior : AvaloniaObject
{
    private sealed class LayoutState
    {
        public SlotLayoutPipelineCore? Pipeline { get; set; }
        public INotifyPropertyChanged? PropertyChangedSource { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
    }

    /// <summary>Whether the behavior measures the node view's declared slot parts.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, bool>("IsEnabled");

    /// <summary>Comma-separated names of visual elements that each render one slot.</summary>
    public static readonly AttachedProperty<string?> SlotNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotNames");

    /// <summary>Comma-separated names of visual items controls whose realized items render slots.</summary>
    public static readonly AttachedProperty<string?> SlotEnumeratorNamesProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("SlotEnumeratorNames");

    /// <summary>Name of the element that supplies the coordinate space for slot anchors.</summary>
    public static readonly AttachedProperty<string?> CoordinateHostNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, string?>("CoordinateHostName");

    /// <summary>Element type used when the coordinate-host name does not resolve; defaults to <see cref="Canvas"/>.</summary>
    public static readonly AttachedProperty<Type?> CoordinateHostTypeProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, Type?>("CoordinateHostType");

    private static readonly AttachedProperty<LayoutState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSlotLayoutBehavior, UserControl, LayoutState?>("State");

    static WorkflowSlotLayoutBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<UserControl>(OnIsEnabledChanged);
        SlotNamesProperty.Changed.AddClassHandler<UserControl>(OnConfigurationChanged);
        SlotEnumeratorNamesProperty.Changed.AddClassHandler<UserControl>(OnConfigurationChanged);
        CoordinateHostNameProperty.Changed.AddClassHandler<UserControl>(OnConfigurationChanged);
        CoordinateHostTypeProperty.Changed.AddClassHandler<UserControl>(OnConfigurationChanged);
    }

    /// <summary>Reads whether slot measuring is enabled on <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether slot measuring is enabled on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the named single-slot parts from <paramref name="element"/>.</summary>
    public static string? GetSlotNames(AvaloniaObject element) => element.GetValue(SlotNamesProperty);

    /// <summary>Sets the named single-slot parts on <paramref name="element"/>.</summary>
    public static void SetSlotNames(AvaloniaObject element, string? value) => element.SetValue(SlotNamesProperty, value);

    /// <summary>Reads the named slot enumerator parts from <paramref name="element"/>.</summary>
    public static string? GetSlotEnumeratorNames(AvaloniaObject element) => element.GetValue(SlotEnumeratorNamesProperty);

    /// <summary>Sets the named slot enumerator parts on <paramref name="element"/>.</summary>
    public static void SetSlotEnumeratorNames(AvaloniaObject element, string? value) => element.SetValue(SlotEnumeratorNamesProperty, value);

    /// <summary>Reads the coordinate-host name from <paramref name="element"/>.</summary>
    public static string? GetCoordinateHostName(AvaloniaObject element) => element.GetValue(CoordinateHostNameProperty);

    /// <summary>Sets the coordinate-host name on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostName(AvaloniaObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Reads the coordinate-host type from <paramref name="element"/>.</summary>
    public static Type? GetCoordinateHostType(AvaloniaObject element) => element.GetValue(CoordinateHostTypeProperty);

    /// <summary>Sets the coordinate-host type on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostType(AvaloniaObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    /// <summary>Re-measures a node view after the canvas zoom or scroll transform changed.</summary>
    /// <remarks>
    /// A transform-only change leaves the stamp's model geometry and measured size untouched, so the layout trigger
    /// cannot see it; a zoom path calls this to force the pass.
    /// </remarks>
    public static void Refresh(UserControl control)
    {
        if (control.GetValue(StateProperty) is { Pipeline: { } pipeline })
        {
            pipeline.RequestMeasure(SlotLayoutTrigger.Viewport);
        }
    }

    private static void OnIsEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void OnConfigurationChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not LayoutState)
            return;

        // 配置换了就得换一条流水线：部件名集合在构造时固化成 WatchedPropertyNames。
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Configuration);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        control.SetValue(StateProperty, new LayoutState());
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        control.DataContextChanged += OnDataContextChanged;
        control.LayoutUpdated += OnLayoutUpdated;
        control.PropertyChanged += OnControlPropertyChanged;
        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void Detach(UserControl control)
    {
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        control.DataContextChanged -= OnDataContextChanged;
        control.LayoutUpdated -= OnLayoutUpdated;
        control.PropertyChanged -= OnControlPropertyChanged;

        if (control.GetValue(StateProperty) is LayoutState state)
        {
            UnsubscribeNode(state);
            state.Pipeline?.Stop();
            state.Pipeline = null;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is LayoutState state)
        {
            // 卸载只丢排队工作，流水线留着：重载回来时同一份配置直接复用。
            state.Pipeline?.CancelPending();
        }
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not UserControl control)
            return;

        UpdatePropertyChangedSubscription(control);
        RebuildPipeline(control);
        Measure(control, SlotLayoutTrigger.Attachment);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // 布局后同步回写：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新。
        // 整棵树每次布局都会走到这里，所以交给流水线先用指纹挡掉「输入没变」的节点，再进共享批次同步执行。
        if (sender is UserControl control)
            Measure(control, SlotLayoutTrigger.Layout);
    }

    // Avalonia 的 Visual.Bounds 是 AvaloniaProperty，直接挂在属性变化上就不必等 SizeChanged 事件。
    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is UserControl control && e.Property == Visual.BoundsProperty)
            Measure(control, SlotLayoutTrigger.Layout);
    }

    private static void OnNodePropertyChanged(UserControl control, PropertyChangedEventArgs e)
    {
        if (control.GetValue(StateProperty) is not LayoutState { Pipeline: { } pipeline })
            return;

        // 只看与插槽位置有关的属性名：标题、颜色这类变更不该重跑一遍测量。
        if (e.PropertyName is not null && !pipeline.WatchedPropertyNames.Contains(e.PropertyName))
            return;

        pipeline.RequestMeasure(SlotLayoutTrigger.Model);
    }

    private static void UpdatePropertyChangedSubscription(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
            return;

        UnsubscribeNode(state);

        if (control.DataContext is INotifyPropertyChanged notify)
        {
            state.PropertyChangedSource = notify;
            state.PropertyChangedHandler = (_, e) => OnNodePropertyChanged(control, e);
            notify.PropertyChanged += state.PropertyChangedHandler;
        }
    }

    private static void UnsubscribeNode(LayoutState state)
    {
        if (state.PropertyChangedSource is not null && state.PropertyChangedHandler is not null)
            state.PropertyChangedSource.PropertyChanged -= state.PropertyChangedHandler;

        state.PropertyChangedSource = null;
        state.PropertyChangedHandler = null;
    }

    private static void RebuildPipeline(UserControl control)
    {
        if (control.GetValue(StateProperty) is not LayoutState state)
            return;

        state.Pipeline?.Stop();
        state.Pipeline = null;

        if (control.DataContext is not INodeComponent node)
            return;

        var configuration = new SlotLayoutConfiguration(
            GetSlotNames(control),
            GetSlotEnumeratorNames(control),
            GetCoordinateHostName(control),
            GetCoordinateHostType(control));

        state.Pipeline = new SlotLayoutPipelineCore(
            node,
            control,
            new AvaloniaSlotPartLocator(control),
            new AvaloniaSlotMeasurementHost(control),
            configuration);
    }

    private static void Measure(UserControl control, SlotLayoutTrigger trigger)
    {
        if (control.GetValue(StateProperty) is { Pipeline: { } pipeline })
            pipeline.RequestMeasure(trigger);
    }

    // ── 平台层：可视树遍历与坐标转换 ────────────────────────────────────────

    private sealed class AvaloniaSlotPartLocator(UserControl owner) : ISlotPartLocator, ISlotPartIdentityLocator
    {
        // TranslatePoint 到坐标宿主得到的是「world + ActualOffset」，所以由流水线减掉布局偏移。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.ScrollSpace;

        public ISlotPart? FindNamedSlot(string name)
            => FindNamed<Control>(owner, name) is { } element ? new AvaloniaSlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (FindNamed<ItemsControl>(owner, name) is not { } items || items.ItemCount == 0)
                return Array.Empty<ISlotPart>();

            var parts = new List<ISlotPart>(items.ItemCount);
            for (var i = 0; i < items.ItemCount; i++)
            {
                if (items.ContainerFromIndex(i) is not { } container)
                    continue;

                // 插槽自身不带控件名：它住在 item template 里，宿主名字作用域够不着，只能沿容器向下找。
                if (FindDescendantWithSlotDataContext(container) is { } slotView)
                    parts.Add(new AvaloniaSlotPart(owner, slotView));
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
        {
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                var namedHost = ResolveNamedHost(owner, hostName!);
                if (namedHost is not null)
                    return namedHost;
            }

            var type = hostType ?? typeof(Canvas);
            if (type.IsAssignableFrom(owner.GetType()))
                return owner;

            return EnumerateVisualAncestors(owner)
                .OfType<Control>()
                .FirstOrDefault(x => type.IsAssignableFrom(x.GetType()));
        }

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            // 指纹里的插槽数：容器增删、模板重建必须重测，哪怕节点外框尺寸没变。
            // 枚举项只数**已实例化的容器**，与 FindEnumeratedSlots 同一口径：数逻辑项会得到一个「看着完整、
            // 实际一个容器都没生成」的指纹，这一趟就会被当成测过而不再重试。
            var count = 0;

            foreach (var name in slotNames)
            {
                if (FindNamed<Control>(owner, name) is not null)
                    count++;
            }

            foreach (var name in enumeratorNames)
            {
                if (FindNamed<ItemsControl>(owner, name) is not { } items)
                    continue;

                for (var i = 0; i < items.ItemCount; i++)
                {
                    if (items.ContainerFromIndex(i) is not null)
                        count++;
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
                if (FindNamed<Control>(owner, name) is { } element)
                {
                    count++;
                    hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element));
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (FindNamed<ItemsControl>(owner, name) is not { } items)
                {
                    continue;
                }

                for (var i = 0; i < items.ItemCount; i++)
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
    }

    private sealed class AvaloniaSlotPart(Visual nodeView, Control element) : ISlotPart
    {
        public ISlotComponent? Slot => element.DataContext as ISlotComponent;

        public bool IsMeasured => element.Bounds.Width > 0d && element.Bounds.Height > 0d;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var target = relativeTo as Visual ?? nodeView;
            var center = element.TranslatePoint(
                new Point(element.Bounds.Width / 2d, element.Bounds.Height / 2d), target);
            return center is { } point ? new SlotCenter(point.X, point.Y) : null;
        }
    }

    private sealed class AvaloniaSlotMeasurementHost(UserControl owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.Bounds.Width, owner.Bounds.Height);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
        {
            Dispatcher.UIThread.Post(flush, DispatcherPriority.Render);
            return true;
        }
    }

    private static Control? ResolveNamedHost(Control control, string? hostName)
    {
        if (hostName is null)
            return null;

        if (control.Name == hostName)
            return control;

        return control.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(x => x.Name == hostName);
    }

    // 与 WorkflowSurfaceBehavior 同因：XAML 载入期间名字域可能还没建立，null 是「还没好」而不是失败。
    private static T? FindNamed<T>(Control owner, string name) where T : Control
    {
        try
        {
            return owner.FindControl<T>(name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Control? FindDescendantWithSlotDataContext(Visual parent)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control { DataContext: ISlotComponent } element)
                return element;

            if (FindDescendantWithSlotDataContext(child) is { } descendant)
                return descendant;
        }

        return null;
    }

    private static IEnumerable<Visual> EnumerateVisualAncestors(Visual source)
        => source.GetVisualAncestors();
}
