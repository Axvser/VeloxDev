using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

// System.Drawing 与 Avalonia 有同名的 Point / Size / Color，两个命名空间一旦同时被引入就是 CS0104。
// 别名把 Avalonia 那一侧钉死，文件里出现的每个类型都不再依赖 using 的解析顺序。
using AvBoxShadows = Avalonia.Media.BoxShadows;
using AvColor = Avalonia.Media.Color;
using AvCornerRadius = Avalonia.CornerRadius;
using AvGridLength = Avalonia.Controls.GridLength;
using AvIBrush = Avalonia.Media.IBrush;
using AvPixelPoint = Avalonia.PixelPoint;
using AvPixelRect = Avalonia.PixelRect;
using AvPixelSize = Avalonia.PixelSize;
using AvPoint = Avalonia.Point;
using AvRelativePoint = Avalonia.RelativePoint;
using AvRelativeRect = Avalonia.RelativeRect;
using AvSize = Avalonia.Size;
using AvSolidColorBrush = Avalonia.Media.SolidColorBrush;
using AvThickness = Avalonia.Thickness;
using AvTransform = Avalonia.Media.Transform;
using AvTranslateTransform = Avalonia.Media.TranslateTransform;

namespace Demo;

/// <summary>
/// 采样器演示台上的被写对象：一个真正在视觉树里的控件，每条采样器一条**样式属性**，类型与产物完全一致。
/// </summary>
/// <remarks>
/// 换成控件而不是一个私有的 scratch 类，是为了让"采样器把值写到哪"这件事可被验证：属性是框架属性系统的真成员，
/// 采样器写它时走的是真实的属性通道，验收再从这个属性读回来 —— 于是断言依据的是**界面上那个控件实际持有的值**，
/// 而不是一个屏幕外的对象。控件自己按属性重绘，于是"画出来"这件事不需要另一套映射代码。
/// <para>
/// <b>重绘要自己挂。</b>Avalonia 没有 WPF 那种写进属性元数据里的 <c>AffectsRender</c> 开关，等价物是
/// <see cref="Visual.AffectsRender{T}(AvaloniaProperty[])"/> —— 一个只能在静态构造函数里调的静态方法，它做的
/// 事情就是给每条属性挂一个类处理器，处理器里调 <see cref="Visual.InvalidateVisual"/>。不挂的话，一条只影响
/// 画面、不影响版面的属性（笔刷、阴影、圆角）写进去之后画面上什么都不会发生：属性系统无从知道"这条属性要重画"。
/// </para>
/// <para>
/// <b>绘制是有标尺的。</b>位移类端点跑到 220，格子只有 96×62，按原值画会一步跨出格子被裁掉 —— 那看上去
/// 和"没动"一模一样。所以位置与尺寸在**绘制反应里**乘一个固定缩放，而属性本身持有的仍是原值：载荷读的是
/// 属性，于是断言的是原值，缩放只影响"怎么画"。
/// </para>
/// </remarks>
internal sealed class SamplerSubject : Control
{
    /// <summary>位移与尺寸的像素缩放。这一组端点最大分量 220，格子留出的行程约 38 像素。</summary>
    private const double Scale = 0.14d;

    private const double BaseLeft = 6d;
    private const double BaseTop = 6d;
    private const double BaseWidth = 26d;
    private const double BaseHeight = 18d;

    /// <summary>这一格显示哪一条采样器的产物。构造时定一次，此后不变。</summary>
    internal required string Kind { get; init; }

    public static readonly StyledProperty<AvBoxShadows> ShadowsProperty =
        AvaloniaProperty.Register<SamplerSubject, AvBoxShadows>(nameof(Shadows));

    public static readonly StyledProperty<AvIBrush?> FillProperty =
        AvaloniaProperty.Register<SamplerSubject, AvIBrush?>(nameof(Fill));

    public static readonly StyledProperty<AvColor> TintProperty =
        AvaloniaProperty.Register<SamplerSubject, AvColor>(nameof(Tint), Avalonia.Media.Colors.Gray);

    public static readonly StyledProperty<AvCornerRadius> RadiusProperty =
        AvaloniaProperty.Register<SamplerSubject, AvCornerRadius>(nameof(Radius), new AvCornerRadius(2));

    public static readonly StyledProperty<AvGridLength> ColumnProperty =
        AvaloniaProperty.Register<SamplerSubject, AvGridLength>(nameof(Column));

    public static readonly StyledProperty<AvPixelPoint> PixelSpotProperty =
        AvaloniaProperty.Register<SamplerSubject, AvPixelPoint>(nameof(PixelSpot));

    public static readonly StyledProperty<AvPixelRect> PixelBoxProperty =
        AvaloniaProperty.Register<SamplerSubject, AvPixelRect>(nameof(PixelBox));

