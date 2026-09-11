using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

// System.Drawing 与 Avalonia 有同名的 Point / Size / Color，两个命名空间一旦同时被引入就是 CS0104。
// 别名把 Avalonia 那一侧钉死，文件里出现的每个类型都不再依赖 using 的解析顺序。
using AvBoxShadow = Avalonia.Media.BoxShadow;
using AvBoxShadows = Avalonia.Media.BoxShadows;
using AvColor = Avalonia.Media.Color;
using AvCornerRadius = Avalonia.CornerRadius;
using AvGridLength = Avalonia.Controls.GridLength;
using AvGridUnitType = Avalonia.Controls.GridUnitType;
using AvIBrush = Avalonia.Media.IBrush;
using AvISolidColorBrush = Avalonia.Media.ISolidColorBrush;
using AvPixelPoint = Avalonia.PixelPoint;
using AvPixelRect = Avalonia.PixelRect;
using AvPixelSize = Avalonia.PixelSize;
using AvPoint = Avalonia.Point;
using AvRelativePoint = Avalonia.RelativePoint;
using AvRelativeRect = Avalonia.RelativeRect;
using AvRelativeUnit = Avalonia.RelativeUnit;
using AvSize = Avalonia.Size;
using AvSolidColorBrush = Avalonia.Media.SolidColorBrush;
using AvThickness = Avalonia.Thickness;
using AvTranslateTransform = Avalonia.Media.TranslateTransform;

namespace Demo;

/// <summary>
/// 把该适配器发布的每一个采样器各推一帧序列，把结果写成机器可读的载荷。
/// </summary>
/// <remarks>
/// 这是采样式验收在真 app 里的落点：采样器要写的对象（画刷、阴影、变换）都需要一个活着的框架运行时，
/// 而这里正是有运行时的进程。
/// <para>
/// 被写对象是 <see cref="SamplerSubject"/> —— 一个**真正在视觉树里的控件**，每条采样器一条同类型的属性。
/// 采样器直接写在它上面，载荷再从该属性读回，所以断言依据的是界面上那个控件实际持有的值。控件自己按属性重绘，
/// 于是"画出来"这件事不需要另一套映射代码。
/// </para>
/// <para>
/// 每条采样器由界面上的一个把手点击驱动，一次只跑一条 —— 验收侧因此走的是"点控件 → 触发处理函数 →
/// 采样器写值 → 读回"这条真实的 UI 交互路径，而不是让 app 在启动时闷头算完一遍。扫描必须落在 UI 线程上：
/// 它要构造画刷、阴影、变换这类有线程亲和性的对象，而点击处理函数本身就在 UI 线程。
/// </para>
/// <para>
/// 端点是工厂而不是实例：引用类型的端点若跨帧复用，一个就地改动端点的采样器会把后面的帧一起带偏，
/// 而「不得改动交给 InsertFrame 的 start/end」正是库对采样器的硬约束 —— 每帧一对全新端点才看得见它。
/// 值类型端点每次取值就是一份副本，引用类型（笔刷、变换）则交给工厂，每一帧现造一对。
/// </para>
/// </remarks>
internal static class SamplerProbe
{
    /// <summary>
    /// 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
    /// 数，所以时间本身不引入误差。
    /// </summary>
    private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

    /// <summary>
    /// 每条采样器的名字 —— 取自采样器类型名，所以把手令牌、载荷键、舞台格子三处说的是同一件事。
    /// </summary>
    internal static class Kinds
    {
        internal const string BoxShadowsSampler = nameof(BoxShadowsSampler);
        internal const string BrushSampler = nameof(BrushSampler);
        internal const string ColorSampler = nameof(ColorSampler);
        internal const string CornerRadiusSampler = nameof(CornerRadiusSampler);
        internal const string GridLengthSampler = nameof(GridLengthSampler);
        internal const string PixelPointSampler = nameof(PixelPointSampler);
        internal const string PixelRectSampler = nameof(PixelRectSampler);
        internal const string PixelSizeSampler = nameof(PixelSizeSampler);
        internal const string PointSampler = nameof(PointSampler);
        internal const string RelativePointSampler = nameof(RelativePointSampler);
        internal const string RelativeRectSampler = nameof(RelativeRectSampler);
        internal const string SizeSampler = nameof(SizeSampler);
        internal const string ThicknessSampler = nameof(ThicknessSampler);
        internal const string TransformSampler = nameof(TransformSampler);
    }

    /// <summary>一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。</summary>
    private sealed record ProbeSpec(string Name, string Property, Func<ISampler> Create, Func<object> Start, Func<object> End);

    // ---- 端点：与 Samplers/AvaloniaEntries.cs 里的一致 ----

    private static readonly AvColor ColorStart = AvColor.FromArgb(200, 200, 100, 50);
    private static readonly AvColor ColorEnd = AvColor.FromArgb(250, 240, 180, 120);

