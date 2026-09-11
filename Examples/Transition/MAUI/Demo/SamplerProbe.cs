using System.Globalization;
using System.Text;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

// 同名类型一律显式取 MAUI 的那一侧：System.Drawing 里也有一份 PointF/RectF/SizeF，量纲不同、不能混。
using MauiColor = Microsoft.Maui.Graphics.Color;
using MauiCornerRadius = Microsoft.Maui.CornerRadius;
using MauiMatrix = Microsoft.Maui.Controls.Shapes.Matrix;
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
/// 把该适配器发布的每一个采样器各推一帧序列，把结果写成机器可读的载荷。
/// </summary>
/// <remarks>
/// 这是采样式验收在真 app 里的落点：采样器要写的对象（画刷、阴影、变换）都需要一个活着的框架运行时，
/// 而这里正是有运行时的进程。
/// <para>
/// 被写对象是 <see cref="SamplerSubject"/> —— 一个**真正在视觉树里的控件**，每条采样器一条同类型的可绑定属性。
/// 采样器直接写在它上面，载荷再从该属性读回，所以断言依据的是界面上那个控件实际持有的值。控件自己按属性重绘，
/// 于是"画出来"这件事不需要另一套映射代码。
/// </para>
/// <para>
/// 每条采样器由界面上的一个把手点击驱动，一次只跑一条 —— 验收侧因此走的是"点控件 → 触发处理函数 →
/// 采样器写值 → 读回"这条真实的 UI 交互路径，而不是让 app 在启动时闷头算完一遍。
/// </para>
/// <para>
/// 端点每帧全新构造：同一个端点实例跨 t 复用会被采样器原地改写污染，而「不得改动交给 InsertFrame 的
/// start/end」是库对采样器的硬约束 —— 这里正是那条约束的观察点。MAUI 的 <c>Color</c>、画刷、阴影、变换都是引用
/// 类型（而且构造它们要求进程里有平台件 —— 纯数据套件正是卡在这一步，见 <c>UnreachableSamplers</c>），
/// 所以端点给的是工厂而不是值。也正因为不在 <c>IDispatcherTimer.Tick</c> 里，那条「Tick 里的异常在 MAUI 上
/// 没人接」的陷阱不必碰 —— 但每一步仍然按不抛写。
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
        internal const string BrushSampler = nameof(BrushSampler);
        internal const string ColorSampler = nameof(ColorSampler);
        internal const string CornerRadiusSampler = nameof(CornerRadiusSampler);
        internal const string PointFSampler = nameof(PointFSampler);
        internal const string PointSampler = nameof(PointSampler);
        internal const string RectFSampler = nameof(RectFSampler);
        internal const string RectSampler = nameof(RectSampler);
        internal const string ShadowSampler = nameof(ShadowSampler);
        internal const string SizeFSampler = nameof(SizeFSampler);
        internal const string SizeSampler = nameof(SizeSampler);
        internal const string ThicknessSampler = nameof(ThicknessSampler);
        internal const string TransformSampler = nameof(TransformSampler);
    }

    /// <summary>一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。</summary>
    private sealed record ProbeSpec(string Name, string Property, Func<ISampler> Create, Func<object> Start, Func<object> End);

    // ---- 端点：与 Samplers/MauiEntries.cs 里的一致 ----

    // 颜色通道是 0..1 的 float（不是 8 位字节），所以端点按 /255 写回同一组 ARGB 值。
    private static readonly MauiColor BrushStartColor = MauiColor.FromRgba(200d / 255d, 100d / 255d, 50d / 255d, 200d / 255d);
    private static readonly MauiColor BrushEndColor = MauiColor.FromRgba(240d / 255d, 180d / 255d, 120d / 255d, 250d / 255d);

    private static SolidColorBrush BrushStart() => new(BrushStartColor);
    private static SolidColorBrush BrushEnd() => new(BrushEndColor);

    private static MauiColor TintStart() => MauiColor.FromRgba(30d / 255d, 200d / 255d, 250d / 255d, 120d / 255d);
    private static MauiColor TintEnd() => MauiColor.FromRgba(210d / 255d, 40d / 255d, 10d / 255d, 200d / 255d);

    /// <summary>阴影的端点：颜色同上，偏移、半径、不透明度各取一对差别明显、且能把三条规则分开的值。</summary>
    private static MauiShadow ShadowStart() => new()
    {
        Brush = new SolidColorBrush(BrushStartColor),
        Offset = new MauiPoint(10, 20),
        Radius = 10f,
        Opacity = 0.2f,
    };

    private static MauiShadow ShadowEnd() => new()
    {
        Brush = new SolidColorBrush(BrushEndColor),
        Offset = new MauiPoint(110, 220),
        Radius = 60f,
        Opacity = 0.8f,
    };

    /// <summary>
    /// 变换的端点用基类 <c>Transform</c>，不用 <c>TranslateTransform</c>：采样器在 t==0 / t==1 原样交出调用方的
    /// 实例，中间帧交出自己那块基类草稿 —— 端点若是派生类型，五个帧的产物就会有两种类型名，而载荷里一个采样器
    /// 只有一个类型标签。矩阵是单位阵加偏移，读出来的偏移就是 WPF 那一侧的 (X, Y)。
    /// </summary>
    private static MauiTransform TransformStart() => new() { Value = new MauiMatrix(1f, 0f, 0f, 1f, 10f, 20f) };

    private static MauiTransform TransformEnd() => new() { Value = new MauiMatrix(1f, 0f, 0f, 1f, 110f, 220f) };

    /// <summary>
    /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手与舞台格子都从这张表生成。
    /// </summary>
    private static readonly ProbeSpec[] Probes =
    [
        new(Kinds.BrushSampler, nameof(SamplerSubject.Fill), () => new BrushSampler(), BrushStart, BrushEnd),
        new(Kinds.ColorSampler, nameof(SamplerSubject.Tint), () => new ColorSampler(), TintStart, TintEnd),
        new(Kinds.CornerRadiusSampler, nameof(SamplerSubject.Corners), () => new CornerRadiusSampler(),
            () => new MauiCornerRadius(1, 2, 3, 4), () => new MauiCornerRadius(11, 22, 33, 44)),
        new(Kinds.PointSampler, nameof(SamplerSubject.Anchor), () => new PointSampler(),
            () => new MauiPoint(10, 20), () => new MauiPoint(110, 220)),
        new(Kinds.PointFSampler, nameof(SamplerSubject.AnchorF), () => new PointFSampler(),
            () => new MauiPointF(10, 20), () => new MauiPointF(110, 220)),
        new(Kinds.RectSampler, nameof(SamplerSubject.Area), () => new RectSampler(),
            () => new MauiRect(0, 0, 100, 50), () => new MauiRect(100, 200, 0, 150)),
        new(Kinds.RectFSampler, nameof(SamplerSubject.AreaF), () => new RectFSampler(),
            () => new SysRectangleF(0, 0, 100, 50), () => new SysRectangleF(100, 200, 0, 150)),
        new(Kinds.ShadowSampler, nameof(SamplerSubject.ShadowValue), () => new ShadowSampler(), ShadowStart, ShadowEnd),
        new(Kinds.SizeSampler, nameof(SamplerSubject.Extent), () => new SizeSampler(),
            () => new MauiSize(100, 50), () => new MauiSize(0, 150)),
        new(Kinds.SizeFSampler, nameof(SamplerSubject.ExtentF), () => new SizeFSampler(),
            () => new MauiSizeF(100, 50), () => new MauiSizeF(0, 150)),
        new(Kinds.ThicknessSampler, nameof(SamplerSubject.Inset), () => new ThicknessSampler(),
            () => new MauiThickness(10, 20, 30, 40), () => new MauiThickness(110, 220, 330, 440)),
        new(Kinds.TransformSampler, nameof(SamplerSubject.Render), () => new TransformSampler(), TransformStart, TransformEnd),
    ];

    /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    /// <summary>
    /// 在被写控件上跑一条采样器的五个固定缓动时间，产出载荷文本，形如
    /// <c>v=1;seq=3;n=5;s.BrushSampler.0=SolidColorBrush,0.78,…;</c>。
    /// </summary>
    /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
    internal static string Run(SamplerSubject subject, string samplerName, long sequence)
    {
        var payload = new StringBuilder($"v=1;seq={sequence};n={Times.Length};");

        // 写一帧、读回、序列化。每一帧都走 Frame —— 载荷报的值与演示台画的值因此必然同源。
        for (var index = 0; index < Times.Length; index++)
            payload.Append($"s.{samplerName}.{index}={Describe(Frame(subject, samplerName, Times[index]))};");

        return payload.ToString();
    }

    /// <summary>
    /// 在被写控件上跑一条采样器的一帧，返回**从控件读回**的值。
    /// </summary>
    /// <remarks>
    /// 验收与演示台走的是同一个入口：载荷报的就是这个返回值，屏幕上那个控件持有的也是它 ——
    /// 界面上看到的和断言里读的必然是同一个数，不可能各说各话。
    /// <para>
    /// 属性路径取自 <see cref="SamplerSubject"/> 上对应的那条可绑定属性 —— 与真实动画走的是同一条路。
    /// 端点收的是工厂：每帧都要一对全新的端点，跨 t 复用会被原地改写污染。
    /// </para>
    /// </remarks>
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
    /// 值的规范文本：类型名打头，后面是该类型的分量，顺序固定 —— 与 <c>Conformance/MauiConformance.cs</c> 里那张
    /// 表的分量序逐项对齐。
    /// </summary>
    /// <remarks>
    /// 打头带上类型名是有意的 —— 采样器一旦换了产物的类型，或者这里的分量顺序被改动，验收侧会立刻发现，
    /// 而不是把两种情况都读成"数值对不上"。
    /// </remarks>
    private static string Describe(object? value) => value switch
    {
        // MAUI 的 Brush 没有自己的 Opacity（不像 WPF 的 SolidColorBrush），不透明度只在颜色的 alpha 通道上。
        SolidColorBrush brush => $"SolidColorBrush,{Channels(brush.Color)}",
        MauiColor color => $"Color,{Channels(color)}",
        // 分量序是本类型自己的序 (TopLeft, TopRight, BottomLeft, BottomRight)：MAUI 的构造函数与属性都是这个序，
        // 与 WPF 的 (…, BottomRight, BottomLeft) 不同，照抄 WPF 会把左下与右下读反。
        MauiCornerRadius radius
            => $"CornerRadius,{Number(radius.TopLeft)},{Number(radius.TopRight)},{Number(radius.BottomLeft)},{Number(radius.BottomRight)}",
        // 颜色（析取：t >= 0.5 取端点刷，不插色）、偏移、不透明度、半径。
        MauiShadow shadow
            => $"Shadow,{Channels(BrushColor(shadow))},{Number(shadow.Offset.X)},{Number(shadow.Offset.Y)},"
             + $"{Number(shadow.Opacity)},{Number(shadow.Radius)}",
        MauiPoint point => $"Point,{Number(point.X)},{Number(point.Y)}",
        MauiPointF pointF => $"PointF,{Number(pointF.X)},{Number(pointF.Y)}",
        MauiRect rect => $"Rect,{Number(rect.X)},{Number(rect.Y)},{Number(rect.Width)},{Number(rect.Height)}",
        // 不是 MAUI 自己的 RectF：适配器的 RectFSampler 操作的是 System.Drawing.RectangleF。
        SysRectangleF rectF => $"RectangleF,{Number(rectF.X)},{Number(rectF.Y)},{Number(rectF.Width)},{Number(rectF.Height)}",
        MauiSize size => $"Size,{Number(size.Width)},{Number(size.Height)}",
        MauiSizeF sizeF => $"SizeF,{Number(sizeF.Width)},{Number(sizeF.Height)}",
        MauiThickness thickness
            => $"Thickness,{Number(thickness.Left)},{Number(thickness.Top)},{Number(thickness.Right)},{Number(thickness.Bottom)}",
        // 基类 Transform 唯一可读的值就是它的矩阵，六个分量按声明序。
        MauiTransform transform
            => $"Transform,{Number(transform.Value.M11)},{Number(transform.Value.M12)},{Number(transform.Value.M21)},"
             + $"{Number(transform.Value.M22)},{Number(transform.Value.OffsetX)},{Number(transform.Value.OffsetY)}",
        // 采样器没写成功时是 null：报成 "null" 让验收侧把它读成"类型不对"，而不是在这里抛成未处理异常。
        null => "null",
        _ => throw new InvalidOperationException(
            $"SamplerProbe 不认识产物类型 {value.GetType().Name}，序列化要跟着采样器一起改。"),
    };

    /// <summary>颜色的四个通道，序为 A,R,G,B —— 与验收表一致。</summary>
    /// <remarks>
    /// <c>Color</c> 是引用类型，未赋值时可能是 null，每一步都不许抛。
    /// </remarks>
    private static string Channels(MauiColor? color)
        => color is null
            ? "0,0,0,0"
            : $"{Number(color.Alpha)},{Number(color.Red)},{Number(color.Green)},{Number(color.Blue)}";

    /// <summary>阴影当前那支刷子的颜色；非纯色刷（或没刷子）时退回全透明。</summary>
    private static MauiColor? BrushColor(MauiShadow shadow)
        => shadow.Brush is SolidColorBrush solid ? solid.Color : null;

    /// <summary>往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
