using System.Linq.Expressions;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;

// 本工程同时引用了 WinForms 与 MAUI 适配器，它们的隐式 using 里有大量与 WPF 同名的类型。凡是用到的，
// 这里一律显式取 WPF 的那一侧；MAUI 的每次增补都只会在这里多一行。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using CornerRadius = System.Windows.CornerRadius;
using Effect = System.Windows.Media.Effects.Effect;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Thickness = System.Windows.Thickness;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers the WPF adapter ships, with the closed form each one follows.</summary>
internal static class WpfEntries
{
    private const string Adapter = "WPF";

    /// <summary>A target exposing one property per sampler, so each entry has something real to write through.</summary>
    private sealed class Target
    {
        public Brush Fill { get; set; } = null!;
        public Color Tint { get; set; }
        public CornerRadius Corners { get; set; }
        public Effect Shadow { get; set; } = null!;
        public Point3D Anchor3D { get; set; }
        public Point Anchor { get; set; }
        public Rect Bounds { get; set; }
        public Size Extent { get; set; }
        public Thickness Margin { get; set; }
        public Transform Render { get; set; } = null!;
        public Vector3D Axis3D { get; set; }
        public Vector Slope { get; set; }
    }

    /// <summary>WPF 适配器所在的程序集；同名的采样器只能从这里按类型名取。</summary>
    private static readonly Assembly SamplerAssembly = typeof(DropShadowEffectSampler).Assembly;

    /// <summary>
    /// 七个适配器把同名采样器（BrushSampler、PointSampler……）放在同一个命名空间里，编译期直接写类型名会
    /// CS0433（多个被引用的程序集都提供该类型）。这里按程序集限定反射取 WPF 的那一个。
    /// </summary>
    private static Type CrossAdapter(string samplerName)
        => SamplerAssembly.GetType($"VeloxDev.Adapters.NativeSamplers.{samplerName}", throwOnError: true)!;

