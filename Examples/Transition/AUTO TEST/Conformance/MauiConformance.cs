namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the MAUI adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// The endpoints are the ones the demo's <c>SamplerProbe</c> drives, transcribed from the same source the pure-data
/// suite uses (<c>Samplers/MauiEntries.cs</c>), so a failure here and a failure there name the same numbers. This
/// table covers all twelve: the nine whose produced value is a value type are also checked there, and the three whose
/// product is a <c>BindableObject</c> — the brush, the shadow and the transform — can only be driven in a live app,
/// which is exactly what this suite is.
/// <para>
/// MAUI's colour channels are floats in <c>[0,1]</c>, not 8-bit bytes, so <see cref="ClosedForm.ColorAt"/> does not
/// describe them: the rule is restated here with a <c>[0,1]</c> bound and plain saturation (no byte truncation), and
/// the component order is the same <c>A,R,G,B</c> the other platforms use.
/// </para>
/// <para>
/// Two component orders are deliberately MAUI's own rather than WPF's, and the demo serialises them the same way:
/// <c>CornerRadius</c> is <c>(TopLeft, TopRight, BottomLeft, BottomRight)</c> — MAUI's constructor and property order,
/// where WPF's is <c>(…, BottomRight, BottomLeft)</c> — and <c>Transform</c> is the six matrix components, because the
/// sampler's scratch value is the base <c>Transform</c>, whose only readable value is its <c>Matrix</c>.
/// </para>
/// </remarks>
internal static class MauiConformance
{
    internal const string Platform = "MAUI";

    // 端点色：与 WPF 那一侧同一组 ARGB 值，只是按 MAUI 的 0..1 通道写。分量序一律 (A,R,G,B)。
    private static readonly (double A, double R, double G, double B) BrushStart = (200d / 255d, 200d / 255d, 100d / 255d, 50d / 255d);
    private static readonly (double A, double R, double G, double B) BrushEnd = (250d / 255d, 240d / 255d, 180d / 255d, 120d / 255d);
    private static readonly (double A, double R, double G, double B) TintStart = (120d / 255d, 30d / 255d, 200d / 255d, 250d / 255d);
    private static readonly (double A, double R, double G, double B) TintEnd = (200d / 255d, 210d / 255d, 40d / 255d, 10d / 255d);

    // 阴影的两支端点刷就是画刷那一对，颜色按 t 析取而不是插值。
    private static readonly (double A, double R, double G, double B) ShadowStart = BrushStart;
    private static readonly (double A, double R, double G, double B) ShadowEnd = BrushEnd;

    /// <summary>尺寸类的端点：宽 100→0、高 50→150，一涨一缩，正好逼出"共用进度、在 0 处停"这条规则。</summary>
    private const double WidthStart = 100d;
    private const double WidthEnd = 0d;
    private const double HeightStart = 50d;
    private const double HeightEnd = 150d;

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 画刷：只走实心↔实心那条路 —— 端点就是一对 SolidColorBrush。采样器每帧交出的都是自己那块草稿刷，
        // 只有颜色被换掉；分量为 A,R,G,B（MAUI 的 Brush 没有自己的 Opacity，不像 WPF 的 SolidColorBrush）。
        new("BrushSampler", "SolidColorBrush", t => ColorAt(t, BrushStart, BrushEnd)),

        new("ColorSampler", "Color", t => ColorAt(t, TintStart, TintEnd)),

        // 圆角：四个分量各自线性外推，没有任何界。分量序是本类型自己的序 —— MAUI 的构造函数与属性都是
        // (TopLeft, TopRight, BottomLeft, BottomRight)，与 WPF 的 (…, BottomRight, BottomLeft) 不同。
        new("CornerRadiusSampler", "CornerRadius", t =>
        [
            ClosedForm.Lerp(1d, 11d, t),   // TopLeft
            ClosedForm.Lerp(2d, 22d, t),   // TopRight
            ClosedForm.Lerp(3d, 33d, t),   // BottomLeft
            ClosedForm.Lerp(4d, 44d, t),   // BottomRight
        ]),