    // 阴影端点必须不是 default，否则采样器会走"原样返回端点"的短路分支而不是逐字段插值。
    private static readonly AvBoxShadow ShadowStart = new()
    {
        OffsetX = 2d,
        OffsetY = 4d,
        Blur = 8d,
        Spread = 1d,
        Color = AvColor.FromArgb(200, 200, 100, 50),
        IsInset = false,
    };

    private static readonly AvBoxShadow ShadowEnd = new()
    {
        OffsetX = 12d,
        OffsetY = 24d,
        Blur = 18d,
        Spread = 11d,
        Color = AvColor.FromArgb(250, 240, 180, 120),
        IsInset = true,
    };

    // 两个 SolidColorBrush 各带一个非默认不透明度，实心→实心动画走的就是 BrushSampler 的第一条分支。
    private static AvSolidColorBrush BrushStart() => new() { Color = ColorStart, Opacity = 0.25d };
    private static AvSolidColorBrush BrushEnd() => new() { Color = ColorEnd, Opacity = 0.75d };

    private static AvTranslateTransform TranslateStart() => new(10, 20);
    private static AvTranslateTransform TranslateEnd() => new(110, 220);

    /// <summary>
    /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手与舞台格子都从这张表生成。
    /// </summary>
    private static readonly ProbeSpec[] Probes =
    [
        new(Kinds.BoxShadowsSampler, nameof(SamplerSubject.Shadows), () => new BoxShadowsSampler(),
            () => new AvBoxShadows(ShadowStart), () => new AvBoxShadows(ShadowEnd)),
        new(Kinds.BrushSampler, nameof(SamplerSubject.Fill), () => new BrushSampler(), BrushStart, BrushEnd),
        new(Kinds.ColorSampler, nameof(SamplerSubject.Tint), () => new ColorSampler(), () => ColorStart, () => ColorEnd),
        new(Kinds.CornerRadiusSampler, nameof(SamplerSubject.Radius), () => new CornerRadiusSampler(),
            () => new AvCornerRadius(10, 20, 30, 40), () => new AvCornerRadius(110, 220, 330, 440)),
        new(Kinds.GridLengthSampler, nameof(SamplerSubject.Column), () => new GridLengthSampler(),
            () => new AvGridLength(10, AvGridUnitType.Pixel), () => new AvGridLength(2, AvGridUnitType.Star)),
        new(Kinds.PixelPointSampler, nameof(SamplerSubject.PixelSpot), () => new PixelPointSampler(),
            () => new AvPixelPoint(10, 20), () => new AvPixelPoint(110, 220)),
        new(Kinds.PixelRectSampler, nameof(SamplerSubject.PixelBox), () => new PixelRectSampler(),
            () => new AvPixelRect(0, 0, 100, 50), () => new AvPixelRect(100, 200, 0, 150)),
        new(Kinds.PixelSizeSampler, nameof(SamplerSubject.PixelExtent), () => new PixelSizeSampler(),
            () => new AvPixelSize(100, 50), () => new AvPixelSize(0, 150)),
        new(Kinds.PointSampler, nameof(SamplerSubject.Spot), () => new PointSampler(),
            () => new AvPoint(10, 20), () => new AvPoint(110, 220)),
        new(Kinds.RelativePointSampler, nameof(SamplerSubject.RelSpot), () => new RelativePointSampler(),
            () => new AvRelativePoint(10, 20, AvRelativeUnit.Absolute), () => new AvRelativePoint(0.5, 0.6, AvRelativeUnit.Relative)),
        new(Kinds.RelativeRectSampler, nameof(SamplerSubject.RelBox), () => new RelativeRectSampler(),
            () => new AvRelativeRect(10, 20, 30, 40, AvRelativeUnit.Absolute), () => new AvRelativeRect(0.1, 0.2, 0.3, 0.4, AvRelativeUnit.Relative)),
        new(Kinds.SizeSampler, nameof(SamplerSubject.Extent), () => new SizeSampler(),
            () => new AvSize(100, 50), () => new AvSize(0, 150)),
        new(Kinds.ThicknessSampler, nameof(SamplerSubject.Inset), () => new ThicknessSampler(),
            () => new AvThickness(1, 2, 3, 4), () => new AvThickness(11, 22, 33, 44)),
        new(Kinds.TransformSampler, nameof(SamplerSubject.Motion), () => new TransformSampler(), TranslateStart, TranslateEnd),
    ];

    /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    /// <summary>
    /// 在被写控件上跑一条采样器，产出载荷文本，形如 <c>v=1;seq=3;n=5;s.BoxShadowsSampler.0=BoxShadows,200,…;</c>。
    /// </summary>
    /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
    internal static string Run(SamplerSubject subject, string samplerName, long sequence)
    {
        var payload = new StringBuilder($"v=1;seq={sequence};n={Times.Length};");

        for (var index = 0; index < Times.Length; index++)
        {
            payload.Append($"s.{samplerName}.{index}={Describe(Frame(subject, samplerName, Times[index]))};");
        }

        return payload.ToString();
    }

