using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>A visual node card that supplies its own content instead of authoring it in XAML.</summary>
/// <remarks>
/// Implement this on a plain visual control when the card cannot be expressed as markup; the surface wraps the
/// returned content at the node's design size and scales it with the card.
/// </remarks>
public interface IVisualWorkflowNodeContent
{
    /// <summary>Builds the card content for <paramref name="node"/>.</summary>
    VisualElement? BuildContent(INodeComponent node);
}

/// <summary>
/// Binds a plain visual element to one workflow node: drag, slot views and the slot anchors.
/// </summary>
/// <remarks>
/// The card markup and paint belong to the host's view; this behavior is the visual-backend counterpart of the
/// platform node/node-rows behaviors. It is installed by <see cref="VisualWorkflowSurface"/> for any view the
/// template selector returns, so a node view does not have to derive from a workflow-specific base class.
/// </remarks>
public static class VisualWorkflowNodeBehavior
{
    private static readonly ConditionalWeakTable<VisualElement, NodeState> States = new();

    /// <summary>Returns the node bound to <paramref name="view"/>, or <see langword="null"/>.</summary>
    public static INodeComponent? GetNode(VisualElement view)
        => view is not null && States.TryGetValue(view, out var state) ? state.Node : null;

    /// <summary>Returns the slot views owned by <paramref name="view"/>.</summary>
    public static IReadOnlyList<VisualElement> GetSlotViews(VisualElement view)
        => view is not null && States.TryGetValue(view, out var state)
            ? state.SlotViews
            : Array.Empty<VisualElement>();

    internal static void Attach(
        VisualElement view,
        INodeComponent node,
        VisualTemplateSelector? selector,
        VisualWorkflowCanvas canvas)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (node is null) throw new ArgumentNullException(nameof(node));

