using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;

namespace Demo;

/// <summary>
/// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**依赖属性**，类型与产物完全一致。
/// </summary>
/// <remarks>
/// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员
/// （依赖属性），采样器写它时走的是真实的属性通道，验收再从这个属性读回来 —— 于是断言依据的是**界面上那个
/// 控件实际持有的值**，而不是一个屏幕外的对象。
/// <para>
/// 每条属性都注册成 <c>AffectsRender</c>，所以采样器一写，Jalium 就自己安排重绘，不必在 setter 里手动
/// <c>InvalidateVisual</c>。每一格只被它那条采样器写，<see cref="Kind"/> 告诉这一格该把哪条属性画出来。
/// </para>
/// <para>
/// <b>绘制是有标尺的。</b>位移类端点跑到 220，格子只有 76×50，按原值画会一步跨出格子被裁掉 —— 那看上去
/// 和"没动"一模一样。所以位置、尺寸与边距在**绘制反应里**乘一个固定缩放，而属性本身持有的仍是原值：载荷读的
/// 是属性，于是断言的是原值，缩放只影响"怎么画"。
/// </para>
/// </remarks>
internal sealed class SamplerSubject : FrameworkElement
{
    /// <summary>
    /// 位移、尺寸与边距的像素缩放。
    /// </summary>
    /// <remarks>
    /// 标尺由这一组端点里最长的行程定出来，而不是拍一个好看的数。这一组里最远的一跳是纵向
    /// 220（PointSampler / TransformSampler 的 Y），而被写对象自己占掉 13 高、基准线在 y=6，格子只剩
    /// 50 − 6 − 13 = 31 像素的纯行程 —— 单看位移，<c>s ≤ 31 / 220 ≈ 0.14</c> 就够。
    /// <para>
    /// 真正卡住的是 <c>RectSampler</c>：它在终点同时做到"下移 220"和"高 150"，而尺寸通道与位移共用同一个
    /// 缓动时间，所以两者会一起走到底。要求 <c>6 + (220 + 150)·s ≤ 50</c>，即 <c>s ≤ 0.119</c>。
    /// 取 <c>0.11</c> 再留一点余量：演出用的 Back.Out 会过冲到 1.10，最坏一格是
    /// <c>6 + (220×1.10 + 150)×0.11 = 49.1 ≤ 50</c>，仍在格内。
    /// </para>
    /// <para>
    /// 基准取左上角而不是格心：行程只朝正方向走，从格心起步会把后半段挤出格底、被裁掉 —— 那看上去反倒
    /// 像"没动"，正是这块台子要避免的错觉。
    /// </para>
    /// </remarks>
    private const double Scale = 0.11d;

    private const double BaseLeft = 4d;
    private const double BaseTop = 6d;
    private const double BaseWidth = 20d;
    private const double BaseHeight = 13d;

    /// <summary>这一格显示哪一条采样器的产物。构造时定一次，此后不变。</summary>
    internal required string Kind { get; init; }

    public static readonly DependencyProperty FillProperty =
        Register(nameof(Fill), typeof(Brush), null);

    public static readonly DependencyProperty TintProperty =
        Register(nameof(Tint), typeof(Color), Colors.Gray);

    public static readonly DependencyProperty CornersProperty =
        Register(nameof(Corners), typeof(CornerRadius), new CornerRadius(2));

    public static readonly DependencyProperty AnchorProperty =
        Register(nameof(Anchor), typeof(Point), default(Point));

    public static readonly DependencyProperty BoundsProperty =
        Register(nameof(Bounds), typeof(Rect), default(Rect));

    public static readonly DependencyProperty ExtentProperty =
        Register(nameof(Extent), typeof(Size), default(Size));

    public static readonly DependencyProperty InsetProperty =
        Register(nameof(Inset), typeof(Thickness), default(Thickness));

    /// <summary>二维变换。<c>new</c> 是必需的：<see cref="Visual"/> 已经有一个同名的公开方法。</summary>
    public static readonly DependencyProperty RenderProperty =
        Register(nameof(Render), typeof(Transform), null);

