using System.Linq.Expressions;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

// 桌面 SDK 的隐式 using（System.Drawing / System.Windows.Media / System.Windows.Media.Media3D）在
// Point / Rect / Size / Thickness / CornerRadius / Color / Brush / Transform 上与 Jalium 大量同名，
// 别名把闭式解里出现的每个类型都钉死在 Jalium 那一侧，不再依赖 using 的解析顺序。
using JaliumAxisAngleRotation3D = Jalium.UI.Media.Media3D.AxisAngleRotation3D;
using JaliumBrush = Jalium.UI.Media.Brush;
using JaliumColor = Jalium.UI.Media.Color;
using JaliumCornerRadius = Jalium.UI.CornerRadius;
using JaliumPoint = Jalium.UI.Point;
using JaliumRect = Jalium.UI.Rect;
using JaliumRotateTransform3D = Jalium.UI.Media.Media3D.RotateTransform3D;
using JaliumSize = Jalium.UI.Size;
using JaliumSolidColorBrush = Jalium.UI.Media.SolidColorBrush;
using JaliumThickness = Jalium.UI.Thickness;
using JaliumTransform = Jalium.UI.Media.Transform;
using JaliumTransform3D = Jalium.UI.Media.Media3D.Transform3D;
using JaliumTranslateTransform = Jalium.UI.Media.TranslateTransform;
using JaliumVector3D = Jalium.UI.Media.Media3D.Vector3D;

namespace VeloxDev.SamplerTest;

/// <summary>Jalium 适配器注册的采样器，以及每个必须满足的闭式解。</summary>
/// <remarks>
/// 采样器类型按"程序集限定名"取，而不是直接写类名：七个适配器都把采样器放在同一个
/// <c>VeloxDev.Adapters.NativeSamplers</c> 命名空间下，PointSampler / ColorSampler / BrushSampler …
/// 因此跨程序集重名，直接写会在编译期撞成 CS0433。测试只通过 <see cref="ISampler"/> 与
/// <see cref="SamplerEntry.SamplerType"/> 使用采样器，所以把归属钉死在 VeloxDev.Jalium 即可。
/// </remarks>
internal static class JaliumEntries
{
    private const string Adapter = "Jalium";
    private const string JaliumAssembly = "VeloxDev.Jalium";

    /// <summary>取本适配器里那个采样器类型：命名空间 + 程序集限定名，绕开跨适配器的重名。</summary>
    private static Type SamplerType(string name)
        => Type.GetType($"VeloxDev.Adapters.NativeSamplers.{name}, {JaliumAssembly}", throwOnError: true)!;

    /// <summary>一个目标类，每个采样器一条属性，让每个条目都有真实的写入对象。</summary>
    private sealed class Target
    {
        public JaliumBrush Fill { get; set; } = null!;
        public JaliumColor Tint { get; set; }
        public JaliumCornerRadius Corners { get; set; }
        public JaliumPoint Spot { get; set; }
        public JaliumRect Bounds { get; set; }
        public JaliumSize Extent { get; set; }
        public JaliumThickness Margins { get; set; }
        public JaliumTransform Render { get; set; } = null!;
        public JaliumTransform3D Pose { get; set; } = null!;
    }

    /// <summary>
    /// 一条条目：把 <paramref name="start"/> 写进一个新目标，跑一帧 t，再读回真正落地的值。
    /// </summary>
    private static SamplerEntry Entry(
        string samplerName,
        SamplerRule rule,
        object start,
        object end,
        Expression<Func<Target, object?>> selector,
        Func<double, object?> expected,
        Func<object?, object?, bool>? equivalent = null)
    {
        if (!TransitionProperty.TryCreate(selector, out var property) || property is null)
        {
            throw new InvalidOperationException($"'{selector}' 不能描述一条可动画的属性路径。");
        }

        return new SamplerEntry
        {
            SamplerType = SamplerType(samplerName),
            Adapter = Adapter,
            Rule = rule,
            Equivalent = equivalent ?? ExactEquivalent,
            Write = (sampler, t) =>
            {
                var target = new Target();
                object? working = null;
                sampler.InsertFrame(target, property, ref working, start, end, null, t);
                return property.GetValue(target);
            },
            Expected = expected,
        };
    }

