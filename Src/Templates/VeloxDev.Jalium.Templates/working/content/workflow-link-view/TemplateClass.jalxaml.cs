// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// </summary>
/// <remarks>
/// <para>
/// The view reads its two ends <b>straight from the model</b> (<c>Sender.Anchor</c> / <c>Receiver.Anchor</c>)
/// rather than from bound properties. On this platform that is not a style choice: a pooled view is rebound many
/// times a second, and a bound endpoint is a frame stale every time — which reads as a link that lags the cards,
/// and as a link drawn from the origin the instant one is created.
/// </para>
/// <para>
/// It also moves its own layout box onto the curve <b>while the model reports a change</b>, never while drawing.
/// This platform's renderer decides whether to draw a child by its layout box, so a box moved during a render pass
/// is always one frame behind what is drawn inside it.
/// </para>
/// <para>
/// The view only paints: it publishes its curve for hit-testing and handles no input itself. Hover feedback is the
/// host's — subscribe <c>IInputEvents</c> on the helper and handle the routed pointer events.
/// </para>
/// </remarks>
public partial class TemplateClass : UserControl
{
    // Extension point: the least horizontal pull of the two control points. Keep it in step with the curve
    // that is published for hit-testing below.
    private const double MinimumPull = 40;

    // The link this view last published a curve to; retracted on rebind so a pooled view cannot leave a
    // stale curve answering for a link that no longer draws here.
    private ILinkComponent? _publishedLink;

    private ILinkComponent? _link;
    private ISlotComponent? _sender;
    private ISlotComponent? _receiver;
    private PropertyChangedEventHandler? _modelChanged;
    private readonly List<INotifyPropertyChanged> _watchedModels = [];
    private bool _refreshPending;
    private readonly List<Point> _curve = [];
    private double _originX;
    private double _originY;
    private bool _hasBounds;

