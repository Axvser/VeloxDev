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
    private IWorkflowLinkViewModel? _publishedLink;

    private IWorkflowLinkViewModel? _link;
    private IWorkflowSlotViewModel? _sender;
    private IWorkflowSlotViewModel? _receiver;
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

    /// <summary>Whether the link should be drawn at all; the model's own visibility is required as well.</summary>
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnRenderChanged));

    /// <summary>The stroke colour.</summary>
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(TemplateClass),
            new PropertyMetadata(ParseColor("TemplateLinkColor", Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), OnRenderChanged));

    /// <summary>Whether the link should be drawn at all.</summary>
    public bool CanRender { get => GetValue(CanRenderProperty) is true; set => SetValue(CanRenderProperty, value); }

    /// <summary>The stroke colour.</summary>
    public Color LineColor
    {
        get => GetValue(LineColorProperty) is Color color ? color : Colors.White;
        set => SetValue(LineColorProperty, value);
    }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).Refresh();

    // 颜色 token 在生成时被替换成字面量；解不出来就退回默认色。
    private static Color ParseColor(string text, Color fallback)
        => ColorConverter.ConvertFromString(text) is Color color ? color : fallback;

    #endregion

    #region Model binding

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        // 换绑：先把上一条的曲线收回，免得池化视图留一条陈旧曲线替别人回答命中。
        _publishedLink?.PublishCurve(null);
        _publishedLink = null;
        Rebind();
    }

    private void Rebind()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (!ReferenceEquals(link, _link))
        {
            Unhook();

            _link = link;
            _sender = link?.Sender;
            _receiver = link?.Receiver;
            _modelChanged = (_, _) => ScheduleRefresh();

            // 订两端**以及它们各自的节点**：节点动的时候，槽的锚点是适配器在稍后的
            // DispatcherPriority.Render 那一拍重测出来的 —— 只订槽会漏掉「节点已经在动、锚点还没写回」
            // 的那一段（症状是连线整条冻在拖动前的几何上）。
            Watch(_sender);
            Watch(_receiver);
            Watch(link);
        }

        Refresh();
    }

    private void Watch(IWorkflowViewModel? model)
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

        // 节点的锚点变了，它这张卡上的每个槽都要重新量一次 —— 所以也订节点。
        var node = model is IWorkflowSlotViewModel slot ? slot.Parent : null;
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

    // 排到 Render 优先级的下一拍再量：槽锚点的重测也排在那一拍，先来后到保证读到的是重测后的值。
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

        var start = link.Sender.Anchor;
        var end = link.Receiver.Anchor;
        var points = LinkCurve.LinkCurvePoints(link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, MinimumPull);
        if (points.Length < 4)
        {
            InvalidateVisual();
            return;
        }

        // 四个控制点一起进盒子 —— 曲线的凸包由它们界定；只按两端点算盒子，会把鼓出端点矩形的那一段切掉。
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

        // 模型没让画、或者几何还没量过（盒子也还没摆）—— 一笔都不画。
        // 这一步挡掉的正是「刚建好那一瞬从原点画一条」：那时模型还没说这条线可见。
        if (!_hasBounds || _link is not { } link || _curve.Count < 4)
        {
            return;
        }

        var start = link.Sender.Anchor;
        var end = link.Receiver.Anchor;

        // Publish the curve the surface hit-tests against — the same control points as the drawing below, in
        // canvas-local space. Replace this together with BuildCurve if you change the shape.
        PublishCurve(link, LinkCurve.BuildLinkCubic(
            link, start.Horizontal, start.Vertical, end.Horizontal, end.Vertical, MinimumPull));

        const double thickness = TemplateLinkThickness;
        var brush = new SolidColorBrush(LineColor);
        var pen = IsVirtualLink(link)
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4d, 2d], 0d) }
            : new Pen(brush, thickness);

        ctx.DrawGeometry(null, pen, BuildCurve());
    }

    // 拖拽预览：两端都是占位槽（没有父节点），也就是一条还没落到任何卡片上的线。
    private static bool IsVirtualLink(IWorkflowLinkViewModel link)
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
    private void PublishCurve(IWorkflowLinkViewModel link, LinkCurve curve)
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
