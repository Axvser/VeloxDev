using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;

// 本工程同时引用 WinForms / WPF / MAUI，同名类型一律显式取 WinUI 的那一侧。
using WinBrush = Microsoft.UI.Xaml.Media.Brush;
using WinColor = Windows.UI.Color;
using WinCornerRadius = Microsoft.UI.Xaml.CornerRadius;
using WinGridLength = Microsoft.UI.Xaml.GridLength;
using WinGridUnitType = Microsoft.UI.Xaml.GridUnitType;
using WinPoint = Windows.Foundation.Point;
using WinRect = Windows.Foundation.Rect;
using WinSize = Windows.Foundation.Size;
using WinThickness = Microsoft.UI.Xaml.Thickness;

namespace VeloxDev.SamplerTest;

/// <summary>The samplers the WinUI adapter ships, with the closed form each one follows.</summary>
/// <remarks>
/// 覆盖的是"结果本身是值"的那些采样器。BrushSampler / ProjectionSampler / TransformSampler 的产物是
/// WinRT 的 DependencyObject，而 WinRT 类激活要求进程里有一个真正的 XAML 运行时 —— 纯数据套件里
/// <c>new SolidColorBrush()</c> 直接就是 <c>REGDB_E_CLASSNOTREG</c>。这三条登记在
/// <see cref="UnreachableSamplers"/> 里，由真跑起来的 demo 覆盖。
/// </remarks>
internal static class WinUiEntries
{
    private const string Adapter = "WinUI";

    /// <summary>A target exposing one property per sampler, so each entry has something real to write through.</summary>
    private sealed class Target
    {
        public WinBrush Fill { get; set; } = null!;
        public WinColor Tint { get; set; }
        public WinCornerRadius Corners { get; set; }
        public WinGridLength Column { get; set; }
        public WinPoint Spot { get; set; }
        public WinRect Bounds { get; set; }
        public WinSize Extent { get; set; }
        public WinThickness Margin { get; set; }
    }

    /// <summary>WinUI 适配器所在的程序集；同名的采样器只能从这里按类型名取。</summary>
    private static readonly Assembly SamplerAssembly = typeof(ProjectionSampler).Assembly;

