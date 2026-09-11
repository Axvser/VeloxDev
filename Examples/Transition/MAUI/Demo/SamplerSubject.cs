using Microsoft.Maui.Controls.Shapes;

// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiBrush = Microsoft.Maui.Controls.Brush;
using MauiColor = Microsoft.Maui.Graphics.Color;
using MauiCornerRadius = Microsoft.Maui.CornerRadius;
using MauiPoint = Microsoft.Maui.Graphics.Point;
using MauiPointF = Microsoft.Maui.Graphics.PointF;
using MauiRect = Microsoft.Maui.Graphics.Rect;
using MauiShadow = Microsoft.Maui.Controls.Shadow;
using MauiSize = Microsoft.Maui.Graphics.Size;
using MauiSizeF = Microsoft.Maui.Graphics.SizeF;
using MauiThickness = Microsoft.Maui.Thickness;
using MauiTransform = Microsoft.Maui.Controls.Shapes.Transform;
using SysRectangleF = System.Drawing.RectangleF;

namespace Demo;

/// <summary>
/// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**可绑定属性**，类型与产物完全一致。
/// </summary>
/// <remarks>
/// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员
/// （可绑定属性），采样器写它时走的是真实的属性通道，验收再从这个属性读回来 —— 于是断言依据的是**界面上那个
/// 控件实际持有的值**，而不是一个屏幕外的对象。
/// <para>
/// <b>为什么是 <see cref="GraphicsView"/>。</b>WPF 那侧派生的是 <c>FrameworkElement</c> 并重写 <c>OnRender</c>，
/// 属性一变就由属性系统安排重绘。MAUI 没有 <c>OnRender</c>：派生 <c>Border</c> 只能靠改框架属性（背景、圆角）
/// 间接换样子，画不了"按采样器的产物现算出来的图形"。<see cref="GraphicsView"/> + <see cref="IDrawable"/> 才是
/// 对等物 —— 自己在自己的局部坐标里画，画完调 <see cref="GraphicsView.Invalidate"/> 让画布重画。
/// </para>
/// <para>
/// <b>MAUI 没有 <c>AffectsRender</c>。</b>可绑定属性的元数据只认 <c>validateValue</c> / <c>propertyChanged</c> /
/// <c>propertyChanging</c> / <c>coerceValue</c> / <c>defaultValueCreator</c> 这几样，没有"这条属性影响渲染"
/// 这个选项，属性系统也不替谁安排重绘。所以重绘是**我们在属性变更回调里自己调 <c>Invalidate()</c>** 换来的：
/// 那是<see cref="Register"/>里唯一一处"记得去做"的事，而不是属性系统给的保证 —— 与 WPF 由
/// <c>AffectsRender</c> 兜底正好相反。
/// </para>
/// <para>
/// <b>绘制是有标尺的。</b>位移类端点跑到 220，格子只有 84×56，按原值画会一步跨出格子被裁掉 —— 那看上去
/// 和"没动"一模一样。所以位置与尺寸在**绘制反应里**乘一个固定缩放，而属性本身持有的仍是原值：载荷读的是
/// 属性，于是断言的是原值，缩放只影响"怎么画"。（这个适配器的清单里没有三维的点与向量 —— Point3D/Vector3D
/// 那几条是 WPF 那侧才有的，这里的位移与尺寸就这几条。）
/// </para>
/// <para>
/// <b>MAUI 把"位置"和"旋转/缩放"表达成元素自己的 <see cref="VisualElement.TranslationX"/>/<c>TranslationY</c>、
/// <c>RotationX</c>/<c>RotationY</c>/<c>Scale</c>，没有变换对象可以挂</b>，而 <c>Transform</c> 型产物也只交出一个
/// 矩阵。所以这里分成两步：产物照原样进属性（真值在那儿），绘制反应只把矩阵的偏移量投影到平移上、把图形画成
/// 2D 图元 —— 看上去动的方向与量级对得上，而矩阵本身一个分量都没被改写。
/// </para>
/// <para>
/// 四条<b>与框架成员重名</b>的属性，各让一步：厚度不叫 <c>Margin</c>（<see cref="View"/> 已经占了那个名字，
/// 而且那条属性正是绘制反应的落点），叫 <see cref="Inset"/>；<c>Shadow</c>、<c>Bounds</c>、<c>Frame</c> 同理 ——
/// <see cref="VisualElement"/> 上有一个 <c>Shadow</c>（本适配器阴影采样器的产物类型正好就是它）、一个只读的
/// <c>Bounds</c> 和它的旧名 <c>Frame</c>，所以这两条叫 <see cref="ShadowValue"/> 与 <see cref="Area"/>。
/// 标尺常量也不叫 <c>Scale</c>：那个名字是元素自己的缩放。<see cref="ShadowValue"/> 与框架那个 <c>Shadow</c>
/// 的差别不只是名字 —— 后者挂到这一格上根本不显示，见 <see cref="Draw"/>。
/// </para>
/// </remarks>
internal sealed class SamplerSubject : GraphicsView, IDrawable
{
    /// <summary>位移与尺寸的像素缩放。这一组端点最大分量 220，格子留出的行程约 28 像素，另给过冲峰值留余量。</summary>
    private const double DrawScale = 0.13d;

