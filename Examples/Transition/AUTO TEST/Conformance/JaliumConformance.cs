namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the Jalium adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// The endpoints are the ones the demo's <c>SamplerProbe</c> drives, transcribed from the same source the pure-data
/// suite uses, so a failure here and a failure there name the same numbers. The adapter registers
/// <c>BrushSampler</c> twice — once for <c>Brush</c> and once for <c>SolidColorBrush</c> — but it is one sampler, so
/// it is one entry here and one probe there.
/// </remarks>
internal static class JaliumConformance
{
    internal const string Platform = "Jalium";

    private static readonly (int A, int R, int G, int B) RgbStart = (200, 200, 100, 50);
    private static readonly (int A, int R, int G, int B) RgbEnd = (250, 240, 180, 120);

    // 两个实心刷的不透明度：0.25 → 1，两个方向都能验到 [0,1] 的钳制。
    private const double BrushStartOpacity = 0.25d;
    private const double BrushEndOpacity = 1d;

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 画刷：只走实心↔实心那条路 —— 端点就是一对 SolidColorBrush，这是"给一对起点/终点"能驱动的路。
        // 分量为 A,R,G,B,Opacity：颜色按颜色的规则走，不透明度自成一界并在 [0,1] 饱和。
        new("BrushSampler", "SolidColorBrush", t =>
        [
            .. ClosedForm.ColorAt(t, RgbStart, RgbEnd),
            ClosedForm.Clamp01(ClosedForm.Lerp(BrushStartOpacity, BrushEndOpacity, t)),
        ]),

        new("ColorSampler", "Color", t => ClosedForm.ColorAt(t, RgbStart, RgbEnd)),

        // 圆角：四个分量各自线性外推，没有任何界。顺序是构造函数序 (TopLeft, TopRight, BottomRight, BottomLeft)。
        new("CornerRadiusSampler", "CornerRadius", t =>
        [
            ClosedForm.Lerp(1d, 11d, t),
            ClosedForm.Lerp(2d, 22d, t),
            ClosedForm.Lerp(3d, 33d, t),
            ClosedForm.Lerp(4d, 44d, t),
        ]),

        // 点：两个分量各自线性外推，没有任何界。
        new("PointSampler", "Point", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— 负尺寸 Jalium 的 Rect 构造器直接抛。
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

        // 尺寸：宽高共用一个进度，在 0 处停止 —— 负尺寸 Jalium 的 Size 构造器直接抛。
        new("SizeSampler", "Size", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                ClosedForm.Lerp(100d, 0d, size),
                ClosedForm.Lerp(50d, 150d, size),
            ];
        }),

        // 厚度：四条边各自线性外推，没有任何界。分量顺序 L,T,R,B。
        new("ThicknessSampler", "Thickness", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(40d, 440d, t),
        ]),

        // 3D 变换：走的是同轴快速路 —— 角度与旋转中心逐分量外推，轴始终是起点那根（守卫要求两端同轴，
        // 所以轴是所有帧共有的常量，排在分量最前）。端点之外没有任何界。
        new("Transform3DSampler", "RotateTransform3D", t =>
        [
            0d,
            0d,
            1d,
            ClosedForm.Lerp(30d, 150d, t),
            ClosedForm.Lerp(1d, 5d, t),
            ClosedForm.Lerp(2d, 6d, t),
            ClosedForm.Lerp(3d, 7d, t),
        ]),

        // 变换：端点原样交出调用方给的实例（t==0 / t==1），中间走同类型快速路 —— 值仍是同一份线性外推。
        new("TransformSampler", "TranslateTransform", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),
    ];
}
