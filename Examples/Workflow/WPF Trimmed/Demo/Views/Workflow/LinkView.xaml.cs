// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The view only paints: it publishes its curve for hit-testing and handles no input itself. The hover
/// highlight below is <b>this demo's</b> reading of <see cref="LinkInteraction.HoverChanged"/> — the template
/// ships the same view without it (see the repo's layering rule), so delete these members to get that back.
/// </summary>
public partial class LinkView : UserControl
{
    // Extension point: the least horizontal pull of the two control points. Keep it in step with the curve
    // that is published for hit-testing below.
    private const double MinimumPull = 40;

    // The link this view last published a curve to; retracted on rebind so a pooled view cannot leave a
    // stale curve answering for a link that no longer draws here.
    private IWorkflowLinkViewModel? _publishedLink;

    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);

        DataContextChanged += OnDataContextChanged;
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(LinkView), new PropertyMetadata(true, OnRenderChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(LinkView), new PropertyMetadata((Color)ColorConverter.ConvertFromString("#DDFFFFFF"), OnRenderChanged));
    public static readonly DependencyProperty HighlightColorProperty =
        DependencyProperty.Register(nameof(HighlightColor), typeof(Color), typeof(LinkView), new PropertyMetadata((Color)ColorConverter.ConvertFromString("#FFFFFFFF"), OnRenderChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    /// <summary>The soft light the link turns into while it is hovered.</summary>
    public Color HighlightColor { get => (Color)GetValue(HighlightColorProperty); set => SetValue(HighlightColorProperty, value); }

    /// <inheritdoc />
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LinkView)d).InvalidateVisual();

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Retract the previous link's curve, then let the next draw publish the new one.
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
        InvalidateVisual();
        ResubscribeHub();
    }

    // VeloxDev customization: the hover highlight. The hub only reports whose turn it is; each view decides
    // whether it lights up, so mutual exclusion needs no bookkeeping. The subscription follows the data
    // context — a pooled view gets recycled and rebound without being unloaded, and the view lives shorter
    // than the tree.
    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeHub()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (ReferenceEquals(link, _inputLink)) return;

        UnsubscribeHub();
        if (link?.GetHelper() is not IWorkflowInputEvents events) return;

        _inputLink = link;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;
        events.Input.KeyDown += OnKeyDown;
    }

    private void UnsubscribeHub()
    {
        if (_inputLink?.GetHelper() is not IWorkflowInputEvents events) return;

        events.Input.PointerEntered -= OnPointerEntered;
        events.Input.PointerExited -= OnPointerExited;
        events.Input.KeyDown -= OnKeyDown;
        _inputLink = null;
    }

    private void OnPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, WorkflowPointerExitedEventArgs e) => IsHighlighted = false;

    // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删、怎么删由这里写。
    private void OnKeyDown(object? sender, WorkflowKeyDownEventArgs e)
    {
        if (e.Key != WorkflowKey.Delete) return;
        if (e.Handle.PreventDefault) return;
        if ((DataContext as IWorkflowLinkViewModel) is { } link && link.DeleteCommand.CanExecute(null)) link.DeleteCommand.Execute(null);
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    #endregion

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;
        if (DataContext is IWorkflowLinkViewModel link && !link.IsRenderReady()) return;

        // Publish the curve the surface hit-tests against: same control points as BuildCurve, same
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(LinkCurve.BuildLinkCubic(DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, MinimumPull));

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = 2;
        var geometry = BuildCurve(DataContext as IWorkflowLinkViewModel);

        // Extension point: this is the hover feedback. Swap the halo's width or alpha, or HighlightColor,
        // to restyle the highlighted link.
        if (IsHighlighted)
        {
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(AtAlpha(color, 0.18)), thickness + 8)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            }, geometry);
        }

        var brush = new SolidColorBrush(color);
        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, geometry);
    }

    // Extension point: the hover halo's opacity (0..1); 0 turns the glow off.
    private static Color AtAlpha(Color color, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    // Extension point: the control points set the curve's shape. Core derives them from each port's own edge
    // (LinkCurve.LinkCurvePoints) — keep that source if you replace the drawing.
    private Geometry BuildCurve(IWorkflowLinkViewModel? link)
    {
        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        var figure = new PathFigure
        {
            StartPoint = new Point(points[0].X, points[0].Y),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            new Point(points[1].X, points[1].Y),
            new Point(points[2].X, points[2].Y),
            new Point(points[3].X, points[3].Y),
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    // Extension point: change the second argument if one view no longer draws exactly one link.
    private void PublishCurve(LinkCurve curve)
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (!ReferenceEquals(_publishedLink, link))
        {
            _publishedLink?.PublishCurve(null);
            _publishedLink = link;
        }

        if (link is not null)
        {
            link.PublishCurve(curve, this);
        }
    }

    #endregion
}
