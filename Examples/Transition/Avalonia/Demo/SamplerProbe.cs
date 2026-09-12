using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

// System.Drawing 与 Avalonia 有同名的 Point / Size / Color，两个命名空间一旦同时被引入就是 CS0104。
// 别名把 Avalonia 那一侧钉死，文件里出现的每个类型都不再依赖 using 的解析顺序。
using AvBoxShadow = Avalonia.Media.BoxShadow;
using AvBoxShadows = Avalonia.Media.BoxShadows;
using AvColor = Avalonia.Media.Color;
using AvLinearGradientBrush = Avalonia.Media.LinearGradientBrush;
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

        /// <summary>
        /// 索引器路径的两条：它们验的不是某个采样器，而是**路径能落到具体的槽上**。
        /// </summary>
        /// <remarks>
        /// 名字不再取自采样器类型名 —— 两条用的是同一个 ColorSampler，区分它们的是路径里那个下标。
        /// 相邻下标必须是两条不同的路径：索引器的 PropertyInfo 对每个下标都是同一个 "Item"，
        /// 下标不并入身份的话这两行会合成一个状态条目，一条动画静默盖掉另一条 —— 而批量那一路
        /// 会立刻报出来（表里的条目在帧报告里缺席）。
        /// </remarks>
        internal const string GradientStop0Color = "GradientStop0Color";

        internal const string GradientStop1Color = "GradientStop1Color";
    }

    /// <summary>一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。</summary>
    /// <param name="Property">
    /// 被写的属性名；带索引器的路径用它表达不了，那些条目的这一项是 null、改由 <paramref name="Path"/> 给出路径。
    /// </param>
    /// <param name="Path">
    /// 一条完整路径，用于"属性名"说不清的那些条目 —— 尤其是带索引器的：<c>Ramp.GradientStops[0].Color</c>。
    /// </param>
    private sealed record ProbeSpec(
        string Name,
        string Description,
        string? Property,
        Func<ISampler> Create,
        Func<object> Start,
        Func<object> End,
        Func<TransitionProperty>? Path = null);

    /// <summary>一个产物读出来的样子：类型名 + 固定顺序的分量。</summary>
    /// <param name="TypeTag">产物的运行时类型名。认不出来的类型也照报，由测试侧去说"类型不对"。</param>
    /// <param name="Components">该类型的分量。认不出的类型没有分量，所以是空数组。</param>
    internal sealed record Measurement(string TypeTag, double[] Components);

    // ---- 端点：与 Samplers/AvaloniaEntries.cs 里的一致 ----

    private static readonly AvColor ColorStart = AvColor.FromArgb(200, 200, 100, 50);
    private static readonly AvColor ColorEnd = AvColor.FromArgb(250, 240, 180, 120);

    // 索引器那两条的端点：写的是同一个画刷的两个停靠点，所以两对颜色必须不同 —— 否则两条路径的闭式解
    // 一模一样，"各落各的槽"这件事就无从断言。
    private static readonly AvColor Stop0Start = AvColor.FromArgb(200, 200, 100, 50);
    private static readonly AvColor Stop0End = AvColor.FromArgb(250, 240, 180, 120);
    private static readonly AvColor Stop1Start = AvColor.FromArgb(120, 30, 200, 250);
    private static readonly AvColor Stop1End = AvColor.FromArgb(200, 210, 40, 10);

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
        new(Kinds.BoxShadowsSampler, "阴影：颜色按颜色的规则走，偏移/模糊/扩散各自外推；IsInset 在 t=0.5 处切换，是个离散量。", nameof(SamplerSubject.Shadows), () => new BoxShadowsSampler(),
            () => new AvBoxShadows(ShadowStart), () => new AvBoxShadows(ShadowEnd)),
        new(Kinds.BrushSampler, "实心刷：R/G/B 共用一个进度，不透明度自成一界并在 [0,1] 饱和 —— 共用进度是为了过冲时不偏色。", nameof(SamplerSubject.Fill), () => new BrushSampler(), BrushStart, BrushEnd),
        new(Kinds.GradientStop0Color,
            "索引器路径：写渐变第 0 个停靠点的颜色（Ramp.GradientStops[0].Color）—— 索引真的落到槽上，而不是被当成整条集合。",
            null, () => new ColorSampler(), () => Stop0Start, () => Stop0End, Path: () => GradientStopPath(0)),
        new(Kinds.GradientStop1Color,
            "索引器路径：相邻下标必须互不覆盖（Ramp.GradientStops[1].Color）—— 与上一行同一个采样器、同一刻并行跑。",
            null, () => new ColorSampler(), () => Stop1Start, () => Stop1End, Path: () => GradientStopPath(1)),

        new(Kinds.ColorSampler, "颜色：A 与 R/G/B 各自成界，越界饱和而不是回绕（回绕会变成完全不同的颜色）。", nameof(SamplerSubject.Tint), () => new ColorSampler(), () => ColorStart, () => ColorEnd),
        new(Kinds.CornerRadiusSampler, "圆角：四角各自插值、互不耦合，没有共用进度，过冲照常穿过端点。", nameof(SamplerSubject.Radius), () => new CornerRadiusSampler(),
            () => new AvCornerRadius(10, 20, 30, 40), () => new AvCornerRadius(110, 220, 330, 440)),
        new(Kinds.GridLengthSampler, "栅格长度：两端单位不同时保持起点 —— 单位换算没有插值的意义，所以这条是离散的。", nameof(SamplerSubject.Column), () => new GridLengthSampler(),
            () => new AvGridLength(10, AvGridUnitType.Pixel), () => new AvGridLength(2, AvGridUnitType.Star)),
        new(Kinds.PixelPointSampler, "像素点：两个整数分量，外推之后向零截断。", nameof(SamplerSubject.PixelSpot), () => new PixelPointSampler(),
            () => new AvPixelPoint(10, 20), () => new AvPixelPoint(110, 220)),
        new(Kinds.PixelRectSampler, "像素矩形：原点外推，宽高向零截断并钳在 0。", nameof(SamplerSubject.PixelBox), () => new PixelRectSampler(),
            () => new AvPixelRect(0, 0, 100, 50), () => new AvPixelRect(100, 200, 0, 150)),
        new(Kinds.PixelSizeSampler, "像素尺寸：两个整数分量向零截断并钳在 0。", nameof(SamplerSubject.PixelExtent), () => new PixelSizeSampler(),
            () => new AvPixelSize(100, 50), () => new AvPixelSize(0, 150)),
        new(Kinds.PointSampler, "二维点：两个分量纯外推，超出格子的行程靠固定标尺画得下。", nameof(SamplerSubject.Spot), () => new PointSampler(),
            () => new AvPoint(10, 20), () => new AvPoint(110, 220)),
        new(Kinds.RelativePointSampler, "相对点：两端单位（绝对/相对）不同时保持起点，与栅格长度同一条规则。", nameof(SamplerSubject.RelSpot), () => new RelativePointSampler(),
            () => new AvRelativePoint(10, 20, AvRelativeUnit.Absolute), () => new AvRelativePoint(0.5, 0.6, AvRelativeUnit.Relative)),
        new(Kinds.RelativeRectSampler, "相对矩形：单位不同时保持起点，五个分量里最后一个是单位码。", nameof(SamplerSubject.RelBox), () => new RelativeRectSampler(),
            () => new AvRelativeRect(10, 20, 30, 40, AvRelativeUnit.Absolute), () => new AvRelativeRect(0.1, 0.2, 0.3, 0.4, AvRelativeUnit.Relative)),
        new(Kinds.SizeSampler, "尺寸：宽高共用进度并在 0 处停住，下降的那一端过冲会被截住。", nameof(SamplerSubject.Extent), () => new SizeSampler(),
            () => new AvSize(100, 50), () => new AvSize(0, 150)),
        new(Kinds.ThicknessSampler, "厚度：四边各自外推、没有上下限，越过端点照走不误。", nameof(SamplerSubject.Inset), () => new ThicknessSampler(),
            () => new AvThickness(1, 2, 3, 4), () => new AvThickness(11, 22, 33, 44)),
        new(Kinds.TransformSampler, "变换：t=0/1 原样交出调用方给的实例，中间帧改的是自己那份草稿 —— 嵌套路径靠这个保住运行时类型。", nameof(SamplerSubject.Motion), () => new TransformSampler(), TranslateStart, TranslateEnd),
    ];

    /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    /// <summary>这一行在验什么。与把手、载荷同源，所以列表里那句话不可能与探针表各自漂移。</summary>
    internal static string Description(string samplerName) => Spec(samplerName).Description;

    /// <summary>
    /// 演示台把一条采样器跑一遍的时长。<c>VELOXDEV_BENCH_MS</c> 可以覆盖 —— 验收要在真 app 里把每条采样器都跑一遍，
    /// 默认时长下光播放就是几百秒，快跑时用它压短。
    /// </summary>
    internal static TimeSpan BenchDuration { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("VELOXDEV_BENCH_MS"), out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Max(1, milliseconds))
            : TimeSpan.FromMilliseconds(DefaultBenchMs);

    private const int DefaultBenchMs = 800;

    /// <summary>
    /// 跑完之后的兜底上限：值还在变就继续等，等到这里为止。真动画的最后一帧是排队投递的，
    /// 固定余量在负载重的机器上会读早，而"值不再变"才是它落地的判据。
    /// </summary>
    internal static readonly TimeSpan BenchSettleCap = TimeSpan.FromSeconds(2);

    /// <summary>这一条采样器的完整定义。名字对不上说明把手与探针表不同步了。</summary>
    private static ProbeSpec Spec(string samplerName)
        => Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
            ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

    /// <summary>这一条采样器写的路径 —— 单属性，或者一条带索引器的路径。</summary>
    /// <remarks>
    /// 每条只建一个实例。路径是状态字典的键，每次新建一个虽然按值相等，但索引实参来自闭包时未必相等；
    /// 缓存下来就没有这层疑问。
    /// </remarks>
    internal static TransitionProperty Path(string samplerName) => Paths[samplerName];

    private static readonly Dictionary<string, TransitionProperty> Paths =
        Probes.ToDictionary(
            static probe => probe.Name,
            static probe => probe.Path is null
                ? TransitionProperty.FromProperty(typeof(SamplerSubject).GetProperty(probe.Property!)!)
                : probe.Path());

    /// <summary>建一条 <c>Ramp.GradientStops[i].Color</c>。下标是常量，所以这条路径的身份只由下标决定。</summary>
    private static TransitionProperty GradientStopPath(int index)
        => TransitionProperty.TryCreate(
            (Expression<Func<SamplerSubject, AvColor>>)(subject =>
                ((AvLinearGradientBrush)subject.Ramp!).GradientStops[index].Color),
            out var property)
            ? property!
            : throw new InvalidOperationException($"索引器路径 GradientStops[{index}].Color 建不出来。");

    /// <summary>这一条采样器的实例。每次都要新的：采样器本身可能带状态。</summary>
    internal static ISampler Create(string samplerName) => Spec(samplerName).Create();

    /// <summary>这一条采样器声明的起点。</summary>
    internal static object Start(string samplerName) => Spec(samplerName).Start();

    /// <summary>这一条采样器声明的终点。</summary>
    internal static object End(string samplerName) => Spec(samplerName).End();

    /// <summary>
    /// 被写控件上这个属性**此刻**持有的值，按分量读出来。
    /// </summary>
    /// <remarks>
    /// 真动画那一段时间靠它采样：采样当刻就把分量取成数字，绝不把值对象留到后面 ——
    /// 画刷、变换这类产物每帧写的是同一个 scratch 实例、就地改，存下实例等于读到"后来"的状态。
    /// </remarks>
    internal static Measurement Read(SamplerSubject subject, string samplerName)
        => Measure(Path(samplerName).GetValue(subject));

    /// <summary>
    /// 这一行此刻的值是否**就是**它声明的起点。
    /// </summary>
    /// <remarks>
    /// 给顶栏那三个按钮用的可观测量。逐分量精确比较而不是带容差：重置是**把声明的那对端点原样写回去**，
    /// 所以"已经回到起点"必然是逐位相同，不需要容差去猜。离散型的那几条永远停在起点，于是它们恒为真 ——
    /// 这是它们应有的样子，不是漏报。
    /// </remarks>
    internal static bool MatchesStart(SamplerSubject subject, string samplerName)
        => SameComponents(Measure(Start(samplerName)).Components, Read(subject, samplerName).Components);

    /// <summary>两个分量向量是否逐位相同。用来判断"这一拍和上一拍比有没有变"。</summary>
    internal static bool SameComponents(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index]) return false;
        }

        return true;
    }

    /// <summary>
    /// 在被写控件上跑一条采样器，产出载荷文本，形如 <c>v=1;seq=3;n=5;s.BoxShadowsSampler.0=BoxShadows,200,…;</c>。
    /// </summary>
    /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
    internal static string Run(SamplerSubject subject, string samplerName, long sequence)
        => $"v=1;seq={sequence};n={Times.Length};" + RunFrames(subject, samplerName);

    /// <summary>
    /// 只要那五帧的字段，不带头部 —— 批量载荷把每一行的这一段拼在一起（逐行载荷与它同一套字段名，
    /// 所以两边共用同一个解析器）。
    /// </summary>
    internal static string RunFrames(SamplerSubject subject, string samplerName)
    {
        var payload = new StringBuilder();

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
        var probe = Spec(samplerName);
        var property = Path(samplerName);

        // 每帧一对全新端点：端点实例跨帧复用会被采样器原地改动污染。
        object? working = null;
        probe.Create().InsertFrame(subject, property, ref working, probe.Start(), probe.End(), null, t);

        return property.GetValue(subject);
    }

    /// <summary>
    /// 把一个产物读成类型名 + 分量，顺序固定。
    /// </summary>
    /// <remarks>
    /// 打头带上类型名是有意的 —— 采样器一旦换了产物的类型，或者这里的分量顺序被改动，验收侧会立刻发现，
    /// 而不是把两种情况都读成"数值对不上"。分量顺序与 <c>VeloxDev.AT</c> 的 <c>AvaloniaConformance</c>
    /// 逐条对应，改这里必须同步改那张表（编译器抓不到这种不一致）。
    /// <para>
    /// <b>认不出的类型不抛。</b> 这里读的是"控件此刻持有什么"，而"类型变了"本身就是要报给验收的异常之一；
    /// 在一个属性回调里抛出去只会把 demo 打挂，把异常变成一次崩溃。所以照实报类型名、分量留空，让测试侧去说。
    /// </para>
    /// </remarks>
    internal static Measurement Measure(object? value) => value switch
    {
        // 单个影子：颜色按颜色的规则走；OffsetX/Y、Blur、Spread 各自外推；IsInset 在 t=0.5 处切换，取 0/1。
        AvBoxShadows shadows when shadows.Count == 1
            => new("BoxShadows",
                [shadows[0].Color.A, shadows[0].Color.R, shadows[0].Color.G, shadows[0].Color.B,
                 shadows[0].OffsetX, shadows[0].OffsetY, shadows[0].Blur, shadows[0].Spread,
                 shadows[0].IsInset ? 1d : 0d]),
        // 实心→实心：颜色按颜色的规则走，不透明度自成一界并在 [0,1] 饱和。
        AvISolidColorBrush brush
            => new("SolidColorBrush", [brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B, brush.Opacity]),
        AvColor color => new("Color", [color.A, color.R, color.G, color.B]),
        AvCornerRadius radius
            => new("CornerRadius", [radius.TopLeft, radius.TopRight, radius.BottomRight, radius.BottomLeft]),
        // 两端单位不同时保持起点，单位本身（GridUnitType.Pixel == 1 / Star == 2 / Auto == 0）也写进载荷。
        AvGridLength length => new("GridLength", [length.Value, (int)length.GridUnitType]),
        AvPixelPoint pixelPoint => new("PixelPoint", [pixelPoint.X, pixelPoint.Y]),
        AvPixelRect pixelRect => new("PixelRect", [pixelRect.X, pixelRect.Y, pixelRect.Width, pixelRect.Height]),
        AvPixelSize pixelSize => new("PixelSize", [pixelSize.Width, pixelSize.Height]),
        AvPoint point => new("Point", [point.X, point.Y]),
        // 单位不同时保持起点；RelativeUnit.Absolute == 1（Avalonia 的枚举序是 Relative, Absolute）。
        AvRelativePoint relativePoint
            => new("RelativePoint",
                [relativePoint.Point.X, relativePoint.Point.Y, (int)relativePoint.Unit]),
        AvRelativeRect relativeRect
            => new("RelativeRect",
                [relativeRect.Rect.X, relativeRect.Rect.Y, relativeRect.Rect.Width, relativeRect.Rect.Height,
                 (int)relativeRect.Unit]),
        AvSize size => new("Size", [size.Width, size.Height]),
        AvThickness thickness
            => new("Thickness", [thickness.Left, thickness.Top, thickness.Right, thickness.Bottom]),
        // 变换：t==0 / t==1 原样交出调用方给的实例，中间与越界帧对已知的同类变换逐字段外推 —— TranslateTransform 即 X/Y。
        AvTranslateTransform translate => new("TranslateTransform", [translate.X, translate.Y]),
        null => new("null", []),
        _ => new(value.GetType().Name, []),
    };

    /// <summary>载荷里的规范文本：类型名打头，后面是逗号分隔的分量。</summary>
    private static string Describe(object? value)
    {
        var (typeTag, components) = Measure(value);
        return components.Length == 0 ? typeTag : $"{typeTag},{Vector(components)}";
    }

    /// <summary>往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Vector(IReadOnlyList<double> values) => string.Join(",", values.Select(Number));

    /// <summary>载荷字段里不能出现分隔符，异常消息还得是一行。</summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "-";

        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            cleaned.Append(character switch
            {
                ';' or '=' => '_',
                _ when char.IsControl(character) => ' ',
                _ => character,
            });
        }

        return cleaned.ToString();
    }

    /// <summary>
    /// 真动画那一段时间里对控件属性的采样累积。
    /// </summary>
    /// <remarks>
    /// 收的是 <see cref="Measurement"/> 里已经取成数字的分量，不是值对象（见 <see cref="Read"/>）。
    /// 非有限值单独计数、**不喂给 min/max** —— 一个 NaN 喂进去会把那个分量的包络永久粘住，
    /// 于是"有过 NaN"这件事就再也看不出来了。
    /// <para>
    /// <see cref="Settled"/> 是"最后一帧已经落地"的判据：流水线的末帧是排队投递的，固定余量在负载重的机器上
    /// 会读早，而值连续几拍不再变是它真的到了。
    /// </para>
    /// </remarks>
    internal sealed class LiveWatch
    {
        /// <summary>
        /// 连续多少拍同一个值算落定。
        /// </summary>
        /// <remarks>
        /// 三十拍（这条链路上每拍 16ms，约 480ms），不是两三拍。落定要代表的是"流水线的末帧已经落到目标上"，
        /// 而末帧是排队投递的：Blazor 上它得等电路线程腾出手，慢的时候一拍与下一拍之间能隔几十毫秒。
        /// <para>
        /// 更麻烦的是产物**被量化**的时候：Blazor 的 CSS 颜色字符串按三位小数格式化，动画末段缓动又被压平，
        /// 连着好几帧能格式化出同一个字符串 —— 于是"值不再变"在动画还没跑完时就成立了。实测：bench 1600ms、
        /// 窗口 5 拍时这条会误报成"没跑到终点"（3 次错 2 次），窗口 30 拍后 3 次全对。
        /// </para>
        /// </remarks>
        private const int SettledTicks = 30;

        private double[] _min = [];
        private double[] _max = [];
        private double[] _last = [];
        private double[] _previous = [];
        private int _unchanged;
        private int _samples;
        private int _bad;
        private string _type = "-";
        private string? _error;

        /// <summary>这段时间一共采到多少拍。</summary>
        internal int Samples => _samples;

        /// <summary>值连续几拍没变了。</summary>
        internal bool Settled => _samples > 0 && _unchanged >= SettledTicks;

        /// <summary>起这条动画时就抛出来的异常。放在每一份观察里，所以十几条并发时谁的错是谁的。</summary>
        internal void Fail(string error) => _error = error;

        internal void Reset()
        {
            _min = [];
            _max = [];
            _last = [];
            _previous = [];
            _unchanged = 0;
            _samples = 0;
            _bad = 0;
            _type = "-";
            _error = null;
            }

        internal void Observe(string typeTag, IReadOnlyList<double> components)
        {
            _type = typeTag;

            if (_last.Length != components.Count)
            {
                _min = new double[components.Count];
                _max = new double[components.Count];
                _last = new double[components.Count];
                _previous = new double[components.Count];
                Array.Fill(_min, double.PositiveInfinity);
                Array.Fill(_max, double.NegativeInfinity);
                _unchanged = 0;
            }

            _samples++;

            var changed = false;
            for (var index = 0; index < components.Count; index++)
            {
                var value = components[index];
                _last[index] = value;

                // 头一拍没有"上一拍"可比，算它变了，免得连续相等的起点提前报落定。
                if (_samples == 1 || !SameAsPrevious(value, _previous[index])) changed = true;
                _previous[index] = value;

                if (!double.IsFinite(value))
                {
                    _bad++;
                    continue;
                }

                if (value < _min[index]) _min[index] = value;
                if (value > _max[index]) _max[index] = value;
            }

            _unchanged = changed ? 0 : _unchanged + 1;
        }

        /// <summary>
        /// Whether this sample's value is the same as the previous one's.
        /// </summary>
        /// <remarks>
        /// NaN 要算作没变。用 <c>!=</c> 直接比的话，NaN 永远不等于自己，于是"值一直在变"，落定判据永远不成立，
        /// 一条真的产出了 NaN 的动画会把每一次采样都拖到兜底上限。而"有没有 NaN"已经由 <c>bad</c> 单独报了。
        /// </remarks>
        private static bool SameAsPrevious(double value, double previous)
            => value == previous || (double.IsNaN(value) && double.IsNaN(previous));

        /// <summary>
        /// 这段动画的机器可读结果：控件属性这段时间里被写成了什么。
        /// </summary>
        /// <param name="sampler">跑的是哪条采样器。</param>
        /// <param name="sequence">点击序号，与 <c>over.conf</c> 共用 —— 载荷靠它证明这一份是新的。</param>
        /// <param name="error">起动画时就抛出来的异常，没有则为 null。</param>
        internal string Digest(string sampler, long sequence)

            => $"v=1;seq={sequence};done=1;sampler={sampler};" + RowFields();


        /// <summary>

        /// 这一行那组字段，前缀是 <c>l.&lt;采样器名&gt;.</c> —— 批量载荷里十几行并排，靠它分得开。

        /// </summary>

        internal string BatchFields(string sampler)

        {

            var prefix = $"l.{sampler}.";

            var payload = new StringBuilder();


            foreach (var field in RowFields().Split(';', StringSplitOptions.RemoveEmptyEntries))

            {

                payload.Append(prefix).Append(field).Append(';');

            }


            return payload.ToString();

        }


        private string RowFields()

            => $"type={_type};k={_last.Length};samples={_samples};bad={_bad};err={Sanitize(_error)};"

             + $"last={Vector(_last)};min={Vector(_min)};max={Vector(_max)};";

        /// <summary>点击那一刻先写一份，让验收侧立刻看到这一份是新的，然后等 <see cref="Digest"/>。</summary>
        internal static string Pending(string sampler, long sequence)
            => $"v=1;seq={sequence};done=0;sampler={sampler};";
    }
}
