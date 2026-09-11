using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;

// 本工程同时引用 WinForms / WPF / WinUI，同名类型一律显式取 MAUI 的那一侧。
using MauiColor = Microsoft.Maui.Graphics.Color;
using MauiCornerRadius = Microsoft.Maui.CornerRadius;
using MauiPoint = Microsoft.Maui.Graphics.Point;
using MauiPointF = Microsoft.Maui.Graphics.PointF;
using MauiRect = Microsoft.Maui.Graphics.Rect;
using MauiSize = Microsoft.Maui.Graphics.Size;
using MauiSizeF = Microsoft.Maui.Graphics.SizeF;
using MauiThickness = Microsoft.Maui.Thickness;
using SysRectangleF = System.Drawing.RectangleF;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers the MAUI adapter ships, with the closed form each one follows.</summary>
/// <remarks>
/// 覆盖的是"结果本身是值"的那些采样器。BrushSampler / ShadowSampler / TransformSampler 的产物是
/// <c>BindableObject</c>，其类型初始化要求进程里有 MAUI 的平台件 —— 纯数据套件里
/// <c>new SolidColorBrush()</c> 抛的是 <c>Element</c> 静态构造失败。这三条登记在
/// <see cref="UnreachableSamplers"/> 里，由真跑起来的 demo 覆盖。
/// </remarks>
internal static class MauiEntries
{
    private const string Adapter = "MAUI";

    /// <summary>A target exposing one property per sampler, so each entry has something real to write through.</summary>
    private sealed class Target
    {
        public MauiColor Tint { get; set; } = null!;
        public MauiCornerRadius Corners { get; set; }
        public MauiPoint Spot { get; set; }
        public MauiPointF SpotF { get; set; }
        public MauiRect Bounds { get; set; }
        public SysRectangleF BoundsF { get; set; }
        public MauiSize Extent { get; set; }
        public MauiSizeF ExtentF { get; set; }
        public MauiThickness Margin { get; set; }
    }

    /// <summary>MAUI 适配器所在的程序集；同名的采样器只能从这里按类型名取。</summary>
    private static readonly Assembly SamplerAssembly = typeof(ShadowSampler).Assembly;

    /// <summary>
    /// 七个适配器把大量同名采样器放在同一个命名空间里，编译期直接写类型名会 CS0433。这里按程序集限定反射取
    /// MAUI 的那一个；<c>SamplerType</c> 覆盖为真实类型（覆盖校验与实例化都靠它）。
    /// </summary>
    private static SamplerEntry Entry<TValue>(
        string samplerName,
        SamplerRule rule,
        object start,
        object end,
        Expression<Func<Target, TValue>> selector,
        Func<double, TValue> expected,
        Func<object?, object?, bool>? equivalent = null)
        where TValue : notnull
    {
        var samplerType = SamplerAssembly.GetType($"VeloxDev.Adapters.NativeSamplers.{samplerName}", throwOnError: true)!;
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, Target, TValue>(sampler, Adapter, rule, start, end, selector, expected);

        return new SamplerEntry
        {
            SamplerType = samplerType,
            Adapter = entry.Adapter,
            Rule = entry.Rule,
            Write = entry.Write,
            Expected = entry.Expected,
            Equivalent = equivalent ?? entry.Equivalent,
        };
    }

    private static double Lerp(double start, double end, double t) => start + (end - start) * t;

    /// <summary>
    /// 一组通道共用一个进度：谁先出界就停在谁那里。独立重述库里的 <c>BoundedProgress</c>，不调用它。
    /// </summary>
    /// <param name="maximum">该组的上界：尺寸是 +∞（只有下界 0），颜色是 1（MAUI 的通道是 float）。</param>
    private static double SharedProgress(double t, double maximum, params (double Start, double End)[] channels)
    {
        var progress = t;
        foreach (var (start, end) in channels)
        {
            var delta = end - start;
            if (delta == 0d) continue;

            var byMaximum = (maximum - start) / delta;
            var byMinimum = (0d - start) / delta;
            var lower = Math.Min(byMaximum, byMinimum);
            var upper = Math.Max(byMaximum, byMinimum);

            if (upper < progress) progress = upper;
            if (lower > progress) progress = lower;
        }

        return progress;
    }

    /// <summary>通道饱和到 [0,1] —— MAUI 的通道是 float，越界不是回绕而是根本不该出现。</summary>
    private static double Channel(double value) => value <= 0d ? 0d : value >= 1d ? 1d : value;

    /// <summary>颜色：R/G/B 共用一个 [0,1] 的进度、在边界停住；Alpha 自成一界，按 t 直走并在 0/1 饱和。</summary>
    private static MauiColor ColorAt(double t, MauiColor from, MauiColor to)
    {
        var progress = SharedProgress(t, 1d, (from.Red, to.Red), (from.Green, to.Green), (from.Blue, to.Blue));

        return MauiColor.FromRgba(
            Channel(from.Red + (to.Red - from.Red) * progress),
            Channel(from.Green + (to.Green - from.Green) * progress),
            Channel(from.Blue + (to.Blue - from.Blue) * progress),
            Channel(from.Alpha + (to.Alpha - from.Alpha) * t));
    }