    public static readonly StyledProperty<AvPixelSize> PixelExtentProperty =
        AvaloniaProperty.Register<SamplerSubject, AvPixelSize>(nameof(PixelExtent));

    public static readonly StyledProperty<AvPoint> SpotProperty =
        AvaloniaProperty.Register<SamplerSubject, AvPoint>(nameof(Spot));

    public static readonly StyledProperty<AvRelativePoint> RelSpotProperty =
        AvaloniaProperty.Register<SamplerSubject, AvRelativePoint>(nameof(RelSpot));

    public static readonly StyledProperty<AvRelativeRect> RelBoxProperty =
        AvaloniaProperty.Register<SamplerSubject, AvRelativeRect>(nameof(RelBox));

    public static readonly StyledProperty<AvSize> ExtentProperty =
        AvaloniaProperty.Register<SamplerSubject, AvSize>(nameof(Extent));

    /// <summary>厚度。名字不叫 <c>Margin</c>：<see cref="Layoutable"/> 已经占用了那个名字。</summary>
    public static readonly StyledProperty<AvThickness> InsetProperty =
        AvaloniaProperty.Register<SamplerSubject, AvThickness>(nameof(Inset));

    /// <summary>
    /// 变换。名字既不叫 <c>Render</c>（本类要重写 <see cref="Visual.Render"/>(DrawingContext)，同一个类型里
    /// 属性与方法不能同名），也不叫 <c>RenderTransform</c>（那是 <see cref="Visual"/> 的属性，本类把缩放后的
    /// 副本画在它上面，原值留在这条属性里给载荷读）。
    /// </summary>
    public static readonly StyledProperty<AvTransform?> MotionProperty =
        AvaloniaProperty.Register<SamplerSubject, AvTransform?>(nameof(Motion));

    /// <summary>
    /// 每条属性写入都要发生的两件事：重绘，以及把值落到控件上。
    /// </summary>
    /// <remarks>
    /// 这张表<b>必须声明在上面那 14 条属性之后</b>：静态字段初始化器按文本顺序执行，声明在前面的话它读到的是
    /// 还没初始化的 null。挂在类处理器上而不是每条属性自己的元数据里，是因为 Avalonia 的注册入口不收
    /// "值变了要做什么"这类回调 —— 类处理器是它给的挂点。
    /// </remarks>
    private static readonly AvaloniaProperty[] Tracked =
    [
        ShadowsProperty, FillProperty, TintProperty, RadiusProperty, ColumnProperty, PixelSpotProperty,
        PixelBoxProperty, PixelExtentProperty, SpotProperty, RelSpotProperty, RelBoxProperty, ExtentProperty,
        InsetProperty, MotionProperty,
    ];

    static SamplerSubject()
    {
        // 一、重绘：Avalonia 里 "属性一变就重画" 的唯一挂点，就是这里显式地挂上去。
        AffectsRender<SamplerSubject>(Tracked);

        // 二、重排：属性一变就把值落到控件上（位置、尺寸、变换），重算是幂等的，多算一次不花钱。
        foreach (var property in Tracked)
        {
            property.Changed.AddClassHandler<SamplerSubject>(static (subject, _) => subject.Reposition());
        }
    }

    public AvBoxShadows Shadows { get => GetValue(ShadowsProperty); set => SetValue(ShadowsProperty, value); }

    public AvIBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public AvColor Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public AvCornerRadius Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    public AvGridLength Column { get => GetValue(ColumnProperty); set => SetValue(ColumnProperty, value); }

    public AvPixelPoint PixelSpot { get => GetValue(PixelSpotProperty); set => SetValue(PixelSpotProperty, value); }

    public AvPixelRect PixelBox { get => GetValue(PixelBoxProperty); set => SetValue(PixelBoxProperty, value); }

    public AvPixelSize PixelExtent { get => GetValue(PixelExtentProperty); set => SetValue(PixelExtentProperty, value); }

    public AvPoint Spot { get => GetValue(SpotProperty); set => SetValue(SpotProperty, value); }

    public AvRelativePoint RelSpot { get => GetValue(RelSpotProperty); set => SetValue(RelSpotProperty, value); }

    public AvRelativeRect RelBox { get => GetValue(RelBoxProperty); set => SetValue(RelBoxProperty, value); }

