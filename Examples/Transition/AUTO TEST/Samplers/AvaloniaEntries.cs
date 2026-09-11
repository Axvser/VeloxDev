using System.Linq.Expressions;
using System.Reflection;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;

// 桌面 SDK 的隐式 using 把 System.Drawing 带进了本文件，Point / Size / Color 与 Avalonia 同名，直接写会 CS0104。
// 别名把 Avalonia 那一侧钉死，闭式解里出现的每个类型都不再依赖 using 的解析顺序。
using AvBoxShadow = Avalonia.Media.BoxShadow;
using AvBoxShadows = Avalonia.Media.BoxShadows;
using AvBrushes = Avalonia.Media.Brushes;
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
using AvTransform = Avalonia.Media.Transform;
using AvTranslateTransform = Avalonia.Media.TranslateTransform;

namespace VeloxDev.SamplerTest;

/// <summary>Avalonia 适配器注册的采样器，以及每个必须满足的闭式解。</summary>
internal static class AvaloniaEntries
{
    private const string Adapter = "Avalonia";

    /// <summary>一个目标类，每个采样器一条属性，让每个条目都有真实的写入对象。</summary>
    private sealed class Target
    {
        public AvBoxShadows Shadows { get; set; }
        public AvIBrush Fill { get; set; } = AvBrushes.Transparent;
        public AvColor Tint { get; set; }
        public AvCornerRadius Radius { get; set; }
        public AvGridLength Column { get; set; }
        public AvPixelPoint PixelSpot { get; set; }
        public AvPixelRect PixelBox { get; set; }
        public AvPixelSize PixelExtent { get; set; }
        public AvPoint Spot { get; set; }
        public AvRelativePoint RelSpot { get; set; }
        public AvRelativeRect RelBox { get; set; }
        public AvSize Extent { get; set; }
        public AvThickness Margins { get; set; }
        public AvTransform Render { get; set; } = new AvTranslateTransform();
    }

    /// <summary>Avalonia 适配器所在的程序集；同名的采样器只能从这里按类型名取。</summary>
    private static readonly Assembly SamplerAssembly = typeof(BoxShadowsSampler).Assembly;

    /// <summary>
    /// WPF / WinUI / Jalium 适配器把同名采样器（BrushSampler、PointSampler……）放在同一个命名空间里，
    /// 编译期直接写类型名会 CS0433（多个被引用的程序集都提供该类型）。这里按程序集限定反射取 Avalonia 的那一个。
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

        // 逐位相等只对"结果本身就是值"的类型成立；引用类型的结果（笔刷、变换）和内部带数组的结构体
        // （BoxShadows）默认比较会退化成引用比较，所以自带一个比较器。
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
    private static AvColor LerpSharedRgb(AvColor c1, AvColor c2, double t)
    {
        var progress = SharedProgress(t, 255d, (c1.R, c2.R), (c1.G, c2.G), (c1.B, c2.B));

        return AvColor.FromArgb(
            Channel(c1.A + (c2.A - c1.A) * t),
            Channel(c1.R + (c2.R - c1.R) * progress),
            Channel(c1.G + (c2.G - c1.G) * progress),
            Channel(c1.B + (c2.B - c1.B) * progress));
    }

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

    private static readonly AvColor ColorStart = AvColor.FromArgb(200, 200, 100, 50);
    private static readonly AvColor ColorEnd = AvColor.FromArgb(250, 240, 180, 120);