    private const double BaseInset = 4d;
    private const double BaseWidth = 22d;
    private const double BaseHeight = 16d;

    /// <summary>阴影那一格的画布比方块多出来的一圈 —— 阴影要画在方块后面、还得看得见，只能给它留出地方。</summary>
    private const double ShadowRoom = 14d;

    /// <summary>这一格显示哪一条采样器的产物。构造时定一次，此后不变。</summary>
    internal required string Kind { get; init; }

    public static readonly BindableProperty FillProperty =
        Register(nameof(Fill), typeof(MauiBrush), null);

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
        Register(nameof(AreaF), typeof(SysRectangleF), default(SysRectangleF));

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

    public MauiColor Tint { get => (MauiColor)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public MauiCornerRadius Corners { get => (MauiCornerRadius)GetValue(CornersProperty); set => SetValue(CornersProperty, value); }

    public MauiPoint Anchor { get => (MauiPoint)GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }

    public MauiPointF AnchorF { get => (MauiPointF)GetValue(AnchorFProperty); set => SetValue(AnchorFProperty, value); }

    /// <summary>矩形。名字不叫 <c>Bounds</c>（也不叫 <c>Frame</c>，那是它的旧名）：<see cref="VisualElement"/> 上那个只读的 <c>Bounds</c> 是布局算出来的位置与大小，两个名字都被占了。</summary>
    public MauiRect Area { get => (MauiRect)GetValue(AreaProperty); set => SetValue(AreaProperty, value); }

    /// <summary>单精度矩形。适配器的 <c>RectFSampler</c> 操作的是 <see cref="SysRectangleF"/>，不是 MAUI 自己的 RectF。</summary>
    public SysRectangleF AreaF { get => (SysRectangleF)GetValue(AreaFProperty); set => SetValue(AreaFProperty, value); }

    /// <summary>阴影。名字不叫 <c>Shadow</c>：<see cref="VisualElement"/> 上已经有一个 <c>Shadow</c>，而采样器的产物正是同一个类型。</summary>
    public MauiShadow? ShadowValue { get => (MauiShadow?)GetValue(ShadowValueProperty); set => SetValue(ShadowValueProperty, value); }

    public MauiSize Extent { get => (MauiSize)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    public MauiSizeF ExtentF { get => (MauiSizeF)GetValue(ExtentFProperty); set => SetValue(ExtentFProperty, value); }

    /// <summary>厚度。名字不叫 <c>Margin</c>：<see cref="View"/> 已经占用了那个名字，而那条属性同样被绘制反应借用。</summary>
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
    }

    /// <summary>
    /// 把采样器写下的值**换算成格子里画得下的样子**。
    /// </summary>
    /// <remarks>
    /// 这是"反应"，不是"值"：属性持有的是采样器写下的原值（载荷读的就是它），这里只把它落到像素上。
    /// 位移走 <see cref="VisualElement.TranslationX"/>/<c>TranslationY</c>（MAUI 没有变换对象），尺寸走
    /// <c>WidthRequest</c>/<c>HeightRequest</c>，厚度走 <c>Margin</c>，阴影交给 <see cref="Draw"/> ——
    /// 采样器的产物只读不写。
    /// </remarks>
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

    /// <summary>
    /// 画这一格：在自己的局部坐标里画一个圆角方块，外加它自己的阴影。位置、尺寸、旋转由元素自己的属性承载，
    /// 不在这幅画里。
    /// </summary>
    /// <remarks>
    /// 画布只接受一个圆角半径，而采样器写的是四个分量，所以取其中的最大值（与 WPF 那侧一致）。半径再钳到短边的
    /// 一半：超过就画不出"更大的圆角"了，而端点在外推下会跑到 44 甚至负值，平台实现不该被这种值叫停。
    /// 颜色只取实心刷的通道 —— 这两条端点刷都是实心的，非实心时退回 <see cref="Tint"/>。
    /// <para>
    /// <b>阴影画在画布上，不挂在<see cref="VisualElement.Shadow"/>上。</b>那是实测出来的分歧：GraphicsView 在
    /// Windows 上是一块自绘画布，框架的阴影合到原生视图上、画布自己的 alpha 不参与，于是把
    /// <see cref="ShadowValue"/> 整份挂过去，五个缓动帧加一次过冲播放下来，那一格**一个像素都没变** ——
    /// 界面上与"没有阴影"是同一件事。所以阴影也用同一套 2D 图元画：**同一个圆角方块，改成阴影的颜色、按标尺
    /// 挪开一份**，方块盖在上面（<see cref="Reposition"/> 给这一格的画布留了 <see cref="ShadowRoom"/> 那么一圈，
    /// 阴影才有地方落 —— 画布与方块一样大时，整块阴影要么被方块盖住、要么被画布裁掉，两个都是"没有阴影"）。
    /// 偏移按标尺缩小后又钳到那一圈的半径上：原值 220 的偏移落下来是 29 个点，比留出来的一圈还大，钳住之后
    /// 方向仍在、量级顶在上限上；模糊半径则按标尺外扩一圈，于是 10→60 的半径在这一格上是阴影厚 1→8 个点。
    /// </para>
    /// </remarks>
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
        canvas.FillColor = Fill is SolidColorBrush solid ? solid.Color : tint ?? Colors.Gray;
        canvas.FillRoundedRectangle(0f, 0f, boxWidth, boxHeight, (float)radius);
    }