    /// <summary>
    /// <c>Microsoft.Maui.Graphics.Color</c> 是引用类型，逐位相等会退化成引用比较，所以按四个通道比。
    /// </summary>
    /// <remarks>
    /// 通道按容差比，理由与四元数那条相同：MAUI 的通道是 float，而这段闭式解和适配器是分开编译的，
    /// <c>a + (b - a) * t</c> 在两边收缩成什么由各自的 JIT 决定，最后一个 bit 会差 —— 实测差 1 个 ULP。
    /// 1e-5 比任何一条真实规则差异（共用进度、钳制、通道错位）都小几个数量级，不会把真错误放过去。
    /// </remarks>
    private static bool ColorEquivalent(object? expected, object? actual)
        => expected is MauiColor e && actual is MauiColor a
           && Close(e.Red, a.Red)
           && Close(e.Green, a.Green)
           && Close(e.Blue, a.Blue)
           && Close(e.Alpha, a.Alpha);

    private static bool Close(float left, float right) => Math.Abs(left - right) <= 1e-5f;

    private static readonly MauiColor TintStart = MauiColor.FromRgba(30d / 255d, 200d / 255d, 250d / 255d, 120d / 255d);
    private static readonly MauiColor TintEnd = MauiColor.FromRgba(210d / 255d, 40d / 255d, 10d / 255d, 200d / 255d);

    /// <summary>尺寸类的端点：宽 100→0、高 50→150，一涨一缩，正好逼出"共用进度、在 0 处停"这条规则。</summary>
    private const double WidthStart = 100d;
    private const double WidthEnd = 0d;
    private const double HeightStart = 50d;
    private const double HeightEnd = 150d;

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        // 颜色：R/G/B 共用一个进度、在 0..1 停止；Alpha 自成一界。
        Entry("ColorSampler", SamplerRule.Saturate,
            TintStart, TintEnd, (Target x) => x.Tint,
            t => ColorAt(t, TintStart, TintEnd),
            ColorEquivalent),

        // 圆角：四个分量各自外推，没有任何界。
        // CornerRadius 是只读属性，只能用构造函数，所以这里的位置参数就是 MAUI 的 (topLeft, topRight,
        // bottomLeft, bottomRight)。别照抄 WinUI 那一侧 —— 它的参数序是 (topLeft, topRight, bottomRight,
        // bottomLeft)，两者不同，本条目正是抓这种照抄的。
        Entry("CornerRadiusSampler", SamplerRule.Extrapolate,
            new MauiCornerRadius(1, 2, 3, 4),
            new MauiCornerRadius(11, 22, 33, 44),
            (Target x) => x.Corners,
            t => new MauiCornerRadius(
                Lerp(1d, 11d, t), Lerp(2d, 22d, t), Lerp(3d, 33d, t), Lerp(4d, 44d, t))),

        // 点：两个分量各自线性外推。
        Entry("PointSampler", SamplerRule.Extrapolate,
            new MauiPoint(10, 20), new MauiPoint(110, 220), (Target x) => x.Spot,
            t => new MauiPoint(Lerp(10d, 110d, t), Lerp(20d, 220d, t))),

        // 单精度点：算式在 float 里做，取整规则与双精度版相同，但每一步都是 float。
        Entry("PointFSampler", SamplerRule.Extrapolate,
            new MauiPointF(10, 20), new MauiPointF(110, 220), (Target x) => x.SpotF,
            t => new MauiPointF(10f + 100f * (float)t, 20f + 200f * (float)t)),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住。
        Entry("RectSampler", SamplerRule.Saturate,
            new MauiRect(0, 0, WidthStart, HeightStart), new MauiRect(100, 200, WidthEnd, HeightEnd),
            (Target x) => x.Bounds,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
                return new MauiRect(
                    Lerp(0d, 100d, t), Lerp(0d, 200d, t),
                    Lerp(WidthStart, WidthEnd, size), Lerp(HeightStart, HeightEnd, size));
            }),

        // 单精度矩形：注意它操作的是 System.Drawing.RectangleF，不是 MAUI 自己的 RectF。
        Entry("RectFSampler", SamplerRule.Saturate,
            new SysRectangleF(0, 0, (float)WidthStart, (float)HeightStart),
            new SysRectangleF(100, 200, (float)WidthEnd, (float)HeightEnd),
            (Target x) => x.BoundsF,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
                return new SysRectangleF(
                    0f + 100f * (float)t, 0f + 200f * (float)t,
                    (float)WidthStart + ((float)WidthEnd - (float)WidthStart) * (float)size,
                    (float)HeightStart + ((float)HeightEnd - (float)HeightStart) * (float)size);
            }),

        // 尺寸：宽高共用一个进度，在 0 处停止。
        Entry("SizeSampler", SamplerRule.Saturate,
            new MauiSize(WidthStart, HeightStart), new MauiSize(WidthEnd, HeightEnd), (Target x) => x.Extent,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
                return new MauiSize(
                    Lerp(WidthStart, WidthEnd, size), Lerp(HeightStart, HeightEnd, size));
            }),

        // 单精度尺寸：同上，运算在 float 里。
        Entry("SizeFSampler", SamplerRule.Saturate,
            new MauiSizeF((float)WidthStart, (float)HeightStart), new MauiSizeF((float)WidthEnd, (float)HeightEnd),
            (Target x) => x.ExtentF,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
                return new MauiSizeF(
                    (float)WidthStart + ((float)WidthEnd - (float)WidthStart) * (float)size,
                    (float)HeightStart + ((float)HeightEnd - (float)HeightStart) * (float)size);
            }),

        // 厚度：四个分量各自外推，没有任何界。
        Entry("ThicknessSampler", SamplerRule.Extrapolate,
            new MauiThickness(10, 20, 30, 40), new MauiThickness(110, 220, 330, 440), (Target x) => x.Margin,
            t => new MauiThickness(
                Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t), Lerp(40d, 440d, t))),
    ];
}