        new("PointSampler", "Point", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 单精度点：适配器的算式在 float 里做，闭式解在 double 里做 —— 这组端点与时间格点上的值都二进制可精确
        // 表示，两侧落在同一个数上。
        new("PointFSampler", "PointF", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— 负尺寸不是矩形。
        new("RectSampler", "Rect", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
            return
            [
                ClosedForm.Lerp(0d, 100d, t),
                ClosedForm.Lerp(0d, 200d, t),
                ClosedForm.Lerp(WidthStart, WidthEnd, size),
                ClosedForm.Lerp(HeightStart, HeightEnd, size),
            ];
        }),

        // 单精度矩形：注意它操作的是 System.Drawing.RectangleF，不是 MAUI 自己的 RectF；运算在 float 里。
        new("RectFSampler", "RectangleF", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
            return
            [
                ClosedForm.Lerp(0d, 100d, t),
                ClosedForm.Lerp(0d, 200d, t),
                ClosedForm.Lerp(WidthStart, WidthEnd, size),
                ClosedForm.Lerp(HeightStart, HeightEnd, size),
            ];
        }),

        // 阴影：颜色是析取的 —— t >= 0.5 取终点那支刷子，否则取起点那支，整支刷子原样交出、不插色；偏移外推；
        // 不透明度自成一界、在 [0,1] 饱和；半径与其它尺寸同理，共用一个进度并在 0 处停住（负半径不是阴影）。
        new("ShadowSampler", "Shadow", t =>
        {
            var color = t >= 0.5 ? ShadowEnd : ShadowStart;
            var radius = ClosedForm.SharedProgress(t, double.PositiveInfinity, (10d, 60d));
            return
            [
                color.A, color.R, color.G, color.B,
                ClosedForm.Lerp(10d, 110d, t),
                ClosedForm.Lerp(20d, 220d, t),
                ClosedForm.Clamp01(ClosedForm.Lerp(0.2d, 0.8d, t)),
                ClosedForm.Lerp(10d, 60d, radius),
            ];
        }),

        // 尺寸：宽高共用一个进度，在 0 处停止。
        new("SizeSampler", "Size", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
            return
            [
                ClosedForm.Lerp(WidthStart, WidthEnd, size),
                ClosedForm.Lerp(HeightStart, HeightEnd, size),
            ];
        }),

        // 单精度尺寸：同上，运算在 float 里。
        new("SizeFSampler", "SizeF", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (WidthStart, WidthEnd), (HeightStart, HeightEnd));
            return
            [
                ClosedForm.Lerp(WidthStart, WidthEnd, size),
                ClosedForm.Lerp(HeightStart, HeightEnd, size),
            ];
        }),

        new("ThicknessSampler", "Thickness", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(40d, 440d, t),
        ]),

        // 变换：端点原样交出调用方给的实例（t==0 / t==1），中间帧交出基类草稿 —— 两边读的都是同一份矩阵，
        // 六个分量各自线性外推。端点矩阵是单位阵加偏移，所以偏移那两项读起来就是 WPF 那一侧的 (X, Y)。
        new("TransformSampler", "Transform", t =>
        [
            ClosedForm.Lerp(1d, 1d, t),    // M11
            ClosedForm.Lerp(0d, 0d, t),    // M12
            ClosedForm.Lerp(0d, 0d, t),    // M21
            ClosedForm.Lerp(1d, 1d, t),    // M22
            ClosedForm.Lerp(10d, 110d, t), // OffsetX
            ClosedForm.Lerp(20d, 220d, t), // OffsetY
        ]),
    ];

    /// <summary>
    /// A MAUI colour: its channels are floats in <c>[0,1]</c>, so the bound is 1 rather than 255 and an out-of-gamut
    /// channel saturates instead of being truncated to a byte. R/G/B share one progress so an overshoot cannot shift
    /// the hue; alpha keeps the full eased time on its own. Components come out <c>A,R,G,B</c>.
    /// </summary>
    /// <remarks>
    /// This is the same rule <see cref="ClosedForm.ColorAt"/> states, restated for the float-based type rather than
    /// reused — the operation the sampler performs is a saturation at 1, not a truncation at 255.
    /// </remarks>
    private static double[] ColorAt(double t, (double A, double R, double G, double B) from, (double A, double R, double G, double B) to)
    {
        var progress = ClosedForm.SharedProgress(t, 1d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        return
        [
            ClosedForm.Clamp01(ClosedForm.Lerp(from.A, to.A, t)),
            ClosedForm.Clamp01(ClosedForm.Lerp(from.R, to.R, progress)),
            ClosedForm.Clamp01(ClosedForm.Lerp(from.G, to.G, progress)),
            ClosedForm.Clamp01(ClosedForm.Lerp(from.B, to.B, progress)),
        ];
    }
}
