using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// The hover highlight below is <b>this demo's</b> — it listens to this link's own input events and decides
/// how lit it looks; the template ships the same view without it (see the repo's layering rule), so delete
/// these members to get that back.
/// </summary>
public partial class LinkView : Control
{
    // Horizontal pull shared by the drawn curve and the hit-test curve, so both describe the same shape.
    private const double PullMinimum = 40;

    // The flattened curve published to the link's helper; the surface hit-tests this exact shape
    // (see ILinkHitTestable / LinkHitTestEx).
    private LinkCurve? _curve;

    public LinkView()
    {
        InitializeComponent();
        // The link is interactive by default: hit-testable so the hover can find it, focusable so Delete
        // reaches the adapter's key route. Only the painted stroke answers — this control draws a geometry
        // and has no background, so the framework's hit test is the stroke, not the canvas-sized box.
        IsHitTestVisible = true;
        Focusable = true;

        // 取焦点的连带代价：本视图是整块画布大小，Avalonia 的 BringIntoViewOnFocusChange 会在焦点落到
        // 它身上时替它请求「滚进视口」，鼠标一碰到线画布就跳一段。在发源地吃掉这条请求 —— 节点卡里
        // 输入框被聚焦时照样滚进视口，作用域刻意只收在这里。
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        RefreshGeometry();
    }

    #region Avalonia property definitions

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<LinkView, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<LinkView, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<LinkView, Color>(nameof(LineColor), Color.Parse("#DDFFFFFF"));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(LineThickness), 2.0);

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<LinkView, bool>(nameof(IsHighlighted), false);

    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<LinkView, Color>(nameof(HighlightColor), Color.Parse("#FFFFFFFF"));

    public double StartLeft
    {
        get => GetValue(StartLeftProperty);
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => GetValue(StartTopProperty);
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => GetValue(EndLeftProperty);
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => GetValue(EndTopProperty);
        set => SetValue(EndTopProperty, value);
    }

    public bool CanRender
    {
        get => GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    public bool IsVirtual
    {
        get => GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    public Color LineColor
    {
        get => GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    // Set by this view while the pointer is over this link (see ResubscribeHub); the render below repaints on change.
    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    // Extension point: the white glow shown while this link is highlighted. White is deliberate — the line
    // reads as lit rather than recoloured, and the halo drawn around it is what makes it a glow.
    public Color HighlightColor
    {
        get => GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    static LinkView()
    {
        AffectsRender<LinkView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, IsHighlightedProperty, HighlightColorProperty);
    }

    #endregion

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The curve follows the endpoints, and the control is pooled: a rebound control must retract the old
        // link's curve before it publishes its own, or the old link answers the pointer at the new control.
        if (change.Property == DataContextProperty
            && change.OldValue is IWorkflowLinkViewModel old && !ReferenceEquals(old, change.NewValue))
        {
            old.PublishCurve(null);
        }

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty
            || change.Property == DataContextProperty)
        {
            RefreshGeometry();
            ResubscribeHub();
        }
    }

    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeHub()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (ReferenceEquals(link, _inputLink)) return;

        UnsubscribeHub();
        if (link?.GetHelper() is not IInputEvents events) return;

        _inputLink = link;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;
        events.Input.KeyDown += OnKeyDown;
    }

    private void UnsubscribeHub()
    {
        if (_inputLink?.GetHelper() is not IInputEvents events) return;

        events.Input.PointerEntered -= OnPointerEntered;
        events.Input.PointerExited -= OnPointerExited;
        events.Input.KeyDown -= OnKeyDown;
        _inputLink = null;
    }

    private void OnPointerEntered(object? sender, PointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, PointerExitedEventArgs e) => IsHighlighted = false;

    // VeloxDev customization: 删除也是宿主的 —— 路由把这次按键交过来（target 就是这条线），删不删、怎么删由这里写。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (e.Key != InputKey.Delete) return;
        if (e.Handle.PreventDefault) return;
        if ((DataContext as IWorkflowLinkViewModel) is { } link && link.DeleteCommand.CanExecute(null)) link.DeleteCommand.Execute(null);
    }

    // The one geometry build: it feeds both the drawn curve and the published hit-test curve, so the two can
    // never describe different shapes.
    private void RefreshGeometry()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        _curve = LinkCurve.BuildLinkCubic(link, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        link?.PublishCurve(_curve, this);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? LineThickness + 1.5 : LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4.0, 2.0], 0) }
            : new Pen(brush, thickness);

        var geometry = BuildCurve(DataContext as IWorkflowLinkViewModel);

        // Extension point: the halo painted under the line while highlighted. Widen or fade the pen here.
        if (IsHighlighted)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            context.DrawGeometry(null, glowPen, geometry);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    // Extension point: the control points set the curve's shape. Core derives them from each port's own edge
    // (LinkCurve.LinkCurvePoints) — keep that source if you replace the drawing.
    private StreamGeometry BuildCurve(IWorkflowLinkViewModel? link)
    {
        var points = LinkCurve.LinkCurvePoints(link, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, points[0].Y), false);
            ctx.CubicBezierTo(
                new Point(points[1].X, points[1].Y),
                new Point(points[2].X, points[2].Y),
                new Point(points[3].X, points[3].Y));
        }

        return geometry;
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };
}