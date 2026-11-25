using System.ComponentModel;
using System.Runtime.CompilerServices;
using Easel;
using Easel.Visuals;
using VisualCanvas = Easel.Visuals.Controls.Canvas;
using VisualItemsControl = Easel.Visuals.Controls.ItemsControl;
using DrawingPointF = System.Drawing.PointF;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WPF-shaped attached behavior that measures the node template's declared slot parts and writes their
/// world-space anchors back to the model.
/// </summary>
/// <remarks>
/// Attach it to the node view root and name the parts with <see cref="SlotNamesProperty"/> and
/// <see cref="SlotEnumeratorNamesProperty"/> (comma-separated <c>x:Name</c>s). The behavior owns only the platform
/// half — the visual-tree walk, the <see cref="VisualElement.PointToSurface"/> / <see cref="VisualElement.PointFromSurface"/>
/// transform, and the UI-thread hand-off; which parts to measure, their order, duplicate suppression and the
/// re-measure schedule belong to <see cref="SlotLayoutPipelineCore"/>.
/// </remarks>
public sealed class WorkflowSlotLayoutBehavior
{
    /// <summary>Whether the behavior measures the declared slot parts.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowSlotLayoutBehavior),
        // 视觉元素不是 Control：只有 VisualPropertyChangedCallback 会在这里触发，Ensure 因此能按属性变化安装。
        new PropertyMetadata(false) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    /// <summary>Comma-separated names of visual elements whose data context is one slot.</summary>
    public static readonly DependencyProperty SlotNamesProperty = DependencyProperty.RegisterAttached(
        "SlotNames", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    /// <summary>Comma-separated names of visual items controls whose realized items are slots.</summary>
    public static readonly DependencyProperty SlotEnumeratorNamesProperty = DependencyProperty.RegisterAttached(
        "SlotEnumeratorNames", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    /// <summary>Name of the element that supplies the coordinate space for slot anchors.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName", typeof(string), typeof(WorkflowSlotLayoutBehavior),
        new PropertyMetadata(null) { VisualPropertyChangedCallback = OnVisualPropertyChanged });

    private static readonly ConditionalWeakTable<VisualElement, SlotState> States = new();

    private WorkflowSlotLayoutBehavior()
    {
    }

    // 视觉元素的附着属性写值只走这一条回调；Ensure 本身幂等，重复调用不会重复接线。
    private static void OnVisualPropertyChanged(IWinformsObject target, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        if (target is VisualElement element)
        {
            Ensure(element);
        }
    }

    /// <summary>Reads the layout switch.</summary>
    public static bool GetIsEnabled(VisualElement element) => (bool)element.GetValue(IsEnabledProperty)!;

    /// <summary>Writes the layout switch.</summary>
    public static void SetIsEnabled(VisualElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the named slot parts.</summary>
    public static string? GetSlotNames(VisualElement element) => (string?)element.GetValue(SlotNamesProperty);

    /// <summary>Writes the named slot parts.</summary>
    public static void SetSlotNames(VisualElement element, string? value) => element.SetValue(SlotNamesProperty, value);

    /// <summary>Reads the named slot enumerator parts.</summary>
    public static string? GetSlotEnumeratorNames(VisualElement element)
        => (string?)element.GetValue(SlotEnumeratorNamesProperty);

    /// <summary>Writes the named slot enumerator parts.</summary>
    public static void SetSlotEnumeratorNames(VisualElement element, string? value)
        => element.SetValue(SlotEnumeratorNamesProperty, value);

    /// <summary>Reads the coordinate-host name.</summary>
    public static string? GetCoordinateHostName(VisualElement element)
        => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Writes the coordinate-host name.</summary>
    public static void SetCoordinateHostName(VisualElement element, string? value)
        => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Attaches or detaches the behavior according to the element's current attached values.</summary>
    public static void Ensure(VisualElement element)
    {
        var enabled = element.IsSet(IsEnabledProperty) && GetIsEnabled(element);
        if (enabled)
        {
            Attach(element);
        }
        else
        {
            Detach(element);
        }
    }

    private static void Attach(VisualElement element)
    {
        var state = States.GetValue(element, static owner => new SlotState(owner));
        state.Subscribe();
    }

    private static void Detach(VisualElement element)
    {
        if (States.TryGetValue(element, out var state))
        {
            state.Unsubscribe();
        }
    }

    private sealed class SlotState
    {
        private readonly VisualElement owner;
        private readonly List<VisualItemsControl> enumerators = [];
        private VisualHost? surface;
        private SlotLayoutPipelineCore? pipeline;
        private bool subscribed;

        public SlotState(VisualElement owner)
        {
            this.owner = owner;
        }

        public void Subscribe()
        {
            if (!subscribed)
            {
                owner.PropertyChanged += OnOwnerPropertyChanged;
                subscribed = true;
            }

            HookSurface();
            EnsureEnumerators();
            EnsurePipeline();
        }

        public void Unsubscribe()
        {
            if (subscribed)
            {
                owner.PropertyChanged -= OnOwnerPropertyChanged;
                subscribed = false;
            }

            foreach (var enumerator in enumerators)
            {
                enumerator.ItemsRebuilt -= OnItemsRebuilt;
            }

            enumerators.Clear();
            if (surface is not null)
            {
                surface.LayoutUpdated -= OnLayoutUpdated;
                surface = null;
            }

            StopPipeline();
        }

        private void HookSurface()
        {
            var next = owner.Surface;
            if (ReferenceEquals(next, surface))
            {
                return;
            }

            if (surface is not null)
            {
                surface.LayoutUpdated -= OnLayoutUpdated;
            }

            surface = next;
            if (surface is not null)
            {
                surface.LayoutUpdated += OnLayoutUpdated;
            }
        }

        private void EnsureEnumerators()
        {
            foreach (var name in ParseNames(GetSlotEnumeratorNames(owner)))
            {
                if (FindNamed(owner, name) is VisualItemsControl items && !enumerators.Contains(items))
                {
                    enumerators.Add(items);
                    items.ItemsRebuilt += OnItemsRebuilt;
                }
            }
        }

        // 数据上下文或部件名换了就换一条流水线：配置在构造时固化成 WatchedPropertyNames。
        private void EnsurePipeline()
        {
            if (owner.DataContext is not INodeComponent node)
            {
                StopPipeline();
                return;
            }

            if (pipeline is null || !ReferenceEquals(pipeline.Node, node))
            {
                StopPipeline();
                pipeline = new SlotLayoutPipelineCore(
                    node,
                    owner,
                    new SlotPartLocator(owner),
                    new SlotMeasurementHost(owner),
                    new SlotLayoutConfiguration(
                        GetSlotNames(owner),
                        GetSlotEnumeratorNames(owner),
                        GetCoordinateHostName(owner),
                        null));
            }
        }

        private void RebuildPipeline()
        {
            StopPipeline();
            EnsurePipeline();
        }

        private void StopPipeline()
        {
            pipeline?.Stop();
            pipeline = null;
        }

        private void Measure(SlotLayoutTrigger trigger)
        {
            EnsurePipeline();
            pipeline?.RequestMeasure(trigger);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            HookSurface();
            EnsureEnumerators();

            // 布局后同步回写：在渲染前读插槽中心，保证连线端点锚点与节点折叠同帧刷新。
            // 流水线先用指纹挡掉「输入没变」的节点，再进共享批次同步执行。
            Measure(SlotLayoutTrigger.Layout);
        }

        private void OnOwnerPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (ReferenceEquals(e.Property, VisualElement.DataContextProperty))
            {
                RebuildPipeline();
                Measure(SlotLayoutTrigger.Configuration);
                return;
            }

            if (ReferenceEquals(e.Property, SlotNamesProperty)
                || ReferenceEquals(e.Property, SlotEnumeratorNamesProperty)
                || ReferenceEquals(e.Property, CoordinateHostNameProperty))
            {
                RebuildPipeline();
                Measure(SlotLayoutTrigger.Configuration);
                return;
            }

            if (ReferenceEquals(e.Property, VisualElement.WidthProperty)
                || ReferenceEquals(e.Property, VisualElement.HeightProperty))
            {
                owner.InvalidateMeasure();
            }
        }

        private void OnItemsRebuilt(object? sender, EventArgs e) => Measure(SlotLayoutTrigger.Configuration);
    }

    // ── 平台层：可视树遍历与坐标转换 ────────────────────────────────────────

    private sealed class SlotPartLocator(VisualElement owner) : ISlotPartLocator, ISlotPartIdentityLocator
    {
        // PointFromSurface 到画布会逆掉相机矩阵，所以量到的中心已是画布局部坐标。
        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.CanvasLocal;

        public ISlotPart? FindNamedSlot(string name)
            => FindNamed(owner, name) is { } element ? new SlotPart(owner, element) : null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
        {
            if (FindNamed(owner, name) is not VisualItemsControl items)
            {
                return Array.Empty<ISlotPart>();
            }

            var parts = new List<ISlotPart>();
            foreach (var element in EnumerateVisuals(items))
            {
                if (element.DataContext is ISlotComponent)
                {
                    parts.Add(new SlotPart(owner, element));
                }
            }

            return parts;
        }

        public object? ResolveCoordinateHost(string? hostName, Type? hostType)
        {
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                for (var current = owner.ParentElement; current is not null; current = current.ParentElement)
                {
                    if (string.Equals(current.Name, hostName, StringComparison.Ordinal))
                    {
                        return current;
                    }
                }
            }

            var type = hostType ?? typeof(VisualCanvas);
            if (type.IsAssignableFrom(owner.GetType()))
            {
                return owner;
            }

            for (var current = owner.ParentElement; current is not null; current = current.ParentElement)
            {
                if (type.IsAssignableFrom(current.GetType()))
                {
                    return current;
                }
            }

            return owner.Surface?.Root as VisualElement;
        }

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
        {
            // 指纹里的插槽数：容器增删、模板重建必须重测，哪怕节点外框尺寸没变。
            var count = 0;
            foreach (var name in slotNames)
            {
                if (FindNamed(owner, name) is not null)
                {
                    count++;
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (FindNamed(owner, name) is not VisualItemsControl items)
                {
                    continue;
                }

                foreach (var element in EnumerateVisuals(items))
                {
                    if (element.DataContext is ISlotComponent)
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
                if (FindNamed(owner, name) is { } element)
                {
                    count++;
                    hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element));
                }
            }

            foreach (var name in enumeratorNames)
            {
                if (FindNamed(owner, name) is not VisualItemsControl items)
                {
                    continue;
                }

                foreach (var element in EnumerateVisuals(items))
                {
                    if (element.DataContext is ISlotComponent)
                    {
                        count++;
                        hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element));
                    }
                }
            }

