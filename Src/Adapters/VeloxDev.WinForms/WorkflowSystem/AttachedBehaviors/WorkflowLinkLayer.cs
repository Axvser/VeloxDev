using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Draws every visible link in one paint pass on an existing surface control.
/// </summary>
/// <remarks>
/// <para>
/// This is the WinForms counterpart of the MAUI link overlay: instead of one child window per link, the attached
/// control's <c>Paint</c> event walks the tree's visible items and draws the links itself. Publishing each curve to
/// Core keeps the drawn body and the hit-test body the same object.
/// </para>
/// <para>
/// Attach it to the surface canvas (the control the pool hosts nodes in), not to a viewport-sized sibling: a
/// WinForms child window does not composite over its siblings, so a viewport overlay would either cover the grid or
/// hide behind the scroll host. The canvas already paints below the node windows and in canvas-local coordinates.
/// </para>
/// </remarks>
public static class WorkflowLinkLayer
{
    private static readonly ConditionalWeakTable<Control, LayerState> States = new();

    /// <summary>Whether the layer paints links on the control.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowLinkLayer),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The tree whose visible links are drawn; resolved from the surface when unset.</summary>
    public static readonly DependencyProperty WorkflowTreeProperty = DependencyProperty.RegisterAttached(
        "WorkflowTree", typeof(IWorkflowTreeViewModel), typeof(WorkflowLinkLayer),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Colour of a resting link.</summary>
    public static readonly DependencyProperty LineColorProperty = DependencyProperty.RegisterAttached(
        "LineColor", typeof(Color), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF), OnVisualChanged));

    /// <summary>Stroke width of a resting link.</summary>
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.RegisterAttached(
        "Thickness", typeof(float), typeof(WorkflowLinkLayer),
        new PropertyMetadata(2f, OnVisualChanged));

    /// <summary>Colour of the link under the pointer; <see cref="Color.Empty"/> keeps <see cref="LineColor"/>.</summary>
    public static readonly DependencyProperty HoveredLineColorProperty = DependencyProperty.RegisterAttached(
        "HoveredLineColor", typeof(Color), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Color.Empty, OnVisualChanged));

    /// <summary>Stroke width of the link under the pointer; zero keeps <see cref="Thickness"/>.</summary>
    public static readonly DependencyProperty HoveredThicknessProperty = DependencyProperty.RegisterAttached(
        "HoveredThickness", typeof(float), typeof(WorkflowLinkLayer),
        new PropertyMetadata(0f, OnVisualChanged));

    /// <summary>Colour of the virtual (drag preview) link.</summary>
    public static readonly DependencyProperty VirtualLineColorProperty = DependencyProperty.RegisterAttached(
        "VirtualLineColor", typeof(Color), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF), OnVisualChanged));

    /// <summary>Colour of the virtual link over a valid receiver; empty keeps <see cref="VirtualLineColor"/>.</summary>
    public static readonly DependencyProperty VirtualValidTargetColorProperty = DependencyProperty.RegisterAttached(
        "VirtualValidTargetColor", typeof(Color), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Color.Empty, OnVisualChanged));

    /// <summary>Colour of the virtual link over a refused receiver; empty keeps <see cref="VirtualLineColor"/>.</summary>
    public static readonly DependencyProperty VirtualInvalidTargetColorProperty = DependencyProperty.RegisterAttached(
        "VirtualInvalidTargetColor", typeof(Color), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Color.Empty, OnVisualChanged));

    /// <summary>Whether a resting link gets the two faint wider strokes that read as a glow.</summary>
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow", typeof(bool), typeof(WorkflowLinkLayer),
        new PropertyMetadata(false, OnVisualChanged));

    /// <summary>The travelling highlight's head, as a fraction of each link's length.</summary>
    public static readonly DependencyProperty FlowHeadProperty = DependencyProperty.RegisterAttached(
        "FlowHead", typeof(double), typeof(WorkflowLinkLayer),
        new PropertyMetadata(0d, OnVisualChanged));

    /// <summary>How lit the travelling highlight is; zero disables it.</summary>
    public static readonly DependencyProperty FlowIntensityProperty = DependencyProperty.RegisterAttached(
        "FlowIntensity", typeof(double), typeof(WorkflowLinkLayer),
        new PropertyMetadata(0d, OnVisualChanged));

    /// <summary>The least horizontal pull of a control point, in pixels.</summary>
    public static readonly DependencyProperty PullMinimumProperty = DependencyProperty.RegisterAttached(
        "PullMinimum", typeof(double), typeof(WorkflowLinkLayer),
        new PropertyMetadata(40d, OnVisualChanged));

    /// <summary>An explicit surface pan for hosts without a <see cref="WorkflowSurfaceBehavior"/> grid decorator.</summary>
    public static readonly DependencyProperty PanOffsetProperty = DependencyProperty.RegisterAttached(
        "PanOffset", typeof(Point), typeof(WorkflowLinkLayer),
        new PropertyMetadata(Point.Empty, OnVisualChanged));

    /// <summary>An explicit content offset for hosts without a <see cref="WorkflowSurfaceBehavior"/> grid decorator.</summary>
    public static readonly DependencyProperty ContentOffsetProperty = DependencyProperty.RegisterAttached(
        "ContentOffset", typeof(Offset), typeof(WorkflowLinkLayer),
        new PropertyMetadata(new Offset(), OnVisualChanged));

    /// <summary>Re-publishes the visible links' geometry and repaints.</summary>
    /// <remarks>The surface calls this after a pan or zoom changes the projection, so hit-testing never reads last frame's curves.</remarks>
    public static void Refresh(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        GetState(element).RefreshNow();
    }

    /// <summary>Gets whether the layer is active.</summary>
    public static bool GetIsEnabled(Control element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the layer on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(Control element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets the explicit tree, or <see langword="null"/> to resolve the surface's tree.</summary>
    public static IWorkflowTreeViewModel? GetWorkflowTree(Control element)
        => (IWorkflowTreeViewModel?)GetRequired(element, WorkflowTreeProperty);

    /// <summary>Sets the explicit tree.</summary>
    public static void SetWorkflowTree(Control element, IWorkflowTreeViewModel? value)
        => SetRequired(element, WorkflowTreeProperty, value);

    /// <summary>Gets the resting link colour.</summary>
    public static Color GetLineColor(Control element) => (Color)GetRequired(element, LineColorProperty)!;

    /// <summary>Sets the resting link colour.</summary>
    public static void SetLineColor(Control element, Color value) => SetRequired(element, LineColorProperty, value);

    /// <summary>Gets the resting link width.</summary>
    public static float GetThickness(Control element) => (float)GetRequired(element, ThicknessProperty)!;

    /// <summary>Sets the resting link width.</summary>
    public static void SetThickness(Control element, float value) => SetRequired(element, ThicknessProperty, value);

    /// <summary>Gets the hover colour.</summary>
    public static Color GetHoveredLineColor(Control element) => (Color)GetRequired(element, HoveredLineColorProperty)!;

    /// <summary>Sets the hover colour.</summary>
    public static void SetHoveredLineColor(Control element, Color value)
        => SetRequired(element, HoveredLineColorProperty, value);

    /// <summary>Gets the hover width.</summary>
    public static float GetHoveredThickness(Control element) => (float)GetRequired(element, HoveredThicknessProperty)!;

    /// <summary>Sets the hover width.</summary>
    public static void SetHoveredThickness(Control element, float value)
        => SetRequired(element, HoveredThicknessProperty, value);

    /// <summary>Gets the virtual link colour.</summary>
    public static Color GetVirtualLineColor(Control element) => (Color)GetRequired(element, VirtualLineColorProperty)!;

    /// <summary>Sets the virtual link colour.</summary>
    public static void SetVirtualLineColor(Control element, Color value)
        => SetRequired(element, VirtualLineColorProperty, value);

    /// <summary>Gets the valid-target virtual colour.</summary>
    public static Color GetVirtualValidTargetColor(Control element)
        => (Color)GetRequired(element, VirtualValidTargetColorProperty)!;

    /// <summary>Sets the valid-target virtual colour.</summary>
    public static void SetVirtualValidTargetColor(Control element, Color value)
        => SetRequired(element, VirtualValidTargetColorProperty, value);

    /// <summary>Gets the invalid-target virtual colour.</summary>
    public static Color GetVirtualInvalidTargetColor(Control element)
        => (Color)GetRequired(element, VirtualInvalidTargetColorProperty)!;

    /// <summary>Sets the invalid-target virtual colour.</summary>
    public static void SetVirtualInvalidTargetColor(Control element, Color value)
        => SetRequired(element, VirtualInvalidTargetColorProperty, value);

    /// <summary>Gets whether resting links are glowed.</summary>
    public static bool GetGlow(Control element) => (bool)GetRequired(element, GlowProperty)!;

    /// <summary>Sets whether resting links are glowed.</summary>
    public static void SetGlow(Control element, bool value) => SetRequired(element, GlowProperty, value);

    /// <summary>Gets the travelling highlight's head.</summary>
    public static double GetFlowHead(Control element) => (double)GetRequired(element, FlowHeadProperty)!;

    /// <summary>Sets the travelling highlight's head.</summary>
    public static void SetFlowHead(Control element, double value) => SetRequired(element, FlowHeadProperty, value);

    /// <summary>Gets the travelling highlight's intensity.</summary>
    public static double GetFlowIntensity(Control element) => (double)GetRequired(element, FlowIntensityProperty)!;

    /// <summary>Sets the travelling highlight's intensity.</summary>
    public static void SetFlowIntensity(Control element, double value)
        => SetRequired(element, FlowIntensityProperty, value);

    /// <summary>Gets the minimum control-point pull.</summary>
    public static double GetPullMinimum(Control element) => (double)GetRequired(element, PullMinimumProperty)!;

    /// <summary>Sets the minimum control-point pull.</summary>
    public static void SetPullMinimum(Control element, double value) => SetRequired(element, PullMinimumProperty, value);

    /// <summary>Gets the explicit pan used when the control is not inside a workflow surface.</summary>
    public static Point GetPanOffset(Control element) => (Point)GetRequired(element, PanOffsetProperty)!;

    /// <summary>Sets the explicit pan.</summary>
    public static void SetPanOffset(Control element, Point value) => SetRequired(element, PanOffsetProperty, value);

    /// <summary>Gets the explicit content offset used when the control is not inside a workflow surface.</summary>
    public static Offset GetContentOffset(Control element) => (Offset)GetRequired(element, ContentOffsetProperty)!;

    /// <summary>Sets the explicit content offset.</summary>
    public static void SetContentOffset(Control element, Offset value)
        => SetRequired(element, ContentOffsetProperty, value);

    private static object? GetRequired(Control element, DependencyProperty property)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return property.GetValue(element);
    }

    private static void SetRequired(Control element, DependencyProperty property, object? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        property.SetValue(element, value);
    }

    private static void OnIsEnabledChanged(Control target, DependencyPropertyChangedEventArgs e)
        => GetState(target).SetEnabled(e.NewValue is true);

    private static void OnConfigurationChanged(Control target, DependencyPropertyChangedEventArgs e)
        => GetState(target).RefreshNow();

    private static void OnVisualChanged(Control target, DependencyPropertyChangedEventArgs e)
        => GetState(target).Invalidate();

    private static LayerState GetState(Control element)
        => States.GetValue(element, static control => new LayerState(control));

    private sealed class LayerState
    {
        private readonly Control canvas;
        private readonly Dictionary<IWorkflowLinkViewModel, PropertyChangedEventHandler> linkHandlers = [];
        private readonly HashSet<IWorkflowSlotViewModel> slotViews = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IWorkflowNodeViewModel> nodeViews = new(ReferenceEqualityComparer.Instance);
        private readonly Pen pen = new(Color.White);
        private readonly Pen flowPen = new(Color.White);
        private readonly GraphicsPath path = new();
        private readonly List<LinkEntry> entries = [];
        private readonly Dictionary<IWorkflowSlotViewModel, SlotProjection> projections = new(ReferenceEqualityComparer.Instance);
        private bool geometryDirty = true;
        private IWorkflowTreeViewModel? tree;
        private INotifyCollectionChanged? visibleItems;
        private bool enabled;

        public LayerState(Control canvas)
        {
            this.canvas = canvas;
        }

        public void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            if (value)
            {
                canvas.Paint += OnPaint;
                canvas.Disposed += OnDisposed;
                RefreshSubscriptions();
            }
            else
            {
                canvas.Paint -= OnPaint;
                canvas.Disposed -= OnDisposed;
                UnsubscribeAll();
                canvas.Invalidate();
            }
        }

        public void Invalidate()
        {
            if (enabled && !canvas.IsDisposed)
            {
                canvas.Invalidate();
            }
        }

        /// <summary>Re-publishes link geometry now, so hit-testing does not wait for the next paint.</summary>
        public void RefreshNow()
        {
            if (!enabled || canvas.IsDisposed)
            {
                return;
            }

            EnsureTree();
            geometryDirty = true;
            EnsureGeometry();
            Invalidate();
        }

        public void RefreshSubscriptions()
        {
            if (!enabled || canvas.IsDisposed)
            {
                return;
            }

            EnsureTree();
            SyncLinks();
        }

        // 平移每帧都会调 RefreshNow；这里只在树真的换了的时候重建订阅，不按可见集重扫。
        private void EnsureTree()
        {
            var nextTree = ResolveTree();
            if (ReferenceEquals(tree, nextTree))
            {
                return;
            }

            UnsubscribeAll();
            tree = nextTree;
            if (tree is not null)
            {
                visibleItems = tree.GetHelper().VisibleItems;
                visibleItems.CollectionChanged += OnVisibleItemsChanged;
            }

            SyncLinks();
        }

        private void SyncLinks()
        {
            if (tree is null)
            {
                return;
            }

            var live = new HashSet<IWorkflowLinkViewModel>(ReferenceEqualityComparer.Instance);
            foreach (var item in tree.GetHelper().VisibleItems)
            {
                if (item is not IWorkflowLinkViewModel link)
                {
                    continue;
                }

                live.Add(link);
                if (!linkHandlers.ContainsKey(link))
                {
                    PropertyChangedEventHandler handler = (_, _) => Invalidate();
                    linkHandlers.Add(link, handler);
                    if (link is INotifyPropertyChanged notifier)
                    {
                        notifier.PropertyChanged += handler;
                    }

                    SubscribeSlot(link.Sender);
                    SubscribeSlot(link.Receiver);
                    SubscribeNode(link.Sender?.Parent);
                    SubscribeNode(link.Receiver?.Parent);
                }
            }

            var stale = new List<IWorkflowLinkViewModel>();
            foreach (var (link, _) in linkHandlers)
            {
                if (!live.Contains(link))
                {
                    stale.Add(link);
                }
            }

            foreach (var link in stale)
            {
                UnsubscribeLink(link);
            }
        }

        private void SubscribeSlot(IWorkflowSlotViewModel? slot)
        {
            if (slot is null || !slotViews.Add(slot))
            {
                return;
            }

            if (slot is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged += OnAnyPropertyChanged;
            }
        }

        private void UnsubscribeAll()
        {
            projections.Clear();

            if (visibleItems is not null)
            {
                visibleItems.CollectionChanged -= OnVisibleItemsChanged;
                visibleItems = null;
            }

            foreach (var link in new List<IWorkflowLinkViewModel>(linkHandlers.Keys))
            {
                UnsubscribeLink(link);
            }

            foreach (var slot in slotViews)
            {
                if (slot is INotifyPropertyChanged notifier)
                {
                    notifier.PropertyChanged -= OnAnyPropertyChanged;
                }
            }

            foreach (var node in nodeViews)
            {
                if (node is INotifyPropertyChanged notifier)
                {
                    notifier.PropertyChanged -= OnAnyPropertyChanged;
                }
            }

            slotViews.Clear();
            nodeViews.Clear();
            tree = null;
        }

        private void UnsubscribeLink(IWorkflowLinkViewModel link)
        {
            // 离开可见集/换树时撤掉已发布的曲线，别让一条不在画的线还回答命中。
            link.PublishCurve(null);

            if (!linkHandlers.Remove(link, out var handler))
            {
                return;
            }

            if (link is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= handler;
            }

            // 端点订阅按引用共享一份：只在没有其它 link 还引用它时才摘。
            if (link.Sender is not null && !AnyLinkReferences(link.Sender)) UnsubscribeSlot(link.Sender);
            if (link.Receiver is not null && !AnyLinkReferences(link.Receiver)) UnsubscribeSlot(link.Receiver);

            if (link.Sender?.Parent is { } senderNode && !AnyLinkReferencesNode(senderNode)) UnsubscribeNode(senderNode);
            if (link.Receiver?.Parent is { } receiverNode && !AnyLinkReferencesNode(receiverNode)) UnsubscribeNode(receiverNode);
        }

        private bool AnyLinkReferences(IWorkflowSlotViewModel slot)
            => linkHandlers.Keys.Any(link => ReferenceEquals(link.Sender, slot) || ReferenceEquals(link.Receiver, slot));

        private void UnsubscribeSlot(IWorkflowSlotViewModel slot)
        {
            if (!slotViews.Remove(slot)) return;
            if (slot is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnAnyPropertyChanged;
            }
        }

        private void SubscribeNode(IWorkflowNodeViewModel? node)
        {
            if (node is null || !nodeViews.Add(node)) return;
            if (node is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged += OnAnyPropertyChanged;
            }
        }

        private bool AnyLinkReferencesNode(IWorkflowNodeViewModel node)
            => linkHandlers.Keys.Any(link =>
                ReferenceEquals(link.Sender?.Parent, node) || ReferenceEquals(link.Receiver?.Parent, node));

        private void UnsubscribeNode(IWorkflowNodeViewModel node)
        {
            if (!nodeViews.Remove(node)) return;
            if (node is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnAnyPropertyChanged;
            }
        }

        private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            geometryDirty = true;
            Invalidate();
        }

        private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is IWorkflowLinkViewModel link)
                    {
                        link.PublishCurve(null);
                    }
                }
            }

            RefreshSubscriptions();
            geometryDirty = true;
            Invalidate();
        }

        private void OnDisposed(object? sender, EventArgs e)
        {
            SetEnabled(false);
            pen.Dispose();
            flowPen.Dispose();
            path.Dispose();
        }

        private IWorkflowTreeViewModel? ResolveTree()
        {
            var explicitTree = GetWorkflowTree(canvas);
            if (explicitTree is not null)
            {
                return explicitTree;
            }

            for (var current = canvas.Parent; current is not null; current = current.Parent)
            {
                if (WorkflowSurfaceBehavior.GetIsEnabled(current))
                {
                    return WorkflowSurfaceBehavior.GetWorkflowTree(current);
                }
            }

            return null;
        }

        private (int X, int Y) ResolveProjection()
        {
            for (var current = canvas.Parent; current is not null; current = current.Parent)
            {
                if (!WorkflowSurfaceBehavior.GetIsEnabled(current))
                {
                    continue;
                }

                var gridName = WorkflowSurfaceBehavior.GetGridDecoratorName(current);
                if (gridName is not null && FindByName(current, gridName) is IWorkflowGridDecorator grid)
                {
                    return (
                        (int)Math.Round(grid.ContentOffsetX - grid.ScrollOffsetX),
                        (int)Math.Round(grid.ContentOffsetY - grid.ScrollOffsetY));
                }

                break;
            }

            var pan = GetPanOffset(canvas);
            var content = GetContentOffset(canvas);
            return (pan.X + (int)Math.Round(content.Horizontal), pan.Y + (int)Math.Round(content.Vertical));
        }

        private static Control? FindByName(Control root, string name)
        {
            if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
            foreach (Control child in root.Controls)
            {
                var found = FindByName(child, name);
                if (found is not null) return found;
            }

            return null;
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            if (!enabled || tree is null || canvas.IsDisposed)
            {
                return;
            }

            EnsureGeometry();

            var lineColor = GetLineColor(canvas);
            var thickness = Math.Max(0.5f, GetThickness(canvas));
            var hoveredColor = GetHoveredLineColor(canvas);
            var hoveredThickness = GetHoveredThickness(canvas);
            var virtualColor = GetVirtualLineColor(canvas);
            var validColor = GetVirtualValidTargetColor(canvas);
            var invalidColor = GetVirtualInvalidTargetColor(canvas);
            var flowHead = GetFlowHead(canvas);
            var flowIntensity = GetFlowIntensity(canvas);
            var glow = GetGlow(canvas);
            var hovered = WorkflowInput.For(tree).HoveredLink;
            var clip = RectangleF.Inflate(e.ClipRectangle, 32f, 32f);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (var entry in entries)
            {
                if (!entry.Bounds.IntersectsWith(clip))
                {
                    continue;
                }

                var color = lineColor;
                var stroke = thickness;
                if (entry.IsVirtual)
                {
                    color = (entry.Link as IConnectionPreviewLink)?.Preview switch
                    {
                        ConnectionPreviewState.ValidTarget when validColor.A > 0 => validColor,
                        ConnectionPreviewState.InvalidTarget when invalidColor.A > 0 => invalidColor,
                        _ => virtualColor,
                    };
                    pen.DashStyle = DashStyle.Custom;
                    pen.DashPattern = [4f, 2f];
                }
                else
                {
                    pen.DashStyle = DashStyle.Solid;
                    if (ReferenceEquals(entry.Link, hovered))
                    {
                        if (hoveredColor.A > 0) color = hoveredColor;
                        if (hoveredThickness > 0) stroke = hoveredThickness;
                    }
                }

                pen.StartCap = pen.EndCap = LineCap.Round;
                path.Reset();
                path.AddBezier(entry.Points[0], entry.Points[1], entry.Points[2], entry.Points[3]);

                if (glow && !entry.IsVirtual)
                {
                    pen.Color = Color.FromArgb((int)Math.Round(color.A * 0.10), color);
                    pen.Width = stroke + 9;
                    e.Graphics.DrawPath(pen, path);
                    pen.Color = Color.FromArgb((int)Math.Round(color.A * 0.16), color);
                    pen.Width = stroke + 4;
                    e.Graphics.DrawPath(pen, path);
                }

                pen.Color = color;
                pen.Width = stroke;
                e.Graphics.DrawPath(pen, path);

                if (!entry.IsVirtual && flowIntensity > 0.001)
                {
                    DrawComet(e.Graphics, entry.Curve, color, stroke, flowHead, flowIntensity);
                }
            }
        }

        // 几何在每次画之前先落定并发布给 Core：命中、删除与悬停都读这一份，不等下一帧。
        private void EnsureGeometry()
        {
            if (!geometryDirty)
            {
                return;
            }

            geometryDirty = false;
            entries.Clear();

            if (tree is null)
            {
                return;
            }

            var (ox, oy) = ResolveProjection();
            var pull = GetPullMinimum(canvas);

            foreach (var item in tree.GetHelper().VisibleItems)
            {
                if (item is not IWorkflowLinkViewModel link || !link.IsVisible)
                {
                    continue;
                }

                var senderSlot = link.Sender;
                var receiverSlot = link.Receiver;
                if (senderSlot is null || receiverSlot is null)
                {
                    link.PublishCurve(null);
                    continue;
                }

                var isVirtual = senderSlot.Parent is null && receiverSlot.Parent is null;
                if (!isVirtual && !WorkflowSlotUpdateGate.IsLinkRenderReady(link))
                {
                    link.PublishCurve(null);
                    continue;
                }

                var senderModel = ResolveModel(senderSlot, senderSlot.Parent, ox, oy);
                var receiverModel = ResolveModel(receiverSlot, receiverSlot.Parent, ox, oy);
                if (!double.IsFinite(senderModel.X) || !double.IsFinite(senderModel.Y)
                    || !double.IsFinite(receiverModel.X) || !double.IsFinite(receiverModel.Y))
                {
                    link.PublishCurve(null);
                    continue;
                }

                var points = LinkCurve.LinkCurvePoints(
                    link, senderModel.X, senderModel.Y, receiverModel.X, receiverModel.Y, pull);

                var local = new PointF[points.Length];
                for (var i = 0; i < points.Length; i++)
                {
                    local[i] = new PointF((float)(points[i].X + ox), (float)(points[i].Y + oy));
                }

                var curve = LinkCurve.FromCubic(
                    local[0].X, local[0].Y, local[1].X, local[1].Y,
                    local[2].X, local[2].Y, local[3].X, local[3].Y);
                link.PublishCurve(curve);
                entries.Add(new LinkEntry(link, local, curve, isVirtual, Bounds(local)));
            }
        }

        private readonly record struct LinkEntry(
            IWorkflowLinkViewModel Link,
            PointF[] Points,
            LinkCurve Curve,
            bool IsVirtual,
            RectangleF Bounds);

        private readonly record struct SlotProjection(
            double CanvasX, double CanvasY, double LocalX, double LocalY,
            double AbsoluteX, double AbsoluteY, bool HasNode);

        // 端点模型坐标的缓存。slot.Anchor 是画布局部坐标，平移时它会跟着量；但节点被移出
        // 可见集时测量会停住，而画布偏移还在变。若每次都用「当前锚点 − 当前偏移」，模型端点
        // 会被平移量拖走，PortOutward 在边上就会翻向 —— 表现是曲线弯折方向来回摆。
        // 有父节点时缓存的是「端点在节点内的局部偏移」，模型坐标 = 当前 node.Anchor + 局部偏移；
        // 无父节点的占位端点才缓存绝对模型坐标。锚点没变就不重算，节点平移仍会跟随。
        private (double X, double Y) ResolveModel(
            IWorkflowSlotViewModel slot, IWorkflowNodeViewModel? node, double ox, double oy)
        {
            // 首选节点内的局部位置：它只随布局/缩放变，和画布平移无关，也不受测量滞后影响。
            if (node is not null && WorkflowSlotLayoutBehavior.TryGetLocalAnchor(slot, out var lx, out var ly))
            {
                return (node.Anchor.Horizontal + lx, node.Anchor.Vertical + ly);
            }

            var anchor = slot.Anchor;
            if (!projections.TryGetValue(slot, out var projection)
                || projection.CanvasX != anchor.Horizontal
                || projection.CanvasY != anchor.Vertical)
            {
                projection = node is null
                    ? new SlotProjection(
                        anchor.Horizontal, anchor.Vertical, 0d, 0d,
                        anchor.Horizontal - ox, anchor.Vertical - oy, HasNode: false)
                    : new SlotProjection(
                        anchor.Horizontal, anchor.Vertical,
                        anchor.Horizontal - (node.Anchor.Horizontal + ox),
                        anchor.Vertical - (node.Anchor.Vertical + oy),
                        0d, 0d, HasNode: true);
                projections[slot] = projection;
            }

            return projection.HasNode && node is not null
                ? (node.Anchor.Horizontal + projection.LocalX, node.Anchor.Vertical + projection.LocalY)
                : (projection.AbsoluteX, projection.AbsoluteY);
        }

        private const double TailFraction = 0.30;
        private const int TailSegments = 16;

        private void DrawComet(Graphics g, LinkCurve curve, Color color, float thickness, double headFraction, double intensity)
        {
            if (curve.Length <= 0) return;

            var head = Math.Clamp(headFraction, 0, 1) * curve.Length;
            var tail = TailFraction * curve.Length;

            for (var pass = 0; pass < 2; pass++)
            {
                var bloom = pass == 0;
                for (var k = 0; k < TailSegments; k++)
                {
                    var f0 = k / (double)TailSegments;
                    var f1 = (k + 1) / (double)TailSegments;
                    var l0 = head - (tail * (1 - f0));
                    var l1 = head - (tail * (1 - f1));
                    if (l1 <= 0 || l0 >= curve.Length) continue;

                    var alpha = intensity * f0 * f0;
                    if (alpha <= 0.004) continue;

                    var blended = Mix(color, Color.White, f0);
                    var a = (int)Math.Round(Math.Clamp(bloom ? alpha * 0.22 : alpha, 0, 1) * 255);
                    flowPen.Color = Color.FromArgb(a, blended.R, blended.G, blended.B);
                    flowPen.Width = bloom ? thickness + 9 : (float)(thickness * (0.45 + (0.95 * f0)));
                    flowPen.StartCap = flowPen.EndCap = LineCap.Round;
                    DrawRun(g, curve, l0, l1);
                }
            }
        }

        private void DrawRun(Graphics g, LinkCurve curve, double from, double to)
        {
            from = Math.Clamp(from, 0, curve.Length);
            to = Math.Min(to, curve.Length);
            if (to <= from) return;

            var previous = PointAt(curve, from);
            var drew = false;
            for (var i = 0; i < curve.Count; i++)
            {
                var length = curve.LengthAt(i);
                if (length <= from || length >= to) continue;

                var point = new PointF((float)curve.XAt(i), (float)curve.YAt(i));
                if (drew) g.DrawLine(flowPen, previous, point);
                previous = point;
                drew = true;
            }

            var last = PointAt(curve, to);
            if (drew) g.DrawLine(flowPen, previous, last);
        }

        private static PointF PointAt(LinkCurve curve, double length)
        {
            var (x, y) = curve.PointAtLength(length);
            return new PointF((float)x, (float)y);
        }

        private static Color Mix(Color from, Color to, double t)
        {
            byte Lerp(byte a, byte b) => (byte)Math.Round(a + ((b - a) * t));
            return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
        }

        private static RectangleF Bounds(PointF[] points)
        {
            var left = points[0].X;
            var top = points[0].Y;
            var right = left;
            var bottom = top;
            for (var i = 1; i < points.Length; i++)
            {
                left = Math.Min(left, points[i].X);
                top = Math.Min(top, points[i].Y);
                right = Math.Max(right, points[i].X);
                bottom = Math.Max(bottom, points[i].Y);
            }

            return RectangleF.FromLTRB(left, top, right, bottom);
        }
    }

    private sealed class ReferenceEqualityComparer
        : IEqualityComparer<IWorkflowLinkViewModel>,
          IEqualityComparer<IWorkflowSlotViewModel>,
          IEqualityComparer<IWorkflowNodeViewModel>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IWorkflowLinkViewModel? x, IWorkflowLinkViewModel? y) => ReferenceEquals(x, y);

        public int GetHashCode(IWorkflowLinkViewModel obj) => RuntimeHelpers.GetHashCode(obj);

        public bool Equals(IWorkflowSlotViewModel? x, IWorkflowSlotViewModel? y) => ReferenceEquals(x, y);

        public int GetHashCode(IWorkflowSlotViewModel obj) => RuntimeHelpers.GetHashCode(obj);

        public bool Equals(IWorkflowNodeViewModel? x, IWorkflowNodeViewModel? y) => ReferenceEquals(x, y);

        public int GetHashCode(IWorkflowNodeViewModel obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
