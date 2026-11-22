using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// The visual canvas a <see cref="VisualWorkflowSurface"/> draws its workflow on: it positions node views from the
/// model's collapsed anchors, owns the single link layer, and synchronises the measured slot anchors.
/// </summary>
/// <remarks>
/// Pan is kept as a plain offset on this element instead of using <see cref="Visual.Transform"/>: node anchors are
/// already collapsed by <see cref="CanvasLayout.Scale"/>, so adding the pan when a card is placed keeps model and
/// screen space one subtraction apart. The link layer draws and publishes curves in the same model space, and only
/// adds the pan when it paints.
/// </remarks>
public sealed class VisualWorkflowCanvas : VisualCanvas
{
    private readonly VisualWorkflowLinkLayer _linkLayer;
    private readonly List<VisualElement> _nodeViews = [];
    private readonly Dictionary<INodeComponent, VisualElement> _viewsByNode = [];
    private DrawingPointF _pan;
    private bool _syncing;
    private INotifyPropertyChanged? _layoutNotifier;

    /// <summary>Creates an empty canvas with its single link layer as the first child.</summary>
    public VisualWorkflowCanvas()
        : this(null)
    {
    }

    internal VisualWorkflowCanvas(VisualWorkflowSurface? owner)
    {
        Owner = owner;
        IsHitTestVisible = true;
        _linkLayer = new VisualWorkflowLinkLayer(this);
        Add(_linkLayer);
    }

    internal VisualWorkflowSurface? Owner { get; }

    /// <summary>The single layer that paints every visible link.</summary>
    public VisualWorkflowLinkLayer LinkLayer => _linkLayer;

    /// <summary>The tree currently shown by this canvas, or <see langword="null"/>.</summary>
    public ITreeComponent? Tree { get; private set; }

    /// <summary>The node views currently materialised on this canvas.</summary>
    public IReadOnlyList<VisualElement> NodeViews => _nodeViews;

    /// <summary>The signed pan translation applied to every node view and link curve, in pixels.</summary>
    public DrawingPointF Pan => _pan;

    /// <summary>Converts a surface point into the canvas-local (collapsed model) space the tree uses.</summary>
    /// <param name="surfacePoint">A point in the hosting surface's client coordinates.</param>
    public Wf.Anchor SurfaceToModel(DrawingPointF surfacePoint)
    {
        var origin = AbsoluteBounds.Location;
        return new Wf.Anchor(
            surfacePoint.X - origin.X - _pan.X,
            surfacePoint.Y - origin.Y - _pan.Y,
            0);
    }

    /// <summary>Converts a canvas-local (collapsed model) point into the hosting surface's client coordinates.</summary>
    /// <param name="modelX">Horizontal canvas-local coordinate.</param>
    /// <param name="modelY">Vertical canvas-local coordinate.</param>
    public DrawingPointF ModelToSurface(double modelX, double modelY)
    {
        var origin = AbsoluteBounds.Location;
        return new DrawingPointF(
            (float)(modelX + _pan.X + origin.X),
            (float)(modelY + _pan.Y + origin.Y));
    }

    /// <summary>Returns the view bound to <paramref name="node"/>, or <see langword="null"/>.</summary>
    /// <param name="node">The node to look up.</param>
    public VisualElement? FindView(INodeComponent node)
        => _viewsByNode.TryGetValue(node, out var view) ? view : null;

    /// <summary>Moves the pan offset and re-places every node view.</summary>
    /// <param name="pan">The new signed pan translation, in pixels.</param>
    public void SetPan(DrawingPointF pan)
    {
        if (_pan == pan)
        {
            return;
        }

        _pan = pan;
        _linkLayer.Pan = pan;
        RepositionViews();
        InvalidateArrange();
    }

    /// <summary>Zooms around <paramref name="surfacePoint"/> by <paramref name="factor"/>.</summary>
    /// <param name="surfacePoint">The point kept still, in surface coordinates.</param>
    /// <param name="factor">The scale multiplier; below one zooms out.</param>
    public void ZoomAt(DrawingPointF surfacePoint, double factor)
    {
        if (Tree?.Layout is not { } layout)
        {
            return;
        }

        var current = layout.Scale.Horizontal;
        var next = Clamp(current * factor, 0.25d, 4d);
        if (Math.Abs(next - current) < 0.0001d)
        {
            return;
        }

        var origin = AbsoluteBounds.Location;
        var localX = surfacePoint.X - origin.X;
        var localY = surfacePoint.Y - origin.Y;
        var worldX = (localX - _pan.X) * current;
        var worldY = (localY - _pan.Y) * current;

        layout.Scale = new Wf.Scale(next, next);
        SetPan(new DrawingPointF(
            (float)(localX - (worldX / next)),
            (float)(localY - (worldY / next))));

        InvalidateMeasure();
        Owner?.OnCanvasGeometryChanged();
    }

    internal void Attach(ITreeComponent tree)
    {
        Detach();

        Tree = tree;
        _linkLayer.Attach(tree);
        tree.Nodes.CollectionChanged += OnNodesChanged;
        tree.Links.CollectionChanged += OnLinksChanged;
        if (tree.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotifier = layout;
            layout.PropertyChanged += OnLayoutPropertyChanged;
        }

        RebuildViews();
        InvalidateMeasure();
    }

    internal void Detach()
    {
        if (Tree is { } tree)
        {
            tree.Nodes.CollectionChanged -= OnNodesChanged;
            tree.Links.CollectionChanged -= OnLinksChanged;
        }

        if (_layoutNotifier is not null)
        {
            _layoutNotifier.PropertyChanged -= OnLayoutPropertyChanged;
            _layoutNotifier = null;
        }

        _linkLayer.Detach();
        ClearViews();
        Tree = null;
        InvalidateMeasure();
    }

