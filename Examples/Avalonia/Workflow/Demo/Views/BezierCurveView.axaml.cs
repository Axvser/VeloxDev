using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views;

public partial class BezierCurveView : UserControl
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        ZIndex = -100;
    }

    public static readonly StyledProperty<Anchor> StartAnchorProperty =
        AvaloniaProperty.Register<BezierCurveView, Anchor>(
            "StartAnchor",
            defaultValue: new Anchor(),
            coerce: (_, v) => v,
            enableDataValidation: false);

    public static readonly StyledProperty<Anchor> EndAnchorProperty =
        AvaloniaProperty.Register<BezierCurveView, Anchor>(
            "EndAnchor",
            defaultValue: new Anchor(),
            coerce: (_, v) => v,
            enableDataValidation: false);

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(
            "CanRender",
            defaultValue: true,
            coerce: (_, v) => v,
            enableDataValidation: false);

    public Anchor StartAnchor
    {
        get => GetValue(StartAnchorProperty);
        set => SetValue(StartAnchorProperty, value);
    }

    public Anchor EndAnchor
    {
        get => GetValue(EndAnchorProperty);
        set => SetValue(EndAnchorProperty, value);
    }

    public bool CanRender
    {
        get => GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    static BezierCurveView()
    {
        StartAnchorProperty.Changed.AddClassHandler<BezierCurveView>((x, e) =>
            x.OnStartAnchorChanged(e.OldValue as Anchor, e.NewValue as Anchor));

        EndAnchorProperty.Changed.AddClassHandler<BezierCurveView>((x, e) =>
            x.OnEndAnchorChanged(e.OldValue as Anchor, e.NewValue as Anchor));

        CanRenderProperty.Changed.AddClassHandler<BezierCurveView>((x, e) =>
            x.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue));
    }

    private void OnStartAnchorChanged(Anchor oldValue, Anchor newValue)
    {
        InvalidateVisual();
    }

    private void OnEndAnchorChanged(Anchor oldValue, Anchor newValue)
    {
        InvalidateVisual();
    }

    private void OnCanRenderChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender)
            return;

        // 计算差距
        var diffx = EndAnchor.Left - StartAnchor.Left;
        var diffy = EndAnchor.Top - StartAnchor.Top;

        // 基于点位差距计算控制点
        var cp1 = new Point(
            StartAnchor.Left + diffx * 0.618,
            StartAnchor.Top + diffy * 0.1);
        var cp2 = new Point(
            EndAnchor.Left - diffx * 0.618,
            EndAnchor.Top - diffy * 0.1);

        // 创建贝塞尔曲线几何图形
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(StartAnchor.Left, StartAnchor.Top), false);
            ctx.CubicBezierTo(cp1, cp2, new Point(EndAnchor.Left, EndAnchor.Top));
            ctx.EndFigure(false);
        }

        // 渲染连接线
        var pen = new Pen(Brushes.Cyan, 2);
        context.DrawGeometry(null, pen, geometry);
    }
}