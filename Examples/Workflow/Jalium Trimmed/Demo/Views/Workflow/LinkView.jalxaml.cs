// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The view only paints: it publishes its curve for hit-testing and handles no input itself. The hover glow
/// below is <b>this demo's</b> — the template ships the same view without it, so delete those members to get
/// that back.
/// </summary>
public partial class LinkView : UserControl
{
    // Extension point: the least horizontal pull of the two control points. Keep it in step with the curve
    // that is published for hit-testing below.
    private const double MinimumPull = 40;

    // VeloxDev customization: the glow's colour and how far it spreads. It reads as the line being lit rather
    // than recoloured — the resting stroke is already near white.
    private static readonly Color GlowColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private const double GlowSpread = 6;

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

    /// <summary>The canvas-local X of the start end.</summary>
    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the start end.</summary>
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local X of the end end.</summary>
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>The canvas-local Y of the end end.</summary>
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(LinkView), new PropertyMetadata(0d, OnRenderChanged));

    /// <summary>Whether the link should be drawn at all.</summary>
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(LinkView), new PropertyMetadata(true, OnRenderChanged));

    /// <summary>Whether this is the drag preview rather than a real link.</summary>
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnRenderChanged));

    /// <summary>The stroke colour.</summary>
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(LinkView),
            new PropertyMetadata(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF), OnRenderChanged));

    /// <summary>The canvas-local X of the start end.</summary>
    public double StartLeft { get => GetValue(StartLeftProperty) is double value ? value : 0d; set => SetValue(StartLeftProperty, value); }

    /// <summary>The canvas-local Y of the start end.</summary>
    public double StartTop { get => GetValue(StartTopProperty) is double value ? value : 0d; set => SetValue(StartTopProperty, value); }

    /// <summary>The canvas-local X of the end end.</summary>
    public double EndLeft { get => GetValue(EndLeftProperty) is double value ? value : 0d; set => SetValue(EndLeftProperty, value); }

    /// <summary>The canvas-local Y of the end end.</summary>
    public double EndTop { get => GetValue(EndTopProperty) is double value ? value : 0d; set => SetValue(EndTopProperty, value); }

    /// <summary>Whether the link should be drawn at all.</summary>
    public bool CanRender { get => GetValue(CanRenderProperty) is true; set => SetValue(CanRenderProperty, value); }

    /// <summary>Whether this is the drag preview rather than a real link.</summary>
    public bool IsVirtual { get => GetValue(IsVirtualProperty) is true; set => SetValue(IsVirtualProperty, value); }

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => GetValue(LineColorProperty) is Color color ? color : Colors.White;
        set => SetValue(LineColorProperty, value);
    }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LinkView)d).InvalidateVisual();

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // Retract the previous link's curve, then let the next draw publish the new one.
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
        InvalidateVisual();
        ResubscribeInput();
    }

    #endregion

    #region 悬停高亮与删除（本 demo 专属）

    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private bool _lit;
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeInput()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (ReferenceEquals(link, _inputLink))
        {
            return;
        }

        UnsubscribeInput();
        if (link?.GetHelper() is not IInputEvents events)
        {
            return;
        }

        _inputLink = link;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;
        events.Input.KeyDown += OnKeyDown;
    }

    private void UnsubscribeInput()
    {
        if (_inputLink?.GetHelper() is not IInputEvents events)
        {
            return;
        }

        events.Input.PointerEntered -= OnPointerEntered;
        events.Input.PointerExited -= OnPointerExited;
        events.Input.KeyDown -= OnKeyDown;
        _inputLink = null;
    }

    private void OnPointerEntered(object? sender, PointerEnteredEventArgs e)
    {
        _lit = true;
        InvalidateVisual();
    }

    private void OnPointerExited(object? sender, PointerExitedEventArgs e)
    {
        _lit = false;
        InvalidateVisual();
    }

    // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删、怎么删由这里写。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (e.Key != InputKey.Delete || e.Handle.PreventDefault)
        {
            return;
        }

        if (DataContext is IWorkflowLinkViewModel link && link.DeleteCommand.CanExecute(null))
        {
            link.DeleteCommand.Execute(null);
        }
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

    /// <inheritdoc />
    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);

        if (!CanRender)
        {
            return;
        }

        var link = DataContext as IWorkflowLinkViewModel;
        if (link is not null && !link.IsRenderReady())
        {
            return;
        }

        // 自盒化：Jalium 的渲染器按 RenderSize 裁剪子元素、不看画了什么，画到盒外的部分会被**静默丢掉**
        // （见 WorkflowLinkBounds 的说明）。所以先把盒子挪到这条线自己的包围盒上，再用盒原点把坐标烘回元素局部。
        if (!WorkflowLinkBounds.Apply(this, [new Point(StartLeft, StartTop), new Point(EndLeft, EndTop)], out var originX, out var originY))
        {
            return;
        }

        // Publish the curve the surface hit-tests against — the same control points as the drawing below, in
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull));

        const double thickness = 2d;
        var geometry = BuildCurve(link, originX, originY);

        // VeloxDev customization: the hover glow. Delete this block to ship without it.
        if (_lit)
        {
            var halo = Color.FromArgb(0x40, GlowColor.R, GlowColor.G, GlowColor.B);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(halo), thickness + GlowSpread), geometry);
        }

        var brush = new SolidColorBrush(LineColor);
        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4d, 2d], 0d) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, geometry);
    }

    // Extension point: the control points set the curve's shape. Core derives them from each port's own edge
    // (LinkCurve.LinkCurvePoints) — keep that source if you replace the drawing.
    private Geometry BuildCurve(IWorkflowLinkViewModel? link, double originX, double originY)
    {
        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, MinimumPull);
        var figure = new PathFigure
        {
            StartPoint = new Point(points[0].X - originX, points[0].Y - originY),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new BezierSegment(
            new Point(points[1].X - originX, points[1].Y - originY),
            new Point(points[2].X - originX, points[2].Y - originY),
            new Point(points[3].X - originX, points[3].Y - originY),
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
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
