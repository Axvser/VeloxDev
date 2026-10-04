using Microsoft.Maui.Controls.Shapes;

// 同名类型一律显式取 MAUI 的那一侧：PointF / SizeF 在 System.Drawing 里也各有一份，量纲不同、不能混。
using MauiBrush = Microsoft.Maui.Controls.Brush;
using MauiColor = Microsoft.Maui.Graphics.Color;
using MauiGradientStop = Microsoft.Maui.Controls.GradientStop;
using MauiLinearGradientBrush = Microsoft.Maui.Controls.LinearGradientBrush;
using MauiCornerRadius = Microsoft.Maui.CornerRadius;
using MauiPoint = Microsoft.Maui.Graphics.Point;
using MauiPointF = Microsoft.Maui.Graphics.PointF;
using MauiRect = Microsoft.Maui.Graphics.Rect;
using MauiRectF = Microsoft.Maui.Graphics.RectF;
using MauiShadow = Microsoft.Maui.Controls.Shadow;
using MauiSize = Microsoft.Maui.Graphics.Size;
using MauiSizeF = Microsoft.Maui.Graphics.SizeF;
using MauiThickness = Microsoft.Maui.Thickness;
using MauiTransform = Microsoft.Maui.Controls.Shapes.Transform;

namespace Demo;

// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**可绑定属性**，类型与产物完全一致。
// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员
// 控件实际持有的值**，而不是一个屏幕外的对象。
// 属性一变就由属性系统安排重绘。MAUI 没有 OnRender：派生 Border 只能靠改框架属性（背景、圆角）
// MAUI 没有 AffectsRender。可绑定属性的元数据只认 validateValue / propertyChanged /
// propertyChanging / coerceValue / defaultValueCreator 这几样，没有"这条属性影响渲染"
// 这个选项，属性系统也不替谁安排重绘。所以重绘是**我们在属性变更回调里自己调 Invalidate()** 换来的：
internal sealed class SamplerSubject : GraphicsView, IDrawable
{
    // 位移与尺寸的像素缩放。这一组端点最大分量 220，格子留出的行程约 28 像素，另给过冲峰值留余量。
    private const double DrawScale = 0.13d;

    private const double BaseInset = 4d;
    private const double BaseWidth = 22d;
    private const double BaseHeight = 16d;

    // 阴影那一格的画布比方块多出来的一圈 —— 阴影要画在方块后面、还得看得见，只能给它留出地方。
    private const double ShadowRoom = 14d;

    // 这一格显示哪一条采样器的产物。构造时定一次，此后不变。
    internal required string Kind { get; init; }

    public static readonly BindableProperty FillProperty =
        Register(nameof(Fill), typeof(MauiBrush), null);

    // 一段两停的渐变，只给索引器那两行当被写的集合用：路径写的是 Ramp.GradientStops[i].Color。
    // 单独一条属性而不是复用 Fill：那一位是 null 开头、由每一行自己的起点值装填的，
    // 而索引器路径要写的元素必须在写它之前就先存在。
    public static readonly BindableProperty RampProperty =
        Register(nameof(Ramp), typeof(MauiBrush), null);

    public static readonly BindableProperty TintProperty =
        Register(nameof(Tint), typeof(MauiColor), Colors.Gray);

    public static readonly BindableProperty CornersProperty =
        Register(nameof(Corners), typeof(MauiCornerRadius), new MauiCornerRadius(2));

    public static readonly BindableProperty AnchorProperty =
        Register(nameof(Anchor), typeof(MauiPoint), default(MauiPoint));

    public static readonly BindableProperty AnchorFProperty =
        Register(nameof(AnchorF), typeof(MauiPointF), default(MauiPointF));

    public static readonly BindableProperty AreaProperty =
        Register(nameof(Area), typeof(MauiRect), default(MauiRect));

    public static readonly BindableProperty AreaFProperty =
        Register(nameof(AreaF), typeof(MauiRectF), default(MauiRectF));

    public static readonly BindableProperty ShadowValueProperty =
        Register(nameof(ShadowValue), typeof(MauiShadow), null);

    public static readonly BindableProperty ExtentProperty =
        Register(nameof(Extent), typeof(MauiSize), default(MauiSize));

    public static readonly BindableProperty ExtentFProperty =
        Register(nameof(ExtentF), typeof(MauiSizeF), default(MauiSizeF));

    public static readonly BindableProperty InsetProperty =
        Register(nameof(Inset), typeof(MauiThickness), default(MauiThickness));

    public static readonly BindableProperty RenderProperty =
        Register(nameof(Render), typeof(MauiTransform), null);