    public static readonly DependencyProperty PoseProperty =
        Register(nameof(Pose), typeof(Transform3D), null);

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public Color Tint { get => (Color)GetValue(TintProperty)!; set => SetValue(TintProperty, value); }

    public CornerRadius Corners { get => (CornerRadius)GetValue(CornersProperty)!; set => SetValue(CornersProperty, value); }

    public Point Anchor { get => (Point)GetValue(AnchorProperty)!; set => SetValue(AnchorProperty, value); }

    public Rect Bounds { get => (Rect)GetValue(BoundsProperty)!; set => SetValue(BoundsProperty, value); }

    public Size Extent { get => (Size)GetValue(ExtentProperty)!; set => SetValue(ExtentProperty, value); }

    /// <summary>厚度。名字不叫 <c>Margin</c>：<see cref="FrameworkElement"/> 已经占用了那个名字。</summary>
    public Thickness Inset { get => (Thickness)GetValue(InsetProperty)!; set => SetValue(InsetProperty, value); }

    public new Transform? Render { get => (Transform?)GetValue(RenderProperty); set => SetValue(RenderProperty, value); }

    public Transform3D? Pose { get => (Transform3D?)GetValue(PoseProperty); set => SetValue(PoseProperty, value); }

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
    /// <para>
    /// 三维那条走的是投影而不是三维实体：这一组端点把轴钉死在单位 Z 上，所以采样出来的旋转就是一次平面内旋转，
    /// 绕 Z 的角与二维 <see cref="RotateTransform.Angle"/> 是同一份几何，画出来不是近似。投影丢掉的只有旋转
    /// 中心（CenterX/Y/Z：1→5、2→6、3→7）—— 二维元素没有第三根轴，那三个分量落在载荷里，不在画面上。轴一旦
    /// 离开 ±Z，平面里就没有对应物，这时不画，而不是拿别的东西冒名顶替。
    /// </para>
    /// </remarks>
    internal void Reposition()
    {
        var (x, y) = Kind switch
        {
            SamplerProbe.Kinds.PointSampler => (Anchor.X, Anchor.Y),
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

        // 边距同样按标尺画：这一组端点也跑到了 330/440，原值会把被写对象整个推出格子。
        Margin = new Thickness(Inset.Left * Scale, Inset.Top * Scale, Inset.Right * Scale, Inset.Bottom * Scale);

        if (Kind == SamplerProbe.Kinds.Transform3DSampler)
        {
            // 绕自己的中心转：被写对象很小，绕左上角转在格子里只看得出四分之一。
            RenderTransformOrigin = new Point(0.5d, 0.5d);
            RenderTransform = Pose is RotateTransform3D pose ? new RotateTransform(PlanarAngle(pose)) : null;
        }
        else
        {
            RenderTransform = Render switch
            {
                TranslateTransform translate => new TranslateTransform(translate.X * Scale, translate.Y * Scale),
                Transform transform => transform,
                _ => null,
            };
        }
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
    /// 把三维旋转投影成二维旋转角：只有轴落在单位 ±Z 上时平面内才有对应物，其余给 0（不转），不假装。
    /// </summary>
    private static double PlanarAngle(RotateTransform3D pose)
    {
        if (pose.Rotation is not AxisAngleRotation3D rotation) return 0d;
        if (Math.Abs(rotation.Axis.Z) < 1d - 1e-9d) return 0d;

        // 轴指 -Z 时，正角在平面里是反向转的，符号要跟着轴走。
        return rotation.Angle * Math.Sign(rotation.Axis.Z);
    }

    /// <summary>
    /// 注册一条依赖属性。每条属性写入都会重算这一格的显示 —— 不只是位置类的那些：厚度、变换也是在
    /// <see cref="Reposition"/> 里落到控件上的。重算是幂等的，多算一次不花钱。
    /// </summary>
    /// <remarks>
    /// <c>AffectsRender</c> 是这里的关键：采样器一写，Jalium 自己安排重绘 —— 不必手写 <c>InvalidateVisual</c>，
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
