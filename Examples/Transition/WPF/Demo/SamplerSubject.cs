using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace Demo;

/// <summary>
/// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**依赖属性**，类型与产物完全一致。
/// </summary>
/// <remarks>
/// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员
/// （依赖属性），采样器写它时走的是真实的属性通道，验收再从这个属性读回来 —— 于是断言依据的是**界面上那个
/// 控件实际持有的值**，而不是一个屏幕外的对象。
/// <para>
/// 每条属性都注册成 <c>AffectsRender</c>，所以采样器一写，WPF 就自己安排重绘，不必在 setter 里手动
/// <c>InvalidateVisual</c>。每一格只被它那条采样器写，<see cref="Kind"/> 告诉这一格该把哪条属性画出来。
/// </para>
/// <para>
/// <b>绘制是有标尺的。</b>位移类端点跑到 220，格子只有 96×62，按原值画会一步跨出格子被裁掉 —— 那看上去
/// 和"没动"一模一样。所以位置与尺寸在**绘制反应里**乘一个固定缩放，而属性本身持有的仍是原值：载荷读的是
/// 属性，于是断言的是原值，缩放只影响"怎么画"。
/// </para>
/// </remarks>
internal sealed class SamplerSubject : FrameworkElement
{
    /// <summary>位移与尺寸的像素缩放。这一组端点最大分量 220，格子留出的行程约 38 像素。</summary>
    private const double Scale = 0.14d;

    private const double BaseLeft = 6d;
    private const double BaseTop = 6d;
    private const double BaseWidth = 26d;
    private const double BaseHeight = 18d;

    /// <summary>这一格显示哪一条采样器的产物。构造时定一次，此后不变。</summary>
    internal required string Kind { get; init; }

    public static readonly DependencyProperty FillProperty =
        Register(nameof(Fill), typeof(Brush), null);

    public static readonly DependencyProperty TintProperty =
        Register(nameof(Tint), typeof(Color), Colors.Gray);

    public static readonly DependencyProperty CornersProperty =
        Register(nameof(Corners), typeof(CornerRadius), new CornerRadius(2));

    public static readonly DependencyProperty ShadowProperty =
        Register(nameof(Shadow), typeof(Effect), null);

    public static readonly DependencyProperty Anchor3DProperty =
        Register(nameof(Anchor3D), typeof(Point3D), default(Point3D));

    public static readonly DependencyProperty AnchorProperty =
        Register(nameof(Anchor), typeof(Point), default(Point));

    public static readonly DependencyProperty BoundsProperty =
        Register(nameof(Bounds), typeof(Rect), default(Rect));

    public static readonly DependencyProperty ExtentProperty =
        Register(nameof(Extent), typeof(Size), default(Size));

    public static readonly DependencyProperty InsetProperty =
        Register(nameof(Inset), typeof(Thickness), default(Thickness));

    public static readonly DependencyProperty RenderProperty =
        Register(nameof(Render), typeof(Transform), null);

    public static readonly DependencyProperty Axis3DProperty =
        Register(nameof(Axis3D), typeof(Vector3D), default(Vector3D));

    public static readonly DependencyProperty SlopeProperty =
        Register(nameof(Slope), typeof(Vector), default(Vector));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public CornerRadius Corners { get => (CornerRadius)GetValue(CornersProperty); set => SetValue(CornersProperty, value); }

    public Effect? Shadow { get => (Effect?)GetValue(ShadowProperty); set => SetValue(ShadowProperty, value); }

    public Point3D Anchor3D { get => (Point3D)GetValue(Anchor3DProperty); set => SetValue(Anchor3DProperty, value); }

    public Point Anchor { get => (Point)GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }

    public Rect Bounds { get => (Rect)GetValue(BoundsProperty); set => SetValue(BoundsProperty, value); }