    /// <summary>
    /// 在被写控件上跑一条采样器的一帧，返回**从控件读回**的值。
    /// </summary>
    /// <remarks>
    /// 验收与演示台走的是同一个入口：载荷报的就是这个返回值，屏幕上那个控件持有的也是它 ——
    /// 界面上看到的和断言里读的必然是同一个数，不可能各说各话。
    /// </remarks>
    /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="t">该帧的缓动时间，可以越出 [0,1]。</param>
    internal static object? Frame(SamplerSubject subject, string samplerName, double t)
    {
        var probe = Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
            ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

        var property = TransitionProperty.FromProperty(typeof(SamplerSubject).GetProperty(probe.Property)!);

        // 每帧一对全新端点：端点实例跨帧复用会被采样器原地改动污染。
        object? working = null;
        probe.Create().InsertFrame(subject, property, ref working, probe.Start(), probe.End(), null, t);

        return property.GetValue(subject);
    }

    /// <summary>
    /// 值的规范文本：类型名打头，后面是该类型的分量，顺序固定。
    /// </summary>
    /// <remarks>
    /// 打头带上类型名是有意的 —— 采样器一旦换了产物的类型，或者这里的分量顺序被改动，验收侧会立刻发现，
    /// 而不是把两种情况都读成"数值对不上"。分量顺序与 <c>VeloxDev.AT</c> 的 <c>AvaloniaConformance</c>
    /// 逐条对应，改这里必须同步改那张表（编译器抓不到这种不一致）。
    /// </remarks>
    private static string Describe(object? value) => value switch
    {
        // 单个影子：颜色按颜色的规则走；OffsetX/Y、Blur、Spread 各自外推；IsInset 在 t=0.5 处切换，取 0/1。
        AvBoxShadows shadows when shadows.Count == 1
            => $"BoxShadows,{shadows[0].Color.A},{shadows[0].Color.R},{shadows[0].Color.G},{shadows[0].Color.B},"
             + $"{Number(shadows[0].OffsetX)},{Number(shadows[0].OffsetY)},{Number(shadows[0].Blur)},{Number(shadows[0].Spread)},"
             + $"{Number(shadows[0].IsInset ? 1 : 0)}",
        // 实心→实心：颜色按颜色的规则走，不透明度自成一界并在 [0,1] 饱和。
        AvISolidColorBrush brush
            => $"SolidColorBrush,{brush.Color.A},{brush.Color.R},{brush.Color.G},{brush.Color.B},{Number(brush.Opacity)}",
        AvColor color => $"Color,{color.A},{color.R},{color.G},{color.B}",
        AvCornerRadius radius
            => $"CornerRadius,{Number(radius.TopLeft)},{Number(radius.TopRight)},{Number(radius.BottomRight)},{Number(radius.BottomLeft)}",
        // 两端单位不同时保持起点，单位本身（GridUnitType.Pixel == 1 / Star == 2 / Auto == 0）也写进载荷。
        AvGridLength length => $"GridLength,{Number(length.Value)},{Number((int)length.GridUnitType)}",
        AvPixelPoint pixelPoint => $"PixelPoint,{pixelPoint.X},{pixelPoint.Y}",
        AvPixelRect pixelRect => $"PixelRect,{pixelRect.X},{pixelRect.Y},{pixelRect.Width},{pixelRect.Height}",
        AvPixelSize pixelSize => $"PixelSize,{pixelSize.Width},{pixelSize.Height}",
        AvPoint point => $"Point,{Number(point.X)},{Number(point.Y)}",
        // 单位不同时保持起点；RelativeUnit.Absolute == 1（Avalonia 的枚举序是 Relative, Absolute）。
        AvRelativePoint relativePoint
            => $"RelativePoint,{Number(relativePoint.Point.X)},{Number(relativePoint.Point.Y)},{Number((int)relativePoint.Unit)}",
        AvRelativeRect relativeRect
            => $"RelativeRect,{Number(relativeRect.Rect.X)},{Number(relativeRect.Rect.Y)},{Number(relativeRect.Rect.Width)},{Number(relativeRect.Rect.Height)},{Number((int)relativeRect.Unit)}",
        AvSize size => $"Size,{Number(size.Width)},{Number(size.Height)}",
        AvThickness thickness
            => $"Thickness,{Number(thickness.Left)},{Number(thickness.Top)},{Number(thickness.Right)},{Number(thickness.Bottom)}",
        // 变换：t==0 / t==1 原样交出调用方给的实例，中间与越界帧对已知的同类变换逐字段外推 —— TranslateTransform 即 X/Y。
        AvTranslateTransform translate => $"TranslateTransform,{Number(translate.X)},{Number(translate.Y)}",
        _ => throw new InvalidOperationException(
            $"SamplerProbe 不认识产物类型 {value?.GetType().Name ?? "null"}，序列化要跟着采样器一起改。"),
    };

    /// <summary>往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