    internal void SyncViewGeometry(VisualElement view)
    {
        if (GetNode(view) is not { } node)
        {
            return;
        }

        var anchor = node.Anchor;
        var size = node.Size;
        var width = Math.Max(24d, size.Width);
        var height = Math.Max(16d, size.Height);

        if (!NearlyEquals(view.Width, width))
        {
            view.Width = width;
        }

        if (!NearlyEquals(view.Height, height))
        {
            view.Height = height;
        }

        var left = anchor.Horizontal + _pan.X;
        var top = anchor.Vertical + _pan.Y;
        if (!NearlyEquals(VisualCanvas.GetLeft(view), left))
        {
            VisualCanvas.SetLeft(view, left);
        }

        if (!NearlyEquals(VisualCanvas.GetTop(view), top))
        {
            VisualCanvas.SetTop(view, top);
        }
    }

    internal void NotifyNodeGeometryChanged(VisualElement view)
    {
        SyncViewGeometry(view);
        _linkLayer.Refresh();
        Owner?.OnCanvasGeometryChanged();
        InvalidateArrange();
    }

    internal void RefreshFromTree()
    {
        if (Tree is not { } tree)
        {
            return;
        }

        var live = new HashSet<INodeComponent>(tree.Nodes);
        foreach (var node in new List<INodeComponent>(_viewsByNode.Keys))
        {
            if (!live.Contains(node))
            {
                RemoveView(node);
            }
        }

        foreach (var node in tree.Nodes)
        {
            if (!_viewsByNode.ContainsKey(node))
            {
                AddView(node);
            }
        }

        foreach (var view in _nodeViews)
        {
            SyncViewGeometry(view);
        }

        _linkLayer.Refresh();
        InvalidateMeasure();
    }

    private static INodeComponent? GetNode(VisualElement view)
        => VisualWorkflowNodeBehavior.GetNode(view);


    /// <inheritdoc />
    protected override DrawingSize MeasureOverride(DrawingSize availableSize)
    {
        // 连线层铺满视口；节点卡各自用显式宽高量，所以给它们空约束也不会塌成 0。
        _linkLayer.Measure(availableSize);
        foreach (var view in _nodeViews)
        {
            SyncViewGeometry(view);
            view.Measure(DrawingSize.Empty);
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(DrawingSize finalSize)
    {
        base.ArrangeOverride(finalSize);
        _linkLayer.Arrange(new DrawingRectangleF(0f, 0f, finalSize.Width, finalSize.Height));
        SyncGeometry();
    }

    private void SyncGeometry()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            foreach (var view in _nodeViews)
            {
                VisualWorkflowNodeBehavior.ArrangeSlots(
                    view,
                    new DrawingSize(
                        (int)Math.Ceiling(view.Bounds.Width),
                        (int)Math.Ceiling(view.Bounds.Height)));
                VisualWorkflowNodeBehavior.SyncAnchors(view);
            }

            _linkLayer.Refresh();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RebuildViews()
    {
        ClearViews();
        if (Tree is not { } tree)
        {
            return;
        }

        foreach (var node in tree.Nodes)
        {
            AddView(node);
        }
    }

    private void ClearViews()
    {
        foreach (var view in _nodeViews)
        {
            DetachView(view);
            if (Children.Contains(view))
            {
                RemoveChild(view);
            }

            view.Dispose();
        }

        _nodeViews.Clear();
        _viewsByNode.Clear();
    }

    private void AddView(INodeComponent node)
    {
        if (_viewsByNode.ContainsKey(node))
        {
            return;
        }

        var view = Owner?.CreateNodeView(node) ?? new DefaultVisualWorkflowNodeView();
        Add(view);
        _nodeViews.Add(view);
        _viewsByNode[node] = view;
        VisualWorkflowNodeBehavior.Attach(view, node, Owner?.TemplateSelector, this);
        SyncViewGeometry(view);
    }

    private void RemoveView(INodeComponent node)
    {
        if (!_viewsByNode.TryGetValue(node, out var view))
        {
            return;
        }

        _viewsByNode.Remove(node);
        _nodeViews.Remove(view);
        DetachView(view);
        if (Children.Contains(view))
        {
            RemoveChild(view);
        }

        view.Dispose();
    }

    private static void DetachView(VisualElement view)
        => VisualWorkflowNodeBehavior.Detach(view);

    private void RepositionViews()
    {
        foreach (var view in _nodeViews)
        {
            SyncViewGeometry(view);
        }
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (var item in e.NewItems)
                {
                    if (item is INodeComponent node)
                    {
                        AddView(node);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (var item in e.OldItems)
                {
                    if (item is INodeComponent node)
                    {
                        RemoveView(node);
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                ClearViews();
                if (Tree is { } tree)
                {
                    foreach (var node in tree.Nodes)
                    {
                        AddView(node);
                    }
                }

                break;
        }

        _linkLayer.Refresh();
        InvalidateMeasure();
        Owner?.OnCanvasGeometryChanged();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _linkLayer.Refresh();
        InvalidateVisual();
        Owner?.OnCanvasGeometryChanged();
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CanvasLayout.Scale))
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private static bool NearlyEquals(double left, double right)
        => !double.IsNaN(left) && !double.IsNaN(right) && Math.Abs(left - right) < 0.01d;

    private static double Clamp(double value, double minimum, double maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;
}
