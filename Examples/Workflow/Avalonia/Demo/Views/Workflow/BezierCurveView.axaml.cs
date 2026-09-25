using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class BezierCurveView : Control
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;

        // 与 Polyline 那条同款：悬停取焦点（Delete 需要）会连带触发 ScrollViewer 的「把焦点元素滚进视口」
        // （BringIntoViewOnFocusChange 默认 true），而本视图是整块画布大小 ⇒ 鼠标碰到线画布就跳一段。
        // 在发源地吃掉这条请求，节点卡的自动滚进视口不受影响。
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        CurveSelectionManager.SelectionChanged += owner =>
        {
            if (owner != this && IsSelected)
                IsSelected = false;
        };
    }

    #region Avalonia property definitions

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsSelected), false);

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<BezierCurveView, Color>(nameof(LineColor), Color.Parse("#DDFFFFFF"));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(LineThickness), 2.0);

    public static readonly StyledProperty<IList<double>> DashArrayProperty =
        AvaloniaProperty.Register<BezierCurveView, IList<double>>(nameof(DashArray), [0d]);

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

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
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

    public IList<double> DashArray
    {
        get => GetValue(DashArrayProperty);
        set => SetValue(DashArrayProperty, value);
    }

    static BezierCurveView()
    {
        AffectsRender<BezierCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, DashArrayProperty, IsSelectedProperty);
    }

    #endregion

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var geometry = CreateBezierGeometry();
        if (geometry == null) return;

        DrawBezierLine(context, geometry);
    }

    private void DrawBezierLine(DrawingContext context, StreamGeometry geometry)
    {
        var color = IsSelected ? Colors.OrangeRed : LineColor;
        var thickness = IsSelected ? LineThickness + 1.5 : LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        Pen pen;
        if (IsVirtual || (DashArray != null && DashArray.Count > 0))
        {
            var dashArray = IsVirtual ? [4.0, 2.0] : DashArray;
            pen = new Pen(brush, thickness) { DashStyle = new DashStyle(dashArray, 0) };
        }
        else
        {
            pen = new Pen(brush, thickness);
        }

        if (IsSelected)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            context.DrawGeometry(null, glowPen, geometry);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private StreamGeometry? CreateBezierGeometry()
    {
        var diffx = EndLeft - StartLeft;

        // Compute the control points (a cubic Bézier curve needs two control points)
        var cp1 = new Point(StartLeft + diffx * 0.3, StartTop);
        var cp2 = new Point(EndLeft - diffx * 0.3, EndTop);

        var startPoint = new Point(StartLeft, StartTop);
        var endPoint = new Point(EndLeft, EndTop);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false);
            ctx.CubicBezierTo(cp1, cp2, endPoint);
        }
        return geometry;
    }

    #region Interaction

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        IsSelected = true;
        CurveSelectionManager.Select(this);

        // 与 Polyline 那条同款：选中是「上色」，Delete 要的是键盘焦点，两者必须同时发生，
        // 否则 OnKeyDown 收不到键、得先点一下线才拿得到焦点。
        Focus();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CurveSelectionManager.Deselect(this);
        IsSelected = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        bool over = HitTestCurve(pt);
        if (over && !IsSelected)
        {
            IsSelected = true;
            CurveSelectionManager.Select(this);
            Focus();
        }
        else if (!over && IsSelected)
        {
            CurveSelectionManager.Deselect(this);
            IsSelected = false;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        // 只有落在画出来的线上的右键才算这条线的：沿 40 段折线逼近判距（半径 6）。
        // 框架只把指针事件送给画出来的描边，这条判据与它同带宽；留着它是为了命中面被改粗时也不在空白处弹菜单
        if (!HitTestCurve(e.GetPosition(this))) return;

        // 未选中先选中：菜单里的删除作用于当前这条线。悬停选中与它无关，菜单弹出后指针就落到菜单上
        IsSelected = true;
        CurveSelectionManager.Select(this);
        Focus();

        _menu ??= BuildMenu();
        _menu.Open(this);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsSelected)
        {
            DeleteLink();
            e.Handled = true;
        }
    }

    // 菜单只有一项，且不绑命令：视图会被池化改绑给另一条链接，菜单项在点击那一刻才去读 DataContext
    private ContextMenu? _menu;

    private ContextMenu BuildMenu()
    {
        var item = new MenuItem { Header = "删除连线" };
        item.Click += (_, _) => DeleteLink();

        return new ContextMenu { Items = { item } };
    }

    private void DeleteLink()
    {
        if (DataContext is IWorkflowLinkViewModel vm)
            vm.DeleteCommand.Execute(null);
    }

    private bool HitTestCurve(Point pt)
    {
        const double hitRadius = 6.0;
        const int segments = 40;

        var diffx = EndLeft - StartLeft;
        var cp1 = new Point(StartLeft + diffx * 0.3, StartTop);
        var cp2 = new Point(EndLeft - diffx * 0.3, EndTop);
        var p0 = new Point(StartLeft, StartTop);
        var p3 = new Point(EndLeft, EndTop);

        Point Eval(double t)
        {
            double mt = 1 - t;
            return new Point(
                mt * mt * mt * p0.X + 3 * mt * mt * t * cp1.X + 3 * mt * t * t * cp2.X + t * t * t * p3.X,
                mt * mt * mt * p0.Y + 3 * mt * mt * t * cp1.Y + 3 * mt * t * t * cp2.Y + t * t * t * p3.Y);
        }

        var prev = Eval(0);
        for (int i = 1; i <= segments; i++)
        {
            var next = Eval((double)i / segments);
            if (DistanceToSegment(pt, prev, next) <= hitRadius) return true;
            prev = next;
        }
        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 0.0001) return new Vector(p.X - a.X, p.Y - a.Y).Length;
        double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        var proj = new Point(a.X + t * ab.X, a.Y + t * ab.Y);
        return new Vector(p.X - proj.X, p.Y - proj.Y).Length;
    }

    #endregion
}