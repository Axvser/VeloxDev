namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the WPF adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// The endpoints are the ones the demo's <c>SamplerProbe</c> drives, transcribed from the same source the pure-data
/// suite uses, so a failure here and a failure there name the same numbers.
/// </remarks>
internal static class WpfConformance
{
    internal const string Platform = "WPF";

    private static readonly (int A, int R, int G, int B) BrushStart = (200, 200, 100, 50);
    private static readonly (int A, int R, int G, int B) BrushEnd = (250, 240, 180, 120);
    private static readonly (int A, int R, int G, int B) TintStart = (120, 30, 200, 250);
    private static readonly (int A, int R, int G, int B) TintEnd = (200, 210, 40, 10);

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 画刷：只走实心↔实心那条路 —— 端点就是一对 SolidColorBrush，这是"给一对起点/终点"能驱动的路。
        // 分量为 A,R,G,B,Opacity：颜色按颜色的规则走，不透明度自成一界并在 [0,1] 饱和。
        new("BrushSampler", "SolidColorBrush", t =>
        [
            .. ClosedForm.ColorAt(t, BrushStart, BrushEnd),
            ClosedForm.Clamp01(ClosedForm.Lerp(0.2, 0.8, t)),
        ]),

        new("ColorSampler", "Color", t => ClosedForm.ColorAt(t, TintStart, TintEnd)),

        // 圆角：四个分量各自线性外推，没有任何界。顺序是构造函数序 (TopLeft, TopRight, BottomRight, BottomLeft)。
        new("CornerRadiusSampler", "CornerRadius", t =>
        [
            ClosedForm.Lerp(1d, 11d, t),
            ClosedForm.Lerp(2d, 22d, t),
            ClosedForm.Lerp(3d, 33d, t),
            ClosedForm.Lerp(4d, 44d, t),
        ]),

        // 阴影：颜色同颜色的规则；Direction / ShadowDepth / BlurRadius 外推；Opacity 在 0..1 停止。
        new("DropShadowEffectSampler", "DropShadowEffect", t =>
        [
            .. ClosedForm.ColorAt(t, BrushStart, BrushEnd),
            ClosedForm.Lerp(45d, 135d, t),
            ClosedForm.Lerp(10d, 20d, t),
            ClosedForm.Clamp01(ClosedForm.Lerp(0.2, 0.8, t)),
            ClosedForm.Lerp(10d, 60d, t),
        ]),

        new("Point3DSampler", "Point3D", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
        ]),

        new("PointSampler", "Point", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 矩形：位置外推；宽高共用一个进度、在 0 处停住 —— 负尺寸 WPF 直接抛。
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

        // 尺寸：宽高共用一个进度，在 0 处停止 —— 负尺寸 WPF 直接抛。
        new("SizeSampler", "Size", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                ClosedForm.Lerp(100d, 0d, size),
                ClosedForm.Lerp(50d, 150d, size),
            ];
        }),

        new("ThicknessSampler", "Thickness", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(40d, 440d, t),
        ]),

        // 变换：端点原样交出调用方给的实例（t==0 / t==1），中间走同类型快速路 —— 值仍是同一份线性外推。
        new("TransformSampler", "TranslateTransform", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        new("Vector3DSampler", "Vector3D", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
        ]),

        new("VectorSampler", "Vector", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),
    ];
}
