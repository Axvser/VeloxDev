using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>A poolable link view for the node-editor surface: orthogonal polyline (golden-ratio stubs)
/// with a 12x8 arrowhead on real links. Sized to the host canvas and drawn at the port centers
/// (node.Anchor + geometry) + layout offset (world + ActualOffset — the authoritative model), so it never
/// goes stale on pan. The link comes from DataContext; only real visible links render — the drag-preview
/// VirtualLink is drawn inline by the surface.</summary>
public class LinkView : FrameworkElement
{
    private const double Phi = 0.6180339887;

    private static readonly SolidColorBrush s_brush = new(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));

    private IWorkflowLinkViewModel? _link;
    private INotifyPropertyChanged? _layoutNotify;
    private PropertyChangedEventHandler? _layoutHandler;
    private INotifyPropertyChanged? _senderNodeNotify;
    private INotifyPropertyChanged? _receiverNodeNotify;

    public LinkView()
    {
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);
        DataContextChanged += OnDataContextChanged;
        AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SizeToParent();
    }

    private void SizeToParent()
    {
        if (VisualTreeHelper.GetParent(this) is FrameworkElement parent && parent.ActualWidth > 0 && parent.ActualHeight > 0)
        {
            Width = parent.ActualWidth;
            Height = parent.ActualHeight;
            parent.SizeChanged -= OnParentSizeChanged;
            parent.SizeChanged += OnParentSizeChanged;
        }
        else
        {
            // Fallback so the view still renders before the parent is measured.
            Width = Math.Max(Width, 2000);
            Height = Math.Max(Height, 2000);
        }
    }

    private void OnParentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement parent)
        {
            Width = parent.ActualWidth;
            Height = parent.ActualHeight;
        }
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe();
        UnsubscribeLayout();
        _link = DataContext as IWorkflowLinkViewModel;
        if (_link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged += OnLinkChanged;
        if (_link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged += OnLinkChanged;
        if (_link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged += OnLinkChanged;
        // The surface positions links from node.Anchor (it never writes slot.Anchor), so the link must
        // track the endpoint nodes directly — a node drag moves the connection immediately.
        if (_link?.Sender?.Parent is INotifyPropertyChanged senderNode)
        {
            _senderNodeNotify = senderNode;
            senderNode.PropertyChanged += OnNodeChanged;
        }
        if (_link?.Receiver?.Parent is INotifyPropertyChanged receiverNode)
        {
            _receiverNodeNotify = receiverNode;
            receiverNode.PropertyChanged += OnNodeChanged;
        }
        if (_link?.Sender.Parent?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            _layoutHandler = (_, _) => InvalidateVisual();
            layout.PropertyChanged += _layoutHandler;
        }

        InvalidateVisual();
    }

    private void Unsubscribe()
    {
        if (_link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged -= OnLinkChanged;
        if (_link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged -= OnLinkChanged;
        if (_link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged -= OnLinkChanged;
        if (_senderNodeNotify is not null)
        {
            _senderNodeNotify.PropertyChanged -= OnNodeChanged;
            _senderNodeNotify = null;
        }
        if (_receiverNodeNotify is not null)
        {
            _receiverNodeNotify.PropertyChanged -= OnNodeChanged;
            _receiverNodeNotify = null;
        }
        _link = null;
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            InvalidateVisual();
        }
    }

    private void UnsubscribeLayout()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= _layoutHandler;
            _layoutNotify = null;
            _layoutHandler = null;
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    /// <summary>True only for the tree's drag-preview VirtualLink: its endpoints are placeholder
    /// <see cref="SlotDefaultViewModel"/>s, not real slots mounted to nodes. The surface draws that
    /// preview inline in OnPostRender, so the pooled LinkView must skip it. Real links are never this
    /// type — even one whose slots were detached keeps rendering instead of vanishing.</summary>
    private static bool IsDragPreview(IWorkflowLinkViewModel link)
        => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel;

    /// <summary>Authoritative model port center (node.Anchor + designLocal·s), independent of slot.Anchor
    /// which the safe surface does not maintain. The card is drawn at the DESIGN size inside a Viewbox
    /// scaled to the collapsed box, so the port world center is the DESIGN local center times the collapse
    /// factor s = node.Size/DesignSize. Returns null when the endpoint has no position yet (slot detached
    /// with no measured anchor) so the link is skipped instead of drawing a degenerate line from the origin.</summary>
    private Point? PortCenter(IWorkflowSlotViewModel slot)
    {
        var node = slot.Parent;
        if (node is null)
        {
            // Detached endpoint: fall back to a measured slot anchor if a GUI wrote one
            // (WPF-style adapters maintain slot.Anchor); otherwise the endpoint has no position.
            var anchor = slot.Anchor;
            if (!double.IsNaN(anchor.Horizontal) && !double.IsNaN(anchor.Vertical))
                return new Point(anchor.Horizontal, anchor.Vertical);
            return null;
        }
        if (SlotView.IndexOf(node, slot) is { } found)
        {
            if (found.IsInput)
                return ScaledCenter(node, new Point(SlotView.InputPortX, SlotView.DesignHeight / 2.0));
            return ScaledCenter(node, new Point(SlotView.DesignWidth - SlotView.OutputInset,
                SlotView.TitleBarH + SlotView.RowH * found.Index + SlotView.RowH / 2.0));
        }
        // Slot is mounted to the node but not in the current port enumeration (stale instance after
        // a selector switch): clamp to the node card so the connection stays visible and roughly
        // positioned while the model re-establishes the slot.
        return ScaledCenter(node, new Point(SlotView.DesignWidth - SlotView.OutputInset, SlotView.DesignHeight / 2.0));
    }

    /// <summary>Design-local center scaled by the collapse factor (node.Size/DesignSize), matching the
    /// card's Viewbox scale and the surface's hit-testing.</summary>
    private static Point ScaledCenter(IWorkflowNodeViewModel node, Point designLocal)
    {
        var sx = SlotView.DesignWidth == 0 ? 1 : node.Size.Width / SlotView.DesignWidth;
        var sy = SlotView.DesignHeight == 0 ? 1 : node.Size.Height / SlotView.DesignHeight;
        return new Point(node.Anchor.Horizontal + designLocal.X * sx, node.Anchor.Vertical + designLocal.Y * sy);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if ((RenderSize.Width <= 0 || RenderSize.Height <= 0) && VisualTreeHelper.GetParent(this) is FrameworkElement p)
        {
            Width = p.ActualWidth;
            Height = p.ActualHeight;
        }

        if (_link is null || !_link.IsVisible || IsDragPreview(_link))
        {
            return;
        }

        var origin = _link.Sender.Parent?.Parent?.Layout.ActualOffset ?? new Offset();
        var from = PortCenter(_link.Sender);
        var to = PortCenter(_link.Receiver);
        if (from is null || to is null) return; // an endpoint has no position — nothing to draw

        var fromP = new Point(from.Value.X + origin.Horizontal, from.Value.Y + origin.Vertical);
        var toP = new Point(to.Value.X + origin.Horizontal, to.Value.Y + origin.Vertical);
        var pen = new Pen(s_brush, 2);

        // Golden-ratio polyline aligned with the other GUI schemes (mirrors the item template).
        double dx = toP.X - fromP.X;
        double stub = dx / 2.0 * (1.0 - Phi);
        var p1 = new Point(fromP.X + stub, fromP.Y);
        var p2 = new Point(toP.X - stub, toP.Y);

        var figure = new PathFigure { StartPoint = fromP, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new[] { p1, p2, toP }, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }
}