    public Size Extent { get => (Size)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    /// <summary>厚度。名字不叫 <c>Margin</c>：<see cref="FrameworkElement"/> 已经占用了那个名字。</summary>
    public Thickness Inset { get => (Thickness)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    public Transform? Render { get => (Transform?)GetValue(RenderProperty); set => SetValue(RenderProperty, value); }

    public Vector3D Axis3D { get => (Vector3D)GetValue(Axis3DProperty); set => SetValue(Axis3DProperty, value); }

    public Vector Slope { get => (Vector)GetValue(SlopeProperty); set => SetValue(SlopeProperty, value); }

    public SamplerSubject()
    {
        Width = BaseWidth;
        Height = BaseHeight;
        Canvas.SetLeft(this, BaseLeft);
        Canvas.SetTop(this, BaseTop);
    }

    /// <summary>
    /// 采样器写进来之后，把这个值**换算成格子里画得下的样子**。
    /// </summary>
    /// <remarks>
    /// 这是"反应"，不是"值"：属性持有的是采样器写下的原值（载荷读的就是它），这里只把它落到像素上。
    /// 三维的点与向量取 X/Y 投影，Z 只出现在载荷里。
    /// </remarks>
    internal void Reposition()
    {
        var (x, y) = Kind switch
        {
            SamplerProbe.Kinds.PointSampler => (Anchor.X, Anchor.Y),
            SamplerProbe.Kinds.Point3DSampler => (Anchor3D.X, Anchor3D.Y),
            SamplerProbe.Kinds.VectorSampler => (Slope.X, Slope.Y),
            SamplerProbe.Kinds.Vector3DSampler => (Axis3D.X, Axis3D.Y),
            SamplerProbe.Kinds.RectSampler => (Bounds.X, Bounds.Y),
            _ => (0d, 0d),
        };

        Canvas.SetLeft(this, BaseLeft + x * Scale);
        Canvas.SetTop(this, BaseTop + y * Scale);

        var (width, height) = Kind switch
        {
            SamplerProbe.Kinds.SizeSampler => (Extent.Width * Scale, Extent.Height * Scale),
            SamplerProbe.Kinds.RectSampler => (Bounds.Width * Scale, Bounds.Height * Scale),
            _ => (BaseWidth, BaseHeight),
        };

        // 画不出 0 宽的东西；"停在下界"这件事本身已经由载荷里的数字如实报告了。
        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);

        Margin = new Thickness(Inset.Left * Scale, Inset.Top * Scale, Inset.Right * Scale, Inset.Bottom * Scale);
        Effect = Shadow;
        RenderTransform = Render switch
        {
            TranslateTransform translate => new TranslateTransform(translate.X * Scale, translate.Y * Scale),
            Transform transform => transform,
            _ => null,
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, Math.Max(1d, ActualWidth), Math.Max(1d, ActualHeight));
        var radius = Math.Max(
            Math.Max(Corners.TopLeft, Corners.TopRight),
            Math.Max(Corners.BottomLeft, Corners.BottomRight));

        drawingContext.DrawRoundedRectangle(Fill ?? new SolidColorBrush(Tint), null, bounds, radius, radius);
    }

    /// <summary>
    /// 注册一条依赖属性。每条属性写入都会重算这一格的显示 —— 不只是位置类的那些：厚度、阴影、变换也是在
    /// <see cref="Reposition"/> 里落到控件上的。重算是幂等的，多算一次不花钱。
    /// </summary>
    /// <remarks>
    /// <c>AffectsRender</c> 是这里的关键：采样器一写，WPF 自己安排重绘 —— 不必手写 <c>InvalidateVisual</c>，
    /// 而且重绘是属性系统给的保证，不是我们记得去调。
    /// </remarks>
    private static DependencyProperty Register(string name, Type type, object? defaultValue)
        => DependencyProperty.Register(
            name,
            type,
            typeof(SamplerSubject),
            new FrameworkPropertyMetadata(
                defaultValue,
                FrameworkPropertyMetadataOptions.AffectsRender,
                static (target, _) => ((SamplerSubject)target).Reposition()));
}