    public TemplateClass()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => Rebind();
    }

    #region Dependency properties

    /// <summary>The canvas-local X of the start end.</summary>
    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the start end.</summary>
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local X of the end end.</summary>
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the end end.</summary>
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local X of the start end.</summary>
    public double StartLeft { get => GetValue(StartLeftProperty) is double value ? value : 0d; set => SetValue(StartLeftProperty, value); }

    /// <summary>The canvas-local Y of the start end.</summary>
    public double StartTop { get => GetValue(StartTopProperty) is double value ? value : 0d; set => SetValue(StartTopProperty, value); }

    /// <summary>The canvas-local X of the end end.</summary>
    public double EndLeft { get => GetValue(EndLeftProperty) is double value ? value : 0d; set => SetValue(EndLeftProperty, value); }

    /// <summary>The canvas-local Y of the end end.</summary>
    public double EndTop { get => GetValue(EndTopProperty) is double value ? value : 0d; set => SetValue(EndTopProperty, value); }

    /// <summary>Whether the link should be drawn at all; the model's own visibility is required as well.</summary>
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnRenderChanged));

    /// <summary>Whether the curve is drawn dashed as a virtual link.</summary>
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnRenderChanged));

    /// <summary>The stroke colour.</summary>
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(TemplateClass),
            new PropertyMetadata(ParseColor("TemplateLinkColor", Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), OnRenderChanged));

    /// <summary>The stroke width.</summary>
    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(TemplateClass),
            new PropertyMetadata((double)TemplateLinkThickness, OnRenderChanged));

    /// <summary>Whether the link should be drawn at all.</summary>
    public bool CanRender { get => GetValue(CanRenderProperty) is true; set => SetValue(CanRenderProperty, value); }

    /// <summary>Whether the curve is drawn dashed as a virtual link.</summary>
    public bool IsVirtual { get => GetValue(IsVirtualProperty) is true; set => SetValue(IsVirtualProperty, value); }

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => GetValue(LineColorProperty) is Color color ? color : Colors.White;
        set => SetValue(LineColorProperty, value);
    }

    /// <summary>The stroke width.</summary>
    public double LineThickness
    {
        get => GetValue(LineThicknessProperty) is double value ? value : TemplateLinkThickness;
        set => SetValue(LineThicknessProperty, value);
    }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).Refresh();

    // Colour tokens are replaced at generation time; fall back when parsing fails.
    private static Color ParseColor(string text, Color fallback)
        => ColorConverter.ConvertFromString(text) is Color color ? color : fallback;

    #endregion

    #region Model binding

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Rebind: retract the previous curve so a pooled view cannot answer for it.
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
        Rebind();
    }

    private void Rebind()
    {
        var link = DataContext as ILinkComponent;
        if (!ReferenceEquals(link, _link))
        {
            Unhook();

            _link = link;
            _sender = link?.Sender;
            _receiver = link?.Receiver;
            _modelChanged = (_, _) => ScheduleRefresh();

            // Watch both slots and their nodes: a moving node re-measures its slots later in the frame.
            Watch(_sender);
            Watch(_receiver);
            Watch(link);
        }

        Refresh();
    }

    private void Watch(IWorkflowComponent? model)
    {
        if (_modelChanged is null)
        {
            return;
        }

        var notifying = model as INotifyPropertyChanged;
        if (notifying is not null && _watchedModels.Contains(notifying) is false)
        {
            _watchedModels.Add(notifying);
            notifying.PropertyChanged += _modelChanged;
        }

        // A node's anchor change invalidates every slot on its card.
        var node = model is ISlotComponent slot ? slot.Parent : null;
        if (node is not null && _watchedModels.Contains(node) is false)
        {
            _watchedModels.Add(node);
            node.PropertyChanged += _modelChanged;
        }
    }

    private void Unhook()
    {
        if (_modelChanged is not null)
        {
            foreach (var model in _watchedModels)
            {
                model.PropertyChanged -= _modelChanged;
            }
        }

        _watchedModels.Clear();
        _sender = null;
        _receiver = null;
        _modelChanged = null;
    }

    // Defer to the render tick so slot anchors have been re-measured.
    private void ScheduleRefresh()
    {
        if (_refreshPending)
        {
            return;
        }

        _refreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _refreshPending = false;
            Refresh();
        }));
    }

    /// <summary>Re-reads the geometry and moves the layout box onto it; never runs while drawing.</summary>
    private void Refresh()
    {
        _curve.Clear();
        _hasBounds = false;

        if (!CanRender || _link is not { } link || !link.IsVisible || !link.IsRenderReady())
        {
            InvalidateVisual();
            return;
        }

        // Bound values may be NaN before the slots are measured.
        if (double.IsNaN(StartLeft) || double.IsNaN(StartTop) || double.IsNaN(EndLeft) || double.IsNaN(EndTop))
        {
            InvalidateVisual();
            return;
        }

        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        if (points.Length < 4)
        {
            InvalidateVisual();
            return;
        }

        // Size the box to all four control points; the endpoints alone clip the curve's bulge.
        foreach (var (x, y) in points)
        {
            _curve.Add(new Point(x, y));
        }

        _hasBounds = WorkflowLinkBounds.Apply(this, _curve, out _originX, out _originY);
        InvalidateVisual();
    }

    #endregion

    #region Render

    /// <inheritdoc />
    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);

        // Draw nothing until the model says visible and the geometry has been measured.
        if (!_hasBounds || _link is not { } link || _curve.Count < 4)
        {
            return;
        }

        // Publish the curve the surface hit-tests against — the same control points as the drawing below, in
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(link, LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull));

        var thickness = LineThickness;
        var brush = new SolidColorBrush(LineColor);
        var pen = IsVirtual || IsVirtualLink(link)
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4d, 2d], 0d) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, BuildCurve());
    }

    // Drag preview: both endpoints are parentless placeholder slots.
    private static bool IsVirtualLink(ILinkComponent link)
        => link.Sender.Parent is null && link.Receiver.Parent is null;

    // Extension point: the control points set the curve's shape. Core derives them from each port's own edge
    // (LinkCurve.LinkCurvePoints) — keep that source if you replace the drawing.
    private Geometry BuildCurve()
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(_curve[0].X - _originX, _curve[0].Y - _originY),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            new Point(_curve[1].X - _originX, _curve[1].Y - _originY),
            new Point(_curve[2].X - _originX, _curve[2].Y - _originY),
            new Point(_curve[3].X - _originX, _curve[3].Y - _originY),
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    // Extension point: change the second argument if one view no longer draws exactly one link.
    private void PublishCurve(ILinkComponent link, LinkCurve curve)
    {
        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        link.PublishCurve(curve, this);
    }

    #endregion
}