        Detach(view);
        var state = new NodeState(view, node, selector, canvas);
        States.Add(view, state);
        state.Attach();
    }

    internal static void Detach(VisualElement view)
    {
        if (States.TryGetValue(view, out var state))
        {
            state.Detach();
            States.Remove(view);
        }
    }

    internal static void ArrangeSlots(VisualElement view, DrawingSize finalSize)
    {
        if (States.TryGetValue(view, out var state))
        {
            state.ArrangeSlots(finalSize);
        }
    }

    internal static void SyncAnchors(VisualElement view)
    {
        if (States.TryGetValue(view, out var state))
        {
            state.SyncAnchors();
        }
    }

    private sealed class NodeState(
        VisualElement view,
        INodeComponent node,
        VisualTemplateSelector? selector,
        VisualWorkflowCanvas canvas)
    {
        private const float HeaderHeight = 72f;
        private const float RowHeight = 22f;
        private const float InputInset = 2f;
        private const float OutputInset = 12f;

        private readonly List<VisualElement> _slotViews = [];
        private readonly Dictionary<ISlotComponent, Wf.Anchor> _anchorCache = [];
        private ScaledContentHost? _contentHost;
        private bool _dragging;
        private DrawingPointF _lastPointer;
        private double _designWidth;
        private double _designHeight;

        internal INodeComponent Node => node;

        internal IReadOnlyList<VisualElement> SlotViews => _slotViews;

        internal void Attach()
        {
            view.DataContext = node;
            if (node is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged += OnNodePropertyChanged;
            }

            node.Slots.CollectionChanged += OnSlotsChanged;
            view.PointerPressed += OnPointerPressed;
            view.PointerMoved += OnPointerMoved;
            view.PointerReleased += OnPointerReleased;
            _designWidth = DesignWidth(node);
            _designHeight = DesignHeight(node);
            if (view is IVisualWorkflowNodeContent provider
                && provider.BuildContent(node) is { } content)
            {
                _contentHost = new ScaledContentHost(content, _designWidth, _designHeight);
                view.Add(_contentHost);
                content.IsHitTestVisible = false;
            }

            RebuildSlots();
        }

        internal void Detach()
        {
            if (node is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnNodePropertyChanged;
            }

            node.Slots.CollectionChanged -= OnSlotsChanged;
            view.PointerPressed -= OnPointerPressed;
            view.PointerMoved -= OnPointerMoved;
            view.PointerReleased -= OnPointerReleased;
            _dragging = false;
            ClearSlots();
            if (_contentHost is not null)
            {
                view.Remove(_contentHost);
                _contentHost.Dispose();
                _contentHost = null;
            }

            view.DataContext = null;
        }

        internal void ArrangeSlots(DrawingSize finalSize)
        {
            if (_slotViews.Count == 0 || _designWidth <= 0d || _designHeight <= 0d)
            {
                return;
            }

            var scale = (float)(finalSize.Width / _designWidth);
            if (scale <= 0f || float.IsNaN(scale))
            {
                scale = 1f;
            }

            var bodyTop = Math.Min(HeaderHeight * scale, finalSize.Height * 0.5f);
            var bodyHeight = Math.Max(0f, finalSize.Height - bodyTop);
            var left = new List<VisualElement>();
            var right = new List<VisualElement>();
            foreach (var slotView in _slotViews)
            {
                if (VisualWorkflowSlotBehavior.GetSlot(slotView) is { } slot && CanSend(slot.Channel))
                {
                    right.Add(slotView);
                }
                else
                {
                    left.Add(slotView);
                }
            }

            PlaceRows(left, finalSize, bodyTop, bodyHeight, scale, input: true);
            PlaceRows(right, finalSize, bodyTop, bodyHeight, scale, input: false);

            static void PlaceRows(
                List<VisualElement> views,
                DrawingSize size,
                float bodyTop,
                float bodyHeight,
                float scale,
                bool input)
            {
                if (views.Count == 0)
                {
                    return;
                }

                var glyph = Math.Max(10f, DefaultVisualWorkflowSlotView.DesignGlyphSize * scale);
                var rowStep = Math.Min(RowHeight * scale, bodyHeight / views.Count);
                var group = rowStep * views.Count;
                var start = bodyTop + ((bodyHeight - group) / 2f);
                var x = input
                    ? InputInset * scale
                    : size.Width - (OutputInset * scale) - glyph;
                for (var index = 0; index < views.Count; index++)
                {
                    var centerY = start + (rowStep * (index + 0.5f));
                    var slotView = views[index];
                    slotView.Width = glyph;
                    slotView.Height = glyph;
                    slotView.Arrange(new DrawingRectangleF(x, centerY - (glyph / 2f), glyph, glyph));
                }
            }
        }

        internal void SyncAnchors()
        {
            var nodeAnchor = node.Anchor;
            if (double.IsNaN(nodeAnchor.Horizontal) || double.IsNaN(nodeAnchor.Vertical))
            {
                return;
            }

            foreach (var slotView in _slotViews)
            {
                if (VisualWorkflowSlotBehavior.GetSlot(slotView) is not { } slot)
                {
                    continue;
                }

                var bounds = slotView.Bounds;
                if (bounds.Width <= 0f || bounds.Height <= 0f)
                {
                    continue;
                }

                var next = WorkflowSurfaceMath.SlotAnchorFromNode(
                    nodeAnchor.Horizontal,
                    nodeAnchor.Vertical,
                    bounds.X + (bounds.Width / 2d),
                    bounds.Y + (bounds.Height / 2d),
                    slot.Anchor?.Layer ?? 0);

                if (_anchorCache.TryGetValue(slot, out var previous) && Same(previous, next))
                {
                    continue;
                }

                _anchorCache[slot] = next;
                slot.Anchor = next;
            }
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size)))
            {
                return;
            }

            if (e.PropertyName is nameof(INodeComponent.Size))
            {
                _designWidth = DesignWidth(node);
                _designHeight = DesignHeight(node);
                _contentHost?.UpdateDesignSize(_designWidth, _designHeight);
            }

            canvas.SyncViewGeometry(view);
            canvas.NotifyNodeGeometryChanged(view);
        }

        private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildSlots();
            canvas.NotifyNodeGeometryChanged(view);
        }

        private void OnPointerPressed(object? sender, VisualPointerEventArgs args)
        {
            if (args.Button != MouseButtons.Left || HitSlot(args.Position))
            {
                return;
            }

            _dragging = true;
            _lastPointer = args.Position;
        }

        private void OnPointerMoved(object? sender, VisualPointerEventArgs args)
        {
            if (!_dragging || (args.Buttons & MouseButtons.Left) == 0)
            {
                _dragging = false;
                return;
            }

            var deltaX = args.Position.X - _lastPointer.X;
            var deltaY = args.Position.Y - _lastPointer.Y;
            if (Math.Abs(deltaX) < 0.01f && Math.Abs(deltaY) < 0.01f)
            {
                return;
            }

            // StandardMove takes a view-space delta and expands it to world itself; the pointer delta is already
            // in that space, so applying Layout.Scale here would apply the zoom twice.
            node.MoveCommand.Execute(new Wf.Offset(deltaX, deltaY));
            _lastPointer = args.Position;
        }

        private void OnPointerReleased(object? sender, VisualPointerEventArgs args)
            => _dragging = false;

        private void RebuildSlots()
        {
            ClearSlots();
            foreach (var slot in node.Slots)
            {
                var slotView = selector?.SelectTemplate(slot) as VisualElement
                               ?? new DefaultVisualWorkflowSlotView();
                VisualWorkflowSlotBehavior.Attach(slotView, slot);
                view.Add(slotView);
                _slotViews.Add(slotView);
            }
        }

        private void ClearSlots()
        {
            foreach (var slotView in _slotViews)
            {
                VisualWorkflowSlotBehavior.Detach(slotView);
                if (view.Children.Contains(slotView))
                {
                    view.Remove(slotView);
                }

                slotView.Dispose();
            }

            _slotViews.Clear();
            _anchorCache.Clear();
        }

        private bool HitSlot(DrawingPointF surfacePoint)
        {
            var local = view.PointFromSurface(surfacePoint);
            foreach (var slotView in _slotViews)
            {
                if (slotView.Bounds.Contains(local))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanSend(SlotChannel channel)
            => (channel & SlotChannel.OneTarget) != 0 || (channel & SlotChannel.MultipleTargets) != 0;

        private static double DesignWidth(INodeComponent node)
        {
            var scale = ScaleOf(node);
            return Math.Max(24d, node.Size.Width * scale);
        }

        private static double DesignHeight(INodeComponent node)
        {
            var scale = ScaleOf(node);
            return Math.Max(16d, node.Size.Height * scale);
        }

        private static double ScaleOf(INodeComponent node)
        {
            var scale = node.Parent?.Layout?.Scale.Horizontal ?? 1d;
            return double.IsNaN(scale) || scale <= 0d ? 1d : scale;
        }

        private static bool Same(Wf.Anchor left, Wf.Anchor right)
            => Math.Abs(left.Horizontal - right.Horizontal) < 0.01d
               && Math.Abs(left.Vertical - right.Vertical) < 0.01d
               && left.Layer == right.Layer;

        // The code-built card content keeps its design metrics and is scaled while painting. The transform goes
        // through the graphics matrix (Prepend) so the card's own position is not scaled with it.
        private sealed class ScaledContentHost : VisualElement
        {
            private readonly VisualElement _child;
            private double _designWidth;
            private double _designHeight;
            private float _scale = 1f;
            private float _offsetX;
            private float _offsetY;

            internal ScaledContentHost(VisualElement child, double designWidth, double designHeight)
            {
                _child = child;
                _designWidth = designWidth;
                _designHeight = designHeight;
                IsHitTestVisible = false;
                Add(child);
            }

            internal void UpdateDesignSize(double width, double height)
            {
                if (double.IsNaN(width) || double.IsNaN(height) || width <= 0d || height <= 0d)
                {
                    return;
                }

                _designWidth = width;
                _designHeight = height;
                InvalidateMeasure();
            }

            protected override DrawingSize MeasureOverride(DrawingSize availableSize)
            {
                var size = DesignSize();
                _child.Measure(size);
                return size;
            }

            protected override void ArrangeOverride(DrawingSize finalSize)
            {
                var design = DesignSize();
                if (design.Width <= 0 || design.Height <= 0)
                {
                    return;
                }

                _scale = Math.Min(
                    finalSize.Width / (float)design.Width,
                    finalSize.Height / (float)design.Height);
                if (_scale <= 0f)
                {
                    _scale = 1f;
                }

                _offsetX = (finalSize.Width - (design.Width * _scale)) / 2f;
                _offsetY = (finalSize.Height - (design.Height * _scale)) / 2f;
                _child.Arrange(new DrawingRectangleF(0f, 0f, design.Width, design.Height));
            }

            protected override void RenderChildren(Graphics graphics)
            {
                var state = graphics.Save();
                try
                {
                    using var matrix = new Matrix(_scale, 0f, 0f, _scale, _offsetX, _offsetY);
                    graphics.MultiplyTransform(matrix, MatrixOrder.Prepend);
                    base.RenderChildren(graphics);
                }
                finally
                {
                    graphics.Restore(state);
                }
            }

            private DrawingSize DesignSize()
                => new(
                    Math.Max(0, (int)Math.Ceiling(_designWidth)),
                    Math.Max(0, (int)Math.Ceiling(_designHeight)));
        }
    }
}