    public MauiBrush? Fill { get => (MauiBrush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public MauiBrush? Ramp { get => (MauiBrush?)GetValue(RampProperty); set => SetValue(RampProperty, value); }

    public MauiColor Tint { get => (MauiColor)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public MauiCornerRadius Corners { get => (MauiCornerRadius)GetValue(CornersProperty); set => SetValue(CornersProperty, value); }

    public MauiPoint Anchor { get => (MauiPoint)GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }

    public MauiPointF AnchorF { get => (MauiPointF)GetValue(AnchorFProperty); set => SetValue(AnchorFProperty, value); }

    // 矩形。名字不叫 Bounds（也不叫 Frame，那是它的旧名）：VisualElement 上那个只读的 Bounds 是布局算出来的位置与大小，两个名字都被占了。
    public MauiRect Area { get => (MauiRect)GetValue(AreaProperty); set => SetValue(AreaProperty, value); }

    // 单精度矩形。适配器的 RectFSampler 操作的就是 MauiRectF；System.Drawing.RectangleF 归 Core 的 RectangleFSampler，两条注册键互不相同。
    public MauiRectF AreaF { get => (MauiRectF)GetValue(AreaFProperty); set => SetValue(AreaFProperty, value); }

    // 阴影。名字不叫 Shadow：VisualElement 上已经有一个 Shadow，而采样器的产物正是同一个类型。
    public MauiShadow? ShadowValue { get => (MauiShadow?)GetValue(ShadowValueProperty); set => SetValue(ShadowValueProperty, value); }

    public MauiSize Extent { get => (MauiSize)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    public MauiSizeF ExtentF { get => (MauiSizeF)GetValue(ExtentFProperty); set => SetValue(ExtentFProperty, value); }

    // 厚度。名字不叫 Margin：View 已经占用了那个名字，而那条属性同样被绘制反应借用。
    public MauiThickness Inset { get => (MauiThickness)GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    public MauiTransform? Render { get => (MauiTransform?)GetValue(RenderProperty); set => SetValue(RenderProperty, value); }

    public SamplerSubject()
    {
        // 自己画自己：把 IDrawable 指回这个实例，画布上的内容就在 Draw 里。
        Drawable = this;

        WidthRequest = BaseWidth;
        HeightRequest = BaseHeight;
        Margin = new MauiThickness(BaseInset, BaseInset, 0d, 0d);

        // 靠左上角摆，不跟着格子拉伸：一拉伸，尺寸类产物就没法从这个被写对象的宽高上看出来了。
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;

        // 索引器那两行要写的是一个集合里的元素，所以它们得有集合可写。每条实例各建一段。
        Ramp = new MauiLinearGradientBrush
        {
            GradientStops =
            {
                new MauiGradientStop { Color = MauiColor.FromRgba(1d, 255d / 255d, 69d / 255d, 0d), Offset = 0f },
                new MauiGradientStop { Color = MauiColor.FromRgba(1d, 70d / 255d, 130d / 255d, 180d / 255d), Offset = 1f },
            },
        };
    }

    // 把采样器写下的值**换算成格子里画得下的样子**。
    // 这是"反应"，不是"值"：属性持有的是采样器写下的原值（载荷读的就是它），这里只把它落到像素上。
    // 位移走 TranslationX/TranslationY（MAUI 没有变换对象），尺寸走
    internal void Reposition()
    {
        var (x, y) = Kind switch
        {
            SamplerProbe.Kinds.PointSampler => (Anchor.X, Anchor.Y),
            SamplerProbe.Kinds.PointFSampler => (AnchorF.X, AnchorF.Y),
            SamplerProbe.Kinds.RectSampler => (Area.X, Area.Y),
            SamplerProbe.Kinds.RectFSampler => (AreaF.X, AreaF.Y),
            // 矩阵里能落到这个被写对象上的只有两个偏移量：其余四个分量是旋转/缩放/斜切，MAUI 把它们表达成元素自己的
            // Rotation/Scale，这里没有对应的可变项。板上画的是矩阵的平移部分，载荷里六个分量一个不少。
            SamplerProbe.Kinds.TransformSampler => Render is { } transform
                ? (transform.Value.OffsetX, transform.Value.OffsetY)
                : (0d, 0d),
            _ => (0d, 0d),
        };

        TranslationX = x * DrawScale;
        TranslationY = y * DrawScale;

        // 尺寸类的产物按标尺画；其余各条落的仍是那个基准方块 —— 基准本身就是画出来的像素值，不能再乘一次标尺，
        // 否则每一格都会缩成两三个像素的黑点（看上去像"什么都没画"，而不是"这一格显示默认样子"）。
        var (width, height) = Kind switch
        {
            SamplerProbe.Kinds.SizeSampler => (Extent.Width * DrawScale, Extent.Height * DrawScale),
            SamplerProbe.Kinds.SizeFSampler => (ExtentF.Width * DrawScale, ExtentF.Height * DrawScale),
            SamplerProbe.Kinds.RectSampler => (Area.Width * DrawScale, Area.Height * DrawScale),
            SamplerProbe.Kinds.RectFSampler => (AreaF.Width * DrawScale, AreaF.Height * DrawScale),
            // 阴影这一格的画布比方块大一圈：方块仍是 22×16、仍贴着格子左上角，多出来的一圈是阴影露出来的地方。
            SamplerProbe.Kinds.ShadowSampler => (BaseWidth + ShadowRoom, BaseHeight + ShadowRoom),
            _ => (BaseWidth, BaseHeight),
        };

        // 画不出 0 宽的东西；"停在下界"这件事本身已经由载荷里的数字如实报告了。
        WidthRequest = Math.Max(1d, width);
        HeightRequest = Math.Max(1d, height);

        // 只画左、上两个分量，右、下留零：它们在这个被写对象上没有可见的对应物 —— 把手从格子里减掉之后，
        // 右/下喂进去只会把它自己那一格压小，端点处上 29 加下 57 已经超过 56 高的格子，被写对象会被压成零高、
        // 整格空白，看上去正是"什么都没发生"。WPF 那侧的被写对象锚在画布左上、边距另算，这里两者共用 Margin
        // 一个属性，所以基准要加回来。载荷里四个分量一个不少。
        Margin = new MauiThickness(BaseInset + Inset.Left * DrawScale, BaseInset + Inset.Top * DrawScale, 0d, 0d);

        // 属性系统不会因为"有人改了一条可绑定属性"就重画画布 —— 这一句是这一步里唯一需要记得做的事。
        Invalidate();
    }

    // 画这一格：在自己的局部坐标里画一个圆角方块，外加它自己的阴影。位置、尺寸、旋转由元素自己的属性承载，
    // 不在这幅画里。
    // ShadowValue 整份挂过去，五个缓动帧加一次过冲播放下来，那一格**一个像素都没变** ——
    // 界面上与"没有阴影"是同一件事。所以阴影也用同一套 2D 图元画：**同一个圆角方块，改成阴影的颜色、按标尺
    // 阴影才有地方落 —— 画布与方块一样大时，整块阴影要么被方块盖住、要么被画布裁掉，两个都是"没有阴影"）。
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var width = Math.Max(1f, dirtyRect.Width);
        var height = Math.Max(1f, dirtyRect.Height);

        // 方块：除了阴影那一格（画布留了一圈给阴影），方块铺满整块画布。
        var boxWidth = Kind == SamplerProbe.Kinds.ShadowSampler ? (float)BaseWidth : width;
        var boxHeight = Kind == SamplerProbe.Kinds.ShadowSampler ? (float)BaseHeight : height;

        var radius = Math.Max(
            Math.Max(Corners.TopLeft, Corners.TopRight),
            Math.Max(Corners.BottomLeft, Corners.BottomRight));
        radius = Math.Clamp(radius, 0d, Math.Min(boxWidth, boxHeight) / 2d);

        // 阴影先画，方块盖在上面。颜色与不透明度取自采样器的产物，只在读取时合成 —— 产物本身一个字段都不改。
        if (ShadowValue is { } cast && ShadowPaint(cast) is { } castColor)
        {
            var spread = cast.Radius * DrawScale;
            canvas.FillColor = castColor;
            canvas.FillRoundedRectangle(
                (float)(Math.Clamp(cast.Offset.X * DrawScale, -ShadowRoom / 2d, ShadowRoom / 2d) - spread),
                (float)(Math.Clamp(cast.Offset.Y * DrawScale, -ShadowRoom / 2d, ShadowRoom / 2d) - spread),
                (float)(boxWidth + spread * 2d), (float)(boxHeight + spread * 2d), (float)radius);
        }

        MauiColor? tint = Tint;

        // 索引器那两行画它们真正在写的那段渐变：停靠点的颜色一动，条带就跟着动 —— 写在集合元素上的值
        // 照样是看得见的。其余各行照旧按 Fill 的实心色画。
        if (Kind is SamplerProbe.Kinds.GradientStop0Color or SamplerProbe.Kinds.GradientStop1Color && Ramp is { } ramp)
        {
            canvas.SetFillPaint(ramp, new RectF(0f, 0f, boxWidth, boxHeight));
        }
        else
        {
            canvas.FillColor = Fill is SolidColorBrush solid ? solid.Color : tint ?? Colors.Gray;
        }

        canvas.FillRoundedRectangle(0f, 0f, boxWidth, boxHeight, (float)radius);
    }

    // 阴影的颜色：取它的刷子（非实心就没有颜色可取），再乘上它自己的不透明度。
    private static MauiColor? ShadowPaint(MauiShadow cast)
        => cast.Brush is SolidColorBrush solid
            ? solid.Color.WithAlpha(Math.Clamp((float)(solid.Color.Alpha * cast.Opacity), 0f, 1f))
            : null;

    // 注册一条可绑定属性。每条属性写入都会重算这一格的显示 —— 不只是位置类的那些：厚度、阴影、变换也是在
    // Reposition 里落到控件上的。重算是幂等的，多算一次不花钱。
    // 这里与 WPF 的分歧是这段代码里最要紧的一处：WPF 注册的是带 AffectsRender 的
    // FrameworkPropertyMetadata，重绘由属性系统安排；MAUI 的可绑定属性没有这个开关，重绘只能由
    private static BindableProperty Register(string name, Type type, object? defaultValue)
        => BindableProperty.Create(
            name,
            type,
            typeof(SamplerSubject),
            defaultValue,
            propertyChanged: static (bindable, _, _) => ((SamplerSubject)bindable).Reposition());
}