    /// <summary>
    /// 阴影的颜色：取它的刷子（非实心就没有颜色可取），再乘上它自己的不透明度。
    /// </summary>
    /// <remarks>采样器的产物只读不写 —— 这里算出的是颜色，不是把产物改掉。</remarks>
    private static MauiColor? ShadowPaint(MauiShadow cast)
        => cast.Brush is SolidColorBrush solid
            ? solid.Color.WithAlpha(Math.Clamp((float)(solid.Color.Alpha * cast.Opacity), 0f, 1f))
            : null;

    /// <summary>
    /// 注册一条可绑定属性。每条属性写入都会重算这一格的显示 —— 不只是位置类的那些：厚度、阴影、变换也是在
    /// <see cref="Reposition"/> 里落到控件上的。重算是幂等的，多算一次不花钱。
    /// </summary>
    /// <remarks>
    /// 这里与 WPF 的分歧是这段代码里最要紧的一处：WPF 注册的是带 <c>AffectsRender</c> 的
    /// <c>FrameworkPropertyMetadata</c>，重绘由属性系统安排；MAUI 的可绑定属性没有这个开关，重绘只能由
    /// <see cref="Reposition"/> 末尾那句 <see cref="GraphicsView.Invalidate"/> 自己叫。也就是说，**漏掉那句
    /// 不会有任何编译期或运行期报错**，只是画布停在上一帧 —— 换属性的写法要连着这句一起搬。
    /// </remarks>
    private static BindableProperty Register(string name, Type type, object? defaultValue)
        => BindableProperty.Create(
            name,
            type,
            typeof(SamplerSubject),
            defaultValue,
            propertyChanged: static (bindable, _, _) => ((SamplerSubject)bindable).Reposition());
}