    // 两个 SolidColorBrush 各带一个非默认不透明度，实心→实心动画走的就是 BrushSampler 的第一条分支。
    private static readonly AvSolidColorBrush BrushStart = new() { Color = ColorStart, Opacity = 0.25d };
    private static readonly AvSolidColorBrush BrushEnd = new() { Color = ColorEnd, Opacity = 0.75d };

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        // 单个影子：OffsetX/Y、Blur、Spread 各按 t 线性外推；颜色 R/G/B 共用一个 0..255 的进度，
        // Alpha 自成一界；IsInset 在 t=0.5 处切换（外推不会插出中间态）。
        Entry(typeof(BoxShadowsSampler), SamplerRule.Saturate,
            new AvBoxShadows(ShadowStart), new AvBoxShadows(ShadowEnd),
            x => x.Shadows,
            t => new AvBoxShadows(new AvBoxShadow
            {
                OffsetX = ShadowStart.OffsetX + (ShadowEnd.OffsetX - ShadowStart.OffsetX) * t,
                OffsetY = ShadowStart.OffsetY + (ShadowEnd.OffsetY - ShadowStart.OffsetY) * t,
                Blur = ShadowStart.Blur + (ShadowEnd.Blur - ShadowStart.Blur) * t,
                Spread = ShadowStart.Spread + (ShadowEnd.Spread - ShadowStart.Spread) * t,
                Color = LerpSharedRgb(ShadowStart.Color, ShadowEnd.Color, t),
                IsInset = t < 0.5d ? ShadowStart.IsInset : ShadowEnd.IsInset,
            }),
            BoxShadowsEquivalent),

        // 实心→实心走的是第一条分支（ISolidColorBrush 对 ISolidColorBrush）：颜色与不透明度两条通道
        // 各自饱和 —— 通道 0..255、不透明度 0..1。渐变分支在这里不参与。
        Entry(CrossAdapter("BrushSampler"), SamplerRule.Saturate,
            BrushStart, BrushEnd,
            x => x.Fill,
            t => new AvSolidColorBrush
            {
                Color = LerpSharedRgb(BrushStart.Color, BrushEnd.Color, t),
                Opacity = Math.Max(0d, Math.Min(1d, BrushStart.Opacity + (BrushEnd.Opacity - BrushStart.Opacity) * t)),
            },
            BrushEquivalent),

        // 颜色：R/G/B 共用一个 0..255 的进度；Alpha 单独按 t 走并在 0/255 饱和。
        Entry(CrossAdapter("ColorSampler"), SamplerRule.Saturate,
            ColorStart, ColorEnd,
            x => x.Tint,
            t => LerpSharedRgb(ColorStart, ColorEnd, t)),

        // 圆角：四个角各按 t 线性外推，没有任何界。构造顺序是 (左上, 右上, 右下, 左下)。
        Entry(CrossAdapter("CornerRadiusSampler"), SamplerRule.Extrapolate,
            new AvCornerRadius(10, 20, 30, 40), new AvCornerRadius(110, 220, 330, 440),
            x => x.Radius,
            t => new AvCornerRadius(
                10 + (110 - 10) * t,
                20 + (220 - 20) * t,
                30 + (330 - 30) * t,
                40 + (440 - 40) * t)),

        // 栅格长度：两端单位不同（Pixel 对 Star）→ 根本无法插值，整段保持起点。
        Entry(CrossAdapter("GridLengthSampler"), SamplerRule.Discrete,
            new AvGridLength(10, AvGridUnitType.Pixel), new AvGridLength(2, AvGridUnitType.Star),
            x => x.Column,
            _ => new AvGridLength(10, AvGridUnitType.Pixel)),

        // 像素点：两个分量各按 t 线性外推，取整是向零截断（不是四舍五入）。
        Entry(typeof(PixelPointSampler), SamplerRule.Extrapolate,
            new AvPixelPoint(10, 20), new AvPixelPoint(110, 220),
            x => x.PixelSpot,
            t => new AvPixelPoint(10 + (int)(100 * t), 20 + (int)(200 * t))),