    /// <summary>
    /// 一条条目：写一帧再读回。接线交给 <see cref="EntryFactory"/>，它按 <c>ISampler</c> 静态调用，
    /// 实际派发到真实采样器；<c>SamplerType</c> 随后覆盖为真实类型（覆盖校验与实例化都靠它）。
    /// </summary>
    private static SamplerEntry Entry<TValue>(
        Type samplerType,
        SamplerRule rule,
        object start,
        object end,
        Expression<Func<Target, TValue>> selector,
        Func<double, TValue> expected,
        Func<object?, object?, bool>? equivalent = null)
        where TValue : notnull
    {
        var sampler = (ISampler)Activator.CreateInstance(samplerType)!;
        var entry = EntryFactory.Create<ISampler, Target, TValue>(sampler, Adapter, rule, start, end, selector, expected);

        // 逐位相等只对"结果本身就是值"的类型成立；引用类型的结果（画刷、变换、阴影）会退化成引用比较，
        // 所以这三类自带一个比较器。
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

    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));

    /// <summary>
    /// 一组通道共用一个进度：谁先出界就停在谁那里。独立重述库里的规则，不调用库的辅助函数。
    /// </summary>
    /// <param name="maximum">该组的上界：尺寸是 +∞（只有下界 0），颜色是 255。</param>
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

    /// <summary>颜色通道饱和截断，不回绕 —— 裸 byte 转换会把 300 变成 44。</summary>
    private static byte Channel(double value) => value <= 0d ? (byte)0 : value >= 255d ? (byte)255 : (byte)value;

    /// <summary>R/G/B 共用一个上界 255 的进度、在边界停住；Alpha 自成一界，按 t 直走并在 0/255 饱和。</summary>
    private static Color ColorAt(double t, Color from, Color to)
    {
        var progress = SharedProgress(t, 255d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        return Color.FromArgb(
            Channel(from.A + (to.A - from.A) * t),
            Channel(from.R + (to.R - from.R) * progress),
            Channel(from.G + (to.G - from.G) * progress),
            Channel(from.B + (to.B - from.B) * progress));
    }

    /// <summary>画刷的实心↔实心分支返回的是可变引用类型，按颜色与不透明度比。</summary>
    private static bool SolidBrushEquivalent(object? expected, object? actual)
        => expected is SolidColorBrush e && actual is SolidColorBrush a
           && e.Color == a.Color
           && e.Opacity == a.Opacity;

    /// <summary>变换的快速路返回 scratch 实例，按分量比（t=0/1 交出的则是端点实例本身）。</summary>
    private static bool TranslateEquivalent(object? expected, object? actual)
        => expected is TranslateTransform e && actual is TranslateTransform a
           && e.X == a.X
           && e.Y == a.Y;

    /// <summary>阴影的快速路返回 scratch 实例，按五个字段比。</summary>
    private static bool EffectEquivalent(object? expected, object? actual)
        => expected is DropShadowEffect e && actual is DropShadowEffect a
           && e.Color == a.Color
           && e.Direction == a.Direction
           && e.ShadowDepth == a.ShadowDepth
           && e.Opacity == a.Opacity
           && e.BlurRadius == a.BlurRadius;

    // 画刷：实心↔实心那条路（颜色插值 + Opacity 自成一界）。
    private static readonly Color BrushColorStart = Color.FromArgb(200, 200, 100, 50);
    private static readonly Color BrushColorEnd = Color.FromArgb(250, 240, 180, 120);
    private static readonly SolidColorBrush BrushStart = new(BrushColorStart) { Opacity = 0.2 };
    private static readonly SolidColorBrush BrushEnd = new(BrushColorEnd) { Opacity = 0.8 };

    private static readonly Color TintStart = Color.FromArgb(120, 30, 200, 250);
    private static readonly Color TintEnd = Color.FromArgb(200, 210, 40, 10);

    // 阴影：同类型快速路（复用 scratch，从 pristine 的 start/end 逐帧重算）。
    private static readonly DropShadowEffect ShadowStart = new()
    {
        Color = BrushColorStart,
        Direction = 45,
        ShadowDepth = 10,
        Opacity = 0.2,
        BlurRadius = 10,
    };

    private static readonly DropShadowEffect ShadowEnd = new()
    {
        Color = BrushColorEnd,
        Direction = 135,
        ShadowDepth = 20,
        Opacity = 0.8,
        BlurRadius = 60,
    };

    // 变换：同类型快速路；用平移，避开 RotationDirection 那条角度分支。
    private static readonly TranslateTransform RenderStart = new(10, 20);
    private static readonly TranslateTransform RenderEnd = new(110, 220);

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        // 画刷：只登记实心↔实心那条路 —— 它才是"给一对起点/终点"驱动的路。非实心的交叉淡入
        // 产出的是渲染后的 ImageBrush，RGB 没有闭式解，登记不了（详见报告）。
        Entry(CrossAdapter("BrushSampler"), SamplerRule.Saturate,
            BrushStart, BrushEnd, (Target x) => x.Fill,
            t => new SolidColorBrush(ColorAt(t, BrushColorStart, BrushColorEnd))
            {
                Opacity = Clamp01(Lerp(0.2, 0.8, t)),
            },
            SolidBrushEquivalent),

        // 颜色：R/G/B 共用一个进度、在 0..255 停止；Alpha 自成一界。
        Entry(CrossAdapter("ColorSampler"), SamplerRule.Saturate,
            TintStart, TintEnd, (Target x) => x.Tint,
            t => ColorAt(t, TintStart, TintEnd)),

        // 圆角：四个分量各自外推，没有任何界。
        Entry(CrossAdapter("CornerRadiusSampler"), SamplerRule.Extrapolate,
            new CornerRadius(1, 2, 3, 4), new CornerRadius(11, 22, 33, 44), (Target x) => x.Corners,
            t => new CornerRadius(
                Lerp(1d, 11d, t), Lerp(2d, 22d, t), Lerp(3d, 33d, t), Lerp(4d, 44d, t))),

        // 阴影：颜色同颜色的规则；Direction / ShadowDepth / BlurRadius 外推；Opacity 在 0..1 停止。
        Entry(CrossAdapter("DropShadowEffectSampler"), SamplerRule.Saturate,
            ShadowStart, ShadowEnd, (Target x) => x.Shadow,
            t => new DropShadowEffect
            {
                Color = ColorAt(t, BrushColorStart, BrushColorEnd),
                Direction = Lerp(45d, 135d, t),
                ShadowDepth = Lerp(10d, 20d, t),
                Opacity = Clamp01(Lerp(0.2, 0.8, t)),
                BlurRadius = Lerp(10d, 60d, t),
            },
            EffectEquivalent),

        Entry(CrossAdapter("Point3DSampler"), SamplerRule.Extrapolate,
            new Point3D(10, 20, 30), new Point3D(110, 220, 330), (Target x) => x.Anchor3D,
            t => new Point3D(Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t))),

        Entry(CrossAdapter("PointSampler"), SamplerRule.Extrapolate,
            new Point(10, 20), new Point(110, 220), (Target x) => x.Anchor,
            t => new Point(Lerp(10d, 110d, t), Lerp(20d, 220d, t))),

        // 矩形：位置外推，宽高共用一个进度、在 0 处停止 —— 负尺寸 WPF 直接抛。
        Entry(CrossAdapter("RectSampler"), SamplerRule.Saturate,
            new Rect(0, 0, 100, 50), new Rect(100, 200, 0, 150), (Target x) => x.Bounds,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new Rect(
                    Lerp(0d, 100d, t), Lerp(0d, 200d, t), Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        // 尺寸：宽高共用一个进度，在 0 处停止 —— 负尺寸 WPF 直接抛。
        Entry(CrossAdapter("SizeSampler"), SamplerRule.Saturate,
            new Size(100, 50), new Size(0, 150), (Target x) => x.Extent,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new Size(Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        Entry(CrossAdapter("ThicknessSampler"), SamplerRule.Extrapolate,
            new Thickness(10, 20, 30, 40), new Thickness(110, 220, 330, 440), (Target x) => x.Margin,
            t => new Thickness(
                Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t), Lerp(40d, 440d, t))),

        // 变换：t==0 / t==1 原样交出端点实例，中间走同类型快速路 —— 值仍然是同一份线性外推。
        Entry(CrossAdapter("TransformSampler"), SamplerRule.Extrapolate,
            RenderStart, RenderEnd, (Target x) => x.Render,
            t => new TranslateTransform(Lerp(10d, 110d, t), Lerp(20d, 220d, t)),
            TranslateEquivalent),

        Entry(CrossAdapter("Vector3DSampler"), SamplerRule.Extrapolate,
            new Vector3D(10, 20, 30), new Vector3D(110, 220, 330), (Target x) => x.Axis3D,
            t => new Vector3D(Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t))),

        Entry(CrossAdapter("VectorSampler"), SamplerRule.Extrapolate,
            new Vector(10, 20), new Vector(110, 220), (Target x) => x.Slope,
            t => new Vector(Lerp(10d, 110d, t), Lerp(20d, 220d, t))),
    ];
}