    /// <summary>默认比较：结构体结果（颜色、点、尺寸、矩形、厚度、圆角）逐位相等，只有引用类型要另配比较器。</summary>
    private static readonly Func<object?, object?, bool> ExactEquivalent =
        static (expected, actual) => object.Equals(expected, actual);

    private static double Lerp(double start, double end, double t) => start + (end - start) * t;

    private static double Clamp01(double value) => Math.Max(0d, Math.Min(1d, value));

    /// <summary>
    /// 一组通道共用一个进度：谁先出界就停在谁那里 —— 与库里的规则一致，但这里是独立重述的。
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

    /// <summary>饱和而不是回绕 —— 裸的 byte 转换会把 300 变成 44。</summary>
    private static byte Channel(double value)
    {
        if (value <= 0d) return 0;
        if (value >= 255d) return 255;
        return (byte)value;
    }

    /// <summary>R/G/B 共用一个 0..255 的进度、在边界停住；Alpha 自成一界，按 t 直走并在 0/255 饱和。</summary>
    private static JaliumColor ColorAt(double t, JaliumColor from, JaliumColor to)
    {
        var rgb = SharedProgress(t, 255d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        return JaliumColor.FromArgb(
            Channel(from.A + (to.A - from.A) * t),
            Channel(from.R + (to.R - from.R) * rgb),
            Channel(from.G + (to.G - from.G) * rgb),
            Channel(from.B + (to.B - from.B) * rgb));
    }

    /// <summary>SolidColorBrush 是引用类型，逐位相等不成立，按颜色与不透明度比。</summary>
    private static bool SolidBrushEquivalent(object? expected, object? actual)
        => expected is JaliumSolidColorBrush e && actual is JaliumSolidColorBrush a
           && e.Color.Equals(a.Color)
           && e.Opacity == a.Opacity;

    /// <summary>TranslateTransform 是引用类型，逐位相等不成立，按 X/Y 比。</summary>
    private static bool TranslateEquivalent(object? expected, object? actual)
        => expected is JaliumTranslateTransform e && actual is JaliumTranslateTransform a
           && e.X == a.X
           && e.Y == a.Y;

    /// <summary>RotateTransform3D 是引用类型，逐位相等不成立，按轴、角度与旋转中心比。</summary>
    private static bool Rotate3DEquivalent(object? expected, object? actual)
        => expected is JaliumRotateTransform3D e && actual is JaliumRotateTransform3D a
           && e.Rotation is JaliumAxisAngleRotation3D er
           && a.Rotation is JaliumAxisAngleRotation3D ar
           && er.Axis.Equals(ar.Axis)
           && er.Angle == ar.Angle
           && e.CenterX == a.CenterX
           && e.CenterY == a.CenterY
           && e.CenterZ == a.CenterZ;

    // 颜色端点：起始端 G/B 起步低、结束端整体抬升，用来同时压 t>1 的顶界与 t<0 的底界。
    private static readonly JaliumColor RgbStart = JaliumColor.FromArgb(200, 200, 100, 50);
    private static readonly JaliumColor RgbEnd = JaliumColor.FromArgb(250, 240, 180, 120);

    // 两个 SolidColorBrush 各带一个非默认不透明度，实心→实心动画走的就是 BrushSampler 的第一条分支。
    // 不透明度取 0.25 → 1：t=1.5 越过 1、t=-0.5 落到 0 以下，两个方向都能验到钳制。
    private static readonly JaliumSolidColorBrush BrushStart = new(RgbStart) { Opacity = 0.25d };
    private static readonly JaliumSolidColorBrush BrushEnd = new(RgbEnd) { Opacity = 1d };

    // 3D 旋转：两端同轴（单位 Z），命中 RotateTransform3D + AxisAngleRotation3D 的快速路 ——
    // 轴相同的守卫成立，才不会掉到 Matrix3D 逐分量插值那条回退路。
    private static readonly JaliumVector3D RotationAxis = new(0d, 0d, 1d);

    private static readonly JaliumRotateTransform3D PoseStart =
        new(new JaliumAxisAngleRotation3D(RotationAxis, 30d), 1d, 2d, 3d);

    private static readonly JaliumRotateTransform3D PoseEnd =
        new(new JaliumAxisAngleRotation3D(RotationAxis, 150d), 5d, 6d, 7d);

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        // 画刷：只登记实心↔实心那条路 —— 它才是"给一对起点/终点"驱动的路。颜色 R/G/B 共用一个 0..255 的
        // 进度、Alpha 自成一界；不透明度是另一条独立通道，在 0..1 停住。非实心的交叉淡入产出的是渲染后的
        // ImageBrush，RGB 没有闭式解，登记不了。
        Entry("BrushSampler", SamplerRule.Saturate, BrushStart, BrushEnd,
            x => x.Fill,
            t => new JaliumSolidColorBrush(ColorAt(t, RgbStart, RgbEnd))
            {
                Opacity = Clamp01(BrushStart.Opacity + (BrushEnd.Opacity - BrushStart.Opacity) * t),
            },
            SolidBrushEquivalent),

        // 颜色：R/G/B 共用一个 0..255 的进度、在边界停住；Alpha 自成一界。
        Entry("ColorSampler", SamplerRule.Saturate, RgbStart, RgbEnd,
            x => x.Tint,
            t => ColorAt(t, RgbStart, RgbEnd)),

        // 圆角：四个角各按 t 线性外推，没有任何界。构造顺序是 (左上, 右上, 右下, 左下)。
        Entry("CornerRadiusSampler", SamplerRule.Extrapolate,
            new JaliumCornerRadius(1, 2, 3, 4), new JaliumCornerRadius(11, 22, 33, 44),
            x => x.Corners,
            t => new JaliumCornerRadius(
                Lerp(1d, 11d, t), Lerp(2d, 22d, t), Lerp(3d, 33d, t), Lerp(4d, 44d, t))),

        // 点：两个分量各按 t 线性外推，没有界。
        Entry("PointSampler", SamplerRule.Extrapolate,
            new JaliumPoint(10, 20), new JaliumPoint(110, 220),
            x => x.Spot,
            t => new JaliumPoint(Lerp(10d, 110d, t), Lerp(20d, 220d, t))),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— Jalium 的 Rect 构造器遇负尺寸会抛。
        Entry("RectSampler", SamplerRule.Saturate,
            new JaliumRect(0, 0, 100, 50), new JaliumRect(100, 200, 0, 150),
            x => x.Bounds,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new JaliumRect(
                    Lerp(0d, 100d, t), Lerp(0d, 200d, t), Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        // 尺寸：宽高共用一个进度、在 0 处停住。
        Entry("SizeSampler", SamplerRule.Saturate,
            new JaliumSize(100, 50), new JaliumSize(0, 150),
            x => x.Extent,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new JaliumSize(Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        // 厚度：四条边各按 t 线性外推，构造顺序是 (左, 上, 右, 下)。
        Entry("ThicknessSampler", SamplerRule.Extrapolate,
            new JaliumThickness(10, 20, 30, 40), new JaliumThickness(110, 220, 330, 440),
            x => x.Margins,
            t => new JaliumThickness(
                Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t), Lerp(40d, 440d, t))),

        // 3D 变换：走的是同轴快速路 —— 角度与旋转中心逐分量外推，轴始终是起点那根（守卫要求两端同轴）。
        Entry("Transform3DSampler", SamplerRule.Extrapolate, PoseStart, PoseEnd,
            x => x.Pose,
            t => new JaliumRotateTransform3D(
                new JaliumAxisAngleRotation3D(RotationAxis, Lerp(30d, 150d, t)),
                Lerp(1d, 5d, t), Lerp(2d, 6d, t), Lerp(3d, 7d, t)),
            Rotate3DEquivalent),

        // 变换：t==0 / t==1 原样交出端点实例；其余帧走同类型快速路，X/Y 仍是同一份线性外推。
        // 用平移，避开 RotationDirection 那条角度分支。
        Entry("TransformSampler", SamplerRule.Extrapolate,
            new JaliumTranslateTransform(10, 20), new JaliumTranslateTransform(110, 220),
            x => x.Render,
            t => new JaliumTranslateTransform(Lerp(10d, 110d, t), Lerp(20d, 220d, t)),
            TranslateEquivalent),
    ];
}