        // 像素矩形：位置外推；宽高共用一个进度、在 0 处停住，再各取 Max(0, ...)。
        Entry(typeof(PixelRectSampler), SamplerRule.Saturate,
            new AvPixelRect(0, 0, 100, 50), new AvPixelRect(100, 200, 0, 150),
            x => x.PixelBox,
            t =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new AvPixelRect(
                    (int)(100 * t),
                    (int)(200 * t),
                    Math.Max(0, 100 + (int)(-100 * progress)),
                    Math.Max(0, 50 + (int)(100 * progress)));
            }),

        // 像素尺寸：宽高共用一个进度、在 0 处停住，再各取 Max(0, ...)。
        Entry(typeof(PixelSizeSampler), SamplerRule.Saturate,
            new AvPixelSize(100, 50), new AvPixelSize(0, 150),
            x => x.PixelExtent,
            t =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new AvPixelSize(
                    Math.Max(0, 100 + (int)(-100 * progress)),
                    Math.Max(0, 50 + (int)(100 * progress)));
            }),

        // 点：两个分量各按 t 线性外推，没有界。
        Entry(CrossAdapter("PointSampler"), SamplerRule.Extrapolate,
            new AvPoint(10, 20), new AvPoint(110, 220),
            x => x.Spot,
            t => new AvPoint(10 + t * 100, 20 + t * 200)),

        // 相对点：两端单位不同（Absolute 对 Relative）→ 保持起点。
        Entry(typeof(RelativePointSampler), SamplerRule.Discrete,
            new AvRelativePoint(10, 20, AvRelativeUnit.Absolute), new AvRelativePoint(0.5, 0.6, AvRelativeUnit.Relative),
            x => x.RelSpot,
            _ => new AvRelativePoint(10, 20, AvRelativeUnit.Absolute)),

        // 相对矩形：两端单位不同（Absolute 对 Relative）→ 保持起点。
        Entry(typeof(RelativeRectSampler), SamplerRule.Discrete,
            new AvRelativeRect(10, 20, 30, 40, AvRelativeUnit.Absolute), new AvRelativeRect(0.1, 0.2, 0.3, 0.4, AvRelativeUnit.Relative),
            x => x.RelBox,
            _ => new AvRelativeRect(10, 20, 30, 40, AvRelativeUnit.Absolute)),

        // 尺寸：宽高共用一个进度、在 0 处停住。Size 自身允许负值，采样器没有额外截断，停点完全由进度决定。
        Entry(CrossAdapter("SizeSampler"), SamplerRule.Saturate,
            new AvSize(100, 50), new AvSize(0, 150),
            x => x.Extent,
            t =>
            {
                var progress = SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
                return new AvSize(100 + -100 * progress, 50 + 100 * progress);
            }),

        // 厚度：四条边各按 t 线性外推，构造顺序是 (左, 上, 右, 下)。
        Entry(CrossAdapter("ThicknessSampler"), SamplerRule.Extrapolate,
            new AvThickness(1, 2, 3, 4), new AvThickness(11, 22, 33, 44),
            x => x.Margins,
            t => new AvThickness(1 + t * 10, 2 + t * 20, 3 + t * 30, 4 + t * 40)),

        // 变换：t=0/1 原样写回端点实例；中间与越界帧对已知的同类变换逐字段外推 —— TranslateTransform 即 X/Y。
        Entry(CrossAdapter("TransformSampler"), SamplerRule.Extrapolate,
            new AvTranslateTransform(10, 20), new AvTranslateTransform(110, 220),
            x => x.Render,
            t => new AvTranslateTransform(10 + t * 100, 20 + t * 200),
            TranslateEquivalent),
    ];

    /// <summary>TranslateTransform 是引用类型，逐位相等不成立，按 X/Y 比。</summary>
    private static bool TranslateEquivalent(object? expected, object? actual)
        => expected is AvTranslateTransform e && actual is AvTranslateTransform a && e.X == a.X && e.Y == a.Y;

    /// <summary>SolidColorBrush 是引用类型，逐位相等不成立，按颜色与不透明度比。</summary>
    private static bool BrushEquivalent(object? expected, object? actual)
        => expected is AvISolidColorBrush e
           && actual is AvISolidColorBrush a
           && e.Color.Equals(a.Color)
           && e.Opacity == a.Opacity;

    /// <summary>BoxShadows 内部是数组，结构体默认比较会退化成引用比较，逐影子比字段。</summary>
    private static bool BoxShadowsEquivalent(object? expected, object? actual)
    {
        if (expected is not AvBoxShadows e || actual is not AvBoxShadows a || e.Count != a.Count)
        {
            return false;
        }

        for (var i = 0; i < e.Count; i++)
        {
            var left = e[i];
            var right = a[i];
            if (left.OffsetX != right.OffsetX
                || left.OffsetY != right.OffsetY
                || left.Blur != right.Blur
                || left.Spread != right.Spread
                || !left.Color.Equals(right.Color)
                || left.IsInset != right.IsInset)
            {
                return false;
            }
        }

        return true;
    }
}
