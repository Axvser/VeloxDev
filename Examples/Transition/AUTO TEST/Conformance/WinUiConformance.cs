namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the WinUI adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// The endpoints are the ones the demo's <c>SamplerProbe</c> drives. Ten samplers, and three of them — brush, projection
/// and transform — are the reason this layer exists at all: their produced value is a WinRT DependencyObject, which a
/// pure-data process cannot even construct, so the only place they can be driven is an app that already has a XAML
/// runtime.
/// <para>
/// Component orders are the demo's, item for item: the two files state the same shape or the check is meaningless.
/// </para>
/// </remarks>
internal static class WinUiConformance
{
    internal const string Platform = "WinUI";

    private static readonly (int A, int R, int G, int B) BrushStart = (200, 200, 100, 50);
    private static readonly (int A, int R, int G, int B) BrushEnd = (250, 240, 180, 120);
    private static readonly (int A, int R, int G, int B) TintStart = (120, 30, 200, 250);
    private static readonly (int A, int R, int G, int B) TintEnd = (200, 210, 40, 10);

    /// <summary>
    /// A brush colour blended the way WinUI's brush sampler blends it: in premultiplied alpha space, divided back by the
    /// interpolated alpha, each channel then saturated to a byte.
    /// </summary>
    /// <remarks>
    /// This is what makes the brush sampler different from the colour sampler, and it only shows when the two endpoint
    /// alphas differ — with equal alphas the two rules coincide. R/G/B share one <c>[0,255]</c> progress taken from the
    /// colours as seen (a premultiplied channel carries alpha inside it, so a hue cannot be bounded there); alpha keeps
    /// the full eased time. Not <see cref="ClosedForm.ColorAt"/>: that one is the un-premultiplied rule.
    /// </remarks>
    private static double[] BrushColorAt(double t, (int A, int R, int G, int B) from, (int A, int R, int G, int B) to)
    {
        var progress = ClosedForm.SharedProgress(t, 255d, (from.R, to.R), (from.G, to.G), (from.B, to.B));

        var alphaFrom = from.A / 255d;
        var alphaTo = to.A / 255d;

        var red = from.R * alphaFrom * (1d - progress) + to.R * alphaTo * progress;
        var green = from.G * alphaFrom * (1d - progress) + to.G * alphaTo * progress;
        var blue = from.B * alphaFrom * (1d - progress) + to.B * alphaTo * progress;
        var alpha = alphaFrom * (1d - t) + alphaTo * t;

        if (alpha > 0d)
        {
            red /= alpha;
            green /= alpha;
            blue /= alpha;
        }

        return
        [
            ClosedForm.Channel(alpha * 255d),
            ClosedForm.Channel(red),
            ClosedForm.Channel(green),
            ClosedForm.Channel(blue),
        ];
    }

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 画刷：只走实心↔实心那条路。分量为 A,R,G,B,Opacity —— 颜色走预乘 alpha 的混合，不透明度自成一界
        // 并在 [0,1] 饱和。两端 alpha 取 200 / 250 而不是相等：预乘与不预乘在 alpha 相同时结果完全一样，
        // 那样这条闭式解就没验到它自己的规则。
        new("BrushSampler", "SolidColorBrush", t =>
        [
            .. BrushColorAt(t, BrushStart, BrushEnd),
            ClosedForm.Clamp01(ClosedForm.Lerp(0.2, 0.8, t)),
        ]),

        // 颜色：R/G/B 共用一个 [0,255] 的进度、在边界停住；Alpha 自成一界，按 t 直走并在 0/255 饱和。
        new("ColorSampler", "Color", t => ClosedForm.ColorAt(t, TintStart, TintEnd)),

        // 圆角：四个分量各自外推、各自在 0 处钳住 —— 不共用进度，因为四个角本就互不相干。顺序是构造函数序
        // (TopLeft, TopRight, BottomRight, BottomLeft)。钳制不是装饰：WinUI 的 CornerRadius 只收非负值，
        // 端点取 (1,2,3,4) → (11,22,33,44)，t = -0.5 上四个分量全被打到负数，正是钳制那一支。
        new("CornerRadiusSampler", "CornerRadius", t =>
        [
            Math.Max(0d, ClosedForm.Lerp(1d, 11d, t)),
            Math.Max(0d, ClosedForm.Lerp(2d, 22d, t)),
            Math.Max(0d, ClosedForm.Lerp(3d, 33d, t)),
            Math.Max(0d, ClosedForm.Lerp(4d, 44d, t)),
        ]),

        // 栅格长度：两端同为 Pixel，走插值那条路；长度在 0 处钳住（只收非负值）。分量是 Value 加一个单位码
        // （枚举底层值：Auto = 0、Pixel = 1、Star = 2）—— 只看数值的话，单位被换掉是看不见的。
        // 两端单位不同那条路（t < 1 返回起点、t >= 1 直接切到终点）这里不覆盖：一条采样器在表里只留一个条目，
        // 而同一端点对上，两条路互斥。
        new("GridLengthSampler", "GridLength", t =>
        [
            Math.Max(0d, ClosedForm.Lerp(10d, 110d, t)),
            1d,
        ]),

        // 点：两个分量各自线性外推，没有界。
        new("PointSampler", "Point", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— 负尺寸不可表示。
        new("RectSampler", "Rect", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                ClosedForm.Lerp(0d, 100d, t),
                ClosedForm.Lerp(0d, 200d, t),
                ClosedForm.Lerp(100d, 0d, size),
                ClosedForm.Lerp(50d, 150d, size),
            ];
        }),

        // 尺寸：宽高共用一个进度，在 0 处停止。
        new("SizeSampler", "Size", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                ClosedForm.Lerp(100d, 0d, size),
                ClosedForm.Lerp(50d, 150d, size),
            ];
        }),

        // 厚度：四个分量各自外推，没有任何界。
        new("ThicknessSampler", "Thickness", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(40d, 440d, t),
        ]),

        // 投影：options 传 null，方向即 Auto —— 三个旋转角不走方向性插值，与另外六个字段一样是普通线性外推。
        // 顺序 RotationX/Y/Z、CenterOfRotationX/Y/Z、GlobalOffsetX/Y/Z。
        new("ProjectionSampler", "PlaneProjection", t =>
        [
            ClosedForm.Lerp(10d, 130d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(0.4, 0.8, t),
            ClosedForm.Lerp(0.4, 0.8, t),
            ClosedForm.Lerp(0d, 0.4, t),
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
        ]),

        // 变换：端点原样交出调用方给的实例（t == 0 / t == 1），中间走同类型快速路 —— 值仍是同一份线性外推。
        new("TransformSampler", "TranslateTransform", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),
    ];
}