            slotCount = count;
            return hash;
        }
    }

    private sealed class SlotPart(VisualElement nodeView, VisualElement element) : ISlotPart, ISlotPartVisibility
    {
        public ISlotComponent? Slot => element.DataContext as ISlotComponent;

        public bool IsMeasured => element.Bounds.Width > 0f && element.Bounds.Height > 0f;

        public bool IsVisible
            => element.Visibility == Easel.Controls.Visibility.Visible
               && nodeView.Visibility == Easel.Controls.Visibility.Visible;

        public SlotCenter? MeasureCenter(object? relativeTo)
        {
            var center = new DrawingPointF(element.Bounds.Width / 2f, element.Bounds.Height / 2f);
            var surfacePoint = element.PointToSurface(center);
            var host = relativeTo as VisualElement ?? nodeView;
            var local = host.PointFromSurface(surfacePoint);
            return new SlotCenter(local.X, local.Y);
        }
    }

    private sealed class SlotMeasurementHost(VisualElement owner) : ISlotMeasurementHost
    {
        public (double Width, double Height) ReadNodeSize() => (owner.Bounds.Width, owner.Bounds.Height);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor) => slot.Anchor = anchor;

        public void InvalidateMeasure() => owner.InvalidateMeasure();

        public bool Post(Action flush)
        {
            // 投递必须异步：平台若同步执行，批次在调用返回前就被清空，请求永远合并不了。
            var surface = owner.Surface;
            if (surface is { IsHandleCreated: true, IsDisposed: false })
            {
                try
                {
                    surface.BeginInvoke(flush);
                    return true;
                }
                catch (InvalidOperationException)
                {
                }
            }

            return false;
        }
    }

    private static string[] ParseNames(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static name => name.Trim())
                .Where(static name => name.Length > 0)
                .ToArray();

    private static VisualElement? FindNamed(VisualElement root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            if (child is VisualElement element && FindNamed(element, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<VisualElement> EnumerateVisuals(VisualElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            if (child is VisualElement element)
            {
                foreach (var nested in EnumerateVisuals(element))
                {
                    yield return nested;
                }
            }
        }
    }
}
