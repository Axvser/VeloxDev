// VeloxDev customization: Customize line geometry, color, thickness, and highlight here.
// Keep the offset-frame contract: geometry is baked +ActualOffset and this element self-positions at
// -ActualOffset sized to the model ActualSize, so collapse-zoom never clips a link's negative half.
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Globalization;
// Alias Path: `using Microsoft.UI.Xaml.Shapes;` would collide with System.IO.Path (implicit usings).
using Path = Microsoft.UI.Xaml.Shapes.Path;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// Passive visual only — never hit-testable, so it cannot swallow canvas gestures. It publishes the curve it
/// draws, in raw canvas-local DP values, so the surface can hit-test it, and implements
/// <see cref="ILinkHighlight"/> so the surface lights it on hover; keep the curve in sync with the drawn shape.
/// </summary>
public sealed partial class TemplateClass : UserControl, ILinkHighlight
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    private readonly Path _path;
    private readonly SolidColorBrush _strokeBrush = new(ParseColor("TemplateLinkColor"));
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly BezierSegment _segment = new();
    private bool _updatePending;
    private bool _isLoaded;
    private CanvasLayout? _layout;
    private PropertyChangedEventHandler? _layoutHandler;
    private double _offsetX;
    private double _offsetY;

    // The drawn curve, published un-baked (raw canvas-local DP values) so a surface can hit-test it.
    private LinkCurve? _curve;
    private IWorkflowLinkViewModel? _boundLink;

    // Offset-frame origin actually applied: the geometry is baked +(_offsetX,_offsetY) and this element
    // is placed at −(_offsetX,_offsetY), so an endpoint's DRAWN canvas-local coordinate still equals its
    // raw DP value (alignment with the nodes is unchanged) while the geometry now lives INSIDE this
    // element's box — no element-bounds clip can cut the negative half of a deep-zoom link.
    internal double OffsetFrameX => _offsetX;
    internal double OffsetFrameY => _offsetY;

    public TemplateClass()
    {
        InitializeComponent();
        Canvas.SetZIndex(this, -100);
        IsHitTestVisible = false;

        // The curve geometry is in raw collapsed (canvas-local) coordinates, so at deep zoom its
        // negative top/left half extends beyond this element's bounds. WinUI clips element content to
        // its bounds unless Clip is nulled — the same root/grid pattern NodeView uses for its
        // overhanging ports. WPF links are OnRender-drawn and never clipped, which is why they survive;
        // this null chain reproduces that "no clip" behavior for the retained Path.
        var container = new Grid { Clip = null };
        _path = new Path { Stroke = _strokeBrush, StrokeThickness = TemplateLinkThickness, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Clip = null };
        container.Children.Add(_path);
        this.Content = container;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
        ScheduleUpdate();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(TemplateClass), new PropertyMetadata(ParseColor("TemplateLinkColor"), OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.Register(nameof(HighlightColor), typeof(Windows.UI.Color), typeof(TemplateClass), new PropertyMetadata(ParseColor("#FFFFFFFF"), OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    // Extension point: the white glow shown while this link is highlighted. White is deliberate — the line
    // reads as lit rather than recoloured, and the halo drawn around it is what makes it a glow.
    public Windows.UI.Color HighlightColor { get => (Windows.UI.Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Synchronous redraw: the endpoint DPs are bound to measured slot anchors which are written
        // in-frame (synchronously on LayoutUpdated), so redraw now or the link lags a frame and
        // jitters while zooming.
        ((TemplateClass)d).UpdatePath();
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    #endregion

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateLayoutSubscription();
        EnsureGeometry();
        ScheduleUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _updatePending = false;
        UnsubscribeLayout();
        _boundLink?.PublishCurve(null);
    }

    private void OnDataContextChanged(object sender, DataContextChangedEventArgs args)
    {
        var link = args.NewValue as IWorkflowLinkViewModel;
        if (!ReferenceEquals(_boundLink, link))
        {
            // Pool reuse / hide: retract the previous link's curve, or it keeps answering hit tests.
            _boundLink?.PublishCurve(null);
            _boundLink = link;
        }

        // Pool reuse re-assigns DataContext (and hides show a null DataContext first), so the layout
        // subscription is rebound here every time — the layout it must follow is the TREE's layout.
        UpdateLayoutSubscription();
        ScheduleUpdate();
    }

    /// <summary>Resolve the owning tree (Sender/Receiver → node → tree) and follow its CanvasLayout,
    /// so when EnsureNegativeCover grows ActualOffset during zoom the offset-frame bake re-runs.</summary>
    private void UpdateLayoutSubscription()
    {
        UnsubscribeLayout();

        if (DataContext is not IWorkflowLinkViewModel link)
        {
            return;
        }

        var tree = link.Sender?.Parent?.Parent as IWorkflowTreeViewModel
                   ?? link.Receiver?.Parent?.Parent as IWorkflowTreeViewModel;

        // A virtual link's endpoints are deliberately parentless, so this must not be the only way to
        // find the tree: fall back to the host chain.
        tree ??= FindHostTree();
        if (tree?.Layout is { } layout)
        {
            _layout = layout;
            _layoutHandler = OnLayoutPropertyChanged;
            _layout.PropertyChanged += _layoutHandler;
        }
    }

    // Walk the host chain for the tree that carries this view; a virtual link has no endpoints to walk.
    private IWorkflowTreeViewModel? FindHostTree()
    {
        for (var p = Parent as FrameworkElement; p is not null; p = p.Parent as FrameworkElement)
        {
            if (p.DataContext is IWorkflowTreeViewModel tree)
            {
                return tree;
            }
        }

        return null;
    }

    private void UnsubscribeLayout()
    {
        if (_layout is not null && _layoutHandler is not null)
        {
            _layout.PropertyChanged -= _layoutHandler;
        }

        _layout = null;
        _layoutHandler = null;
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only the two quantities this view bakes/positions against need a rebuild. ViewportOffset
        // changes on every pan tick and must NOT trigger redraws (they would fight smooth panning).
        if (e.PropertyName is nameof(CanvasLayout.ActualOffset) or nameof(CanvasLayout.ActualSize))
        {
            ScheduleUpdate();
        }
    }

    private void EnsureGeometry()
    {
        if (_pathFigure.Segments.Count == 0)
        {
            _pathFigure.Segments.Add(_segment);
        }

        if (_pathGeometry.Figures.Count == 0)
        {
            _pathGeometry.Figures.Add(_pathFigure);
        }
    }

    private void ScheduleUpdate()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _updatePending = false;
            if (_isLoaded)
            {
                UpdatePath();
            }
        });
    }

    private void UpdatePath()
    {
        EnsureGeometry();

        if (!CanRender)
        {
            _path.Data = null;
            PublishCurve();
            return;
        }

        // Offset frame: bake the canvas translate (+ActualOffset) into the geometry AND shift this
        // element to −ActualOffset. The box then spans [−ActualOffset, ActualSize − ActualOffset] in
        // canvas-local, and the baked geometry is always inside this element's own [0, ActualSize]
        // box — collapse-zoom can never push a link's negative half out of its element bounds, which
        // is what a WinUI element-bounds clip would cut. Rendered position is unchanged: an element
        // at −o drawing point (g+o) lands at canvas-local g, the exact coordinate the raw DPs hold and
        // the nodes sit at. Width/Height come from the model canvas size (not the arranged ActualWidth)
        // so the box is authoritative even where an ElementName ActualWidth binding would lag.
        double ox = 0, oy = 0, w = double.NaN, h = double.NaN;
        if (_layout is not null)
        {
            ox = _layout.ActualOffset.Horizontal;
            oy = _layout.ActualOffset.Vertical;
            w = _layout.ActualSize.Width;
            h = _layout.ActualSize.Height;
        }

        _offsetX = ox;
        _offsetY = oy;
        Canvas.SetLeft(this, -ox);
        Canvas.SetTop(this, -oy);
        if (w > 0 && h > 0)
        {
            Width = w;
            Height = h;
        }

        BuildCurve(ox, oy);
        // Extension point: the white glow shown while this link is highlighted. White is deliberate — the line
    // reads as lit rather than recoloured, and the halo drawn around it is what makes it a glow.
        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? TemplateLinkThickness + 1.5 : TemplateLinkThickness;
        _strokeBrush.Color = color;
        _path.StrokeThickness = thickness;

        if (IsVirtualLink)
            _path.StrokeDashArray = VirtualStrokeDashArray;
        else
            _path.StrokeDashArray = null;

        _path.Data = _pathGeometry;
    }

    // Extension point: the control points set the curve's shape. Both are pulled horizontally by
    // max(40, |dx| / 2), which is what makes the line leave each port horizontally — keep that
    // property if you replace the formula.
    private void BuildCurve(double ox, double oy)
    {
        double dx = EndLeft - StartLeft;
        double pull = Math.Max(40, Math.Abs(dx) * 0.5);
        _pathFigure.StartPoint = new Point(StartLeft + ox, StartTop + oy);
        _segment.Point1 = new Point(StartLeft + pull + ox, StartTop + oy);
        _segment.Point2 = new Point(EndLeft - pull + ox, EndTop + oy);
        _segment.Point3 = new Point(EndLeft + ox, EndTop + oy);

        // Publish the UN-baked curve for hit-testing — keep it built from the same control points as the
        // geometry above, and in the raw DP (canvas-local) space, if you replace the formula.
        _curve = LinkCurve.BuildCubic(StartLeft, StartTop, EndLeft, EndTop, 40);
        PublishCurve();
    }

    // Extension point: drop the published curve when the endpoints are not measured, so nothing can hit an
    // undrawn line.
    private void PublishCurve()
    {
        var ready = CanRender
            && !double.IsNaN(StartLeft) && !double.IsNaN(StartTop)
            && !double.IsNaN(EndLeft) && !double.IsNaN(EndTop);

        _boundLink?.PublishCurve(ready ? _curve : null, this);
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 8
            ? Windows.UI.Color.FromArgb(
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value)
            : Windows.UI.Color.FromArgb(
                0xFF,
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value);
    }
}