    /// <summary>
    /// 七个适配器把大量同名采样器放在同一个命名空间里，编译期直接写类型名会 CS0433。这里按程序集限定反射取
    /// WinUI 的那一个；<c>SamplerType</c> 覆盖为真实类型（覆盖校验与实例化都靠它）。
    /// </summary>
    private static SamplerEntry Entry<TValue>(
        string samplerName,
        SamplerRule rule,
        object start,
        object end,
        Expression<Func<Target, TValue>> selector,
        Func<double, TValue> expected)
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
            Equivalent = entry.Equivalent,
        };
    }

    private static double Lerp(double start, double end, double t) => start + (end - start) * t;

    /// <summary>
    /// 一组通道共用一个进度：谁先出界就停在谁那里。独立重述库里的 <c>BoundedProgress</c>，不调用它。
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

    /// <summary>通道饱和截断，不回绕 —— 裸 byte 转换会把 300 变成 44。</summary>
    private static byte Channel(double value) => value <= 0d ? (byte)0 : value >= 255d ? (byte)255 : (byte)value;

    /// <summary>圆角的负值 WinUI 直接拒收（构造函数就 Validate），所以停在 0 处。</summary>
    private static double ClampAtZero(double value) => value <= 0d ? 0d : value;

    /// <summary>颜色：R/G/B 共用一个 [0,255] 的进度、在边界停住；Alpha 自成一界，按 t 直走并在 0/255 饱和。</summary>
    private static WinColor ColorAt(double t, WinColor from, WinColor to)
    {
        var progress = SharedProgress(t, 255d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        return WinColor.FromArgb(
            Channel(from.A + (to.A - from.A) * t),
            Channel(from.R + (to.R - from.R) * progress),
            Channel(from.G + (to.G - from.G) * progress),
            Channel(from.B + (to.B - from.B) * progress));
    }

    private static readonly WinColor TintStart = WinColor.FromArgb(120, 30, 200, 250);
    private static readonly WinColor TintEnd = WinColor.FromArgb(200, 210, 40, 10);

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        // 颜色：R/G/B 共用一个进度、在 0..255 停止；Alpha 自成一界。
        Entry("ColorSampler", SamplerRule.Saturate,
            TintStart, TintEnd, (Target x) => x.Tint,
            t => ColorAt(t, TintStart, TintEnd)),

        // 圆角：四个分量各自外推、各自在 0 处钳住 —— 不共用进度，因为四个角本来就互不相干，
        // 谁先缩到 0 与另外三个无关。钳制不是装饰：WinUI 的 CornerRadius 只接受非负值，连构造函数都 Validate，
        // 不钳就是抛异常。端点取 (1,2,3,4) → (11,22,33,44)，t = -0.5 上四个分量全被打到负数，
        // 正是钳制那一支被走到的地方。
        // 端点用对象初始化器而不是构造函数：它的参数序是 (topLeft, topRight, bottomRight, bottomLeft)，
        // 与属性序不同，写位置参数会让"哪个角配哪个角"取决于记性。
        Entry("CornerRadiusSampler", SamplerRule.Saturate,
            new WinCornerRadius { TopLeft = 1, TopRight = 2, BottomRight = 3, BottomLeft = 4 },
            new WinCornerRadius { TopLeft = 11, TopRight = 22, BottomRight = 33, BottomLeft = 44 },
            (Target x) => x.Corners,
            t => new WinCornerRadius
            {
                TopLeft = ClampAtZero(Lerp(1d, 11d, t)),
                TopRight = ClampAtZero(Lerp(2d, 22d, t)),
                BottomRight = ClampAtZero(Lerp(3d, 33d, t)),
                BottomLeft = ClampAtZero(Lerp(4d, 44d, t)),
            }),

        // 栅格长度：这里验的是"两端同为 Pixel"那条插值路，它比另一条更值得盯 —— 长度在 0 处钳住，
        // 端点取 10 → 110，t = -0.5 上被外推到 -40，正是钳制那一支。不钳就是 ArgumentException：
        // GridLength 只收非负值，连构造函数都 Validate。
        // 另一条路（两端单位不同，无法插值）是 t < 1 返回起点、t >= 1 直接切到终点 —— 注意这与 Avalonia
        // 的同一个采样器不同，那里是无论如何都返回起点。一条采样器在注册表里只留一个条目，所以那条这里不覆盖。
        Entry("GridLengthSampler", SamplerRule.Saturate,
            new WinGridLength(10, WinGridUnitType.Pixel), new WinGridLength(110, WinGridUnitType.Pixel),
            (Target x) => x.Column,
            t => new WinGridLength(ClampAtZero(10d + 100d * t), WinGridUnitType.Pixel)),

        // 点：两个分量各自线性外推，没有界。
        Entry("PointSampler", SamplerRule.Extrapolate,
            new WinPoint(10, 20), new WinPoint(110, 220), (Target x) => x.Spot,
            t => new WinPoint(Lerp(10d, 110d, t), Lerp(20d, 220d, t))),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— 负尺寸不可表示。
        Entry("RectSampler", SamplerRule.Saturate,
            new WinRect(0, 0, 100, 50), new WinRect(100, 200, 0, 150), (Target x) => x.Bounds,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new WinRect(
                    Lerp(0d, 100d, t), Lerp(0d, 200d, t), Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        // 尺寸：宽高共用一个进度，在 0 处停止。
        Entry("SizeSampler", SamplerRule.Saturate,
            new WinSize(100, 50), new WinSize(0, 150), (Target x) => x.Extent,
            t =>
            {
                var size = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new WinSize(Lerp(100d, 0d, size), Lerp(50d, 150d, size));
            }),

        // 厚度：四个分量各自外推，没有任何界。
        Entry("ThicknessSampler", SamplerRule.Extrapolate,
            new WinThickness(10, 20, 30, 40), new WinThickness(110, 220, 330, 440), (Target x) => x.Margin,
            t => new WinThickness(
                Lerp(10d, 110d, t), Lerp(20d, 220d, t), Lerp(30d, 330d, t), Lerp(40d, 440d, t))),
    ];
}