    public AvSize Extent { get => GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    public AvThickness Inset { get => GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    public AvTransform? Motion { get => GetValue(MotionProperty); set => SetValue(MotionProperty, value); }

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
    /// </remarks>
    internal void Reposition()
    {
        // 位置。位移类的产物挪动这一格、按标尺画；厚度也挪，但按原值 —— 它的端点最大分量 44，格子装得下，
        // 乘 0.14 的话只剩几像素位移，这一格看上去就成了"什么都没发生"。
        // 厚度的落点是 Canvas.Left/Top 而不是 Margin：Avalonia 的 Canvas 不理会子元素的 Margin（抓帧实测，
        // 只往外写 Margin 的话格子里的控件纹丝不动），而画布本来也只吃 Left/Top 这两个分量（右/下在画布里
        // 无处安放）；四个分量仍然原样留在载荷里。
        double x, y;
        var scale = Scale;
        switch (Kind)
        {
            case SamplerProbe.Kinds.PixelPointSampler: x = PixelSpot.X; y = PixelSpot.Y; break;
            case SamplerProbe.Kinds.PixelRectSampler: x = PixelBox.X; y = PixelBox.Y; break;
            case SamplerProbe.Kinds.PointSampler: x = Spot.X; y = Spot.Y; break;
            // 相对点/矩形只取它们的点与矩形投影：单位（Absolute == 1 / Relative == 0）留在载荷里，这里画不出来。
            case SamplerProbe.Kinds.RelativePointSampler: x = RelSpot.Point.X; y = RelSpot.Point.Y; break;
            case SamplerProbe.Kinds.RelativeRectSampler: x = RelBox.Rect.X; y = RelBox.Rect.Y; break;
            case SamplerProbe.Kinds.ThicknessSampler: x = Inset.Left; y = Inset.Top; scale = 1d; break;
            default: x = 0d; y = 0d; break;
        }

        Canvas.SetLeft(this, BaseLeft + x * scale);
        Canvas.SetTop(this, BaseTop + y * scale);

        // 尺寸。只有尺寸类产物改宽高，其余格子恒为基准大小。
        double width, height;
        switch (Kind)
        {
            case SamplerProbe.Kinds.PixelRectSampler:
                width = PixelBox.Width * Scale;
                height = PixelBox.Height * Scale;
                break;
            case SamplerProbe.Kinds.PixelSizeSampler:
                width = PixelExtent.Width * Scale;
                height = PixelExtent.Height * Scale;
                break;
            case SamplerProbe.Kinds.RelativeRectSampler:
                width = RelBox.Rect.Width * Scale;
                height = RelBox.Rect.Height * Scale;
                break;
            case SamplerProbe.Kinds.SizeSampler:
                width = Extent.Width * Scale;
                height = Extent.Height * Scale;
                break;
            // GridLength 是个长度，落到宽度上，按原值（10 像素本来就小，上标尺只会缩成一根 1.4 像素的线）。
            // 它在这一格恒等于起点（起止单位不同 → 采样器的规则是保持起点），所以这一格停住是采样器的规则，
            // 不是演出漏了一拍。
            case SamplerProbe.Kinds.GridLengthSampler:
                width = Math.Max(1d, Column.Value);
                height = BaseHeight;
                break;
            default:
                width = BaseWidth;
                height = BaseHeight;
                break;
        }

        // 画不出 0 宽的东西；"停在下界"这件事本身已经由载荷里的数字如实报告了。
        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);

        // 变换同样按标尺画：原始变换留在 Motion 属性里，载荷读的是它。
        RenderTransform = Motion switch
        {
            AvTranslateTransform translate => new AvTranslateTransform(translate.X * Scale, translate.Y * Scale),
            AvTransform transform => transform,
            _ => null,
        };
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0d, 0d, Math.Max(1d, Bounds.Width), Math.Max(1d, Bounds.Height));

        // 圆角格的端点比 WPF 那边大一个数量级（最大分量 440）：按原值画的话 t=0 的半径 10 就已经超过被写对象
        // 高度的一半（18/2 = 9），整条缓动从头到尾都是一颗胶囊 —— 看上去像没动。乘上标尺后从 1.4 长到 62，
        // 走的正是 WPF 那边"方角 → 胶囊"的那段可见变化。其余格子根本不写这条属性，照原值画。
        var corners = Kind == SamplerProbe.Kinds.CornerRadiusSampler
            ? new AvCornerRadius(
                Radius.TopLeft * Scale, Radius.TopRight * Scale, Radius.BottomRight * Scale, Radius.BottomLeft * Scale)
            : Radius;

        // 半径按四个角里最大的那个画（与 WPF 那边同一规则），框架自己会把它钳到宽高的一半，于是"胶囊"画得出来。
        var radius = Math.Max(
            Math.Max(corners.TopLeft, corners.TopRight),
            Math.Max(corners.BottomLeft, corners.BottomRight));

        // 阴影与填充一笔画完：DrawRectangle 的 BoxShadows 重载就是 Avalonia 里画影子的入口（Border 自己也走它）。
        // 阴影按原值画，与 WPF 那边"效果按原值"同一条规则：它是一圈光晕，被格子裁掉远侧一半也仍然看得见；
        // 反过来乘 0.14 的话偏移与模糊都只剩一两像素，这一格就什么都看不出来了。
        context.DrawRectangle(Fill ?? new AvSolidColorBrush(Tint), null, bounds, radius, radius, Shadows);
    }
}
