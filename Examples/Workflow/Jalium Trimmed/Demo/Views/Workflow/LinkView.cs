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
        // World size: the LinkView is a child of the scale-only canvas, so it is scaled with it and
        // must span the UNSCALED world canvas (Layout.ActualSize) — the scaled parent bounds would
        // overshoot at zoom > 1 and clip the polyline at zoom < 1.
        var (w, h) = WorldSize();
        if (w > 0 && h > 0)
        {
            Width = w;
            Height = h;
            return;
        }

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

    private (double W, double H) WorldSize()
    {
        var layout = _link?.Sender.Parent?.Parent?.Layout;
        if (layout is not null && layout.ActualSize.Width > 0)
        {
            return (layout.ActualSize.Width, layout.ActualSize.Height);
        }

        return (0, 0);
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
            _layoutHandler = (_, _) =>
            {
                SizeToParent();
                InvalidateVisual();
            };
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

    private static bool IsVirtual(IWorkflowLinkViewModel link)
        => link.Sender.Parent is null && link.Receiver.Parent is null;

    /// <summary>Authoritative model port center (node.Anchor + geometry), independent of slot.Anchor
    /// which the safe surface does not maintain.</summary>
    private Point PortCenter(IWorkflowSlotViewModel slot)
    {
        var node = slot.Parent;
        if (node is null) return default;
        if (SlotView.IndexOf(node, slot) is { } found)
        {
            if (found.IsInput)
                return new Point(node.Anchor.Horizontal + SlotView.InputPortX, node.Anchor.Vertical + node.Size.Height / 2.0);
            return new Point(node.Anchor.Horizontal + node.Size.Width - SlotView.OutputInset,
                node.Anchor.Vertical + SlotView.TitleBarH + SlotView.RowH * found.Index + SlotView.RowH / 2.0);
        }
        return default;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            var (w, h) = WorldSize();
            if (w > 0)
            {
                Width = w;
                Height = h;
            }
        }

        if (_link is null || !_link.IsVisible || IsVirtual(_link))
        {
            return;
        }

        var origin = _link.Sender.Parent?.Parent?.Layout.ActualOffset ?? new Offset();
        var from = new Point(PortCenter(_link.Sender).X + origin.Horizontal, PortCenter(_link.Sender).Y + origin.Vertical);
        var to = new Point(PortCenter(_link.Receiver).X + origin.Horizontal, PortCenter(_link.Receiver).Y + origin.Vertical);
        var virtualLink = IsVirtual(_link);

        var pen = virtualLink
            ? new Pen(s_brush, 2) { DashStyle = new DashStyle(new[] { 4.0, 2.0 }) }
            : new Pen(s_brush, 2);

        double dx = Math.Abs(to.X - from.X);
        double stub = dx / 2.0 * (1.0 - Phi);
        if (stub < 8) stub = 8;
        double dir = to.X >= from.X ? 1 : -1;
        var p1 = new Point(from.X + dir * stub, from.Y);
        var p2 = new Point(p1.X, to.Y);
        var p3 = new Point(to.X - dir * stub, to.Y);

        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(new[] { p1, p2, p3, to }, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);

        if (!virtualLink)
        {
            const double len = 12, halfW = 4;
            var arrow = new PathFigure { StartPoint = to, IsClosed = true, IsFilled = true };
            arrow.Segments.Add(new LineSegment(new Point(to.X - dir * len, to.Y - halfW), true));
            arrow.Segments.Add(new LineSegment(new Point(to.X - dir * len, to.Y + halfW), true));
            var arrowGeometry = new PathGeometry();
            arrowGeometry.Figures.Add(arrow);
            dc.DrawGeometry(s_brush, null, arrowGeometry);
        }
    }
}
