namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the Avalonia adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// The endpoints are the ones the demo's <c>SamplerProbe</c> drives, transcribed from the same source the pure-data
/// suite uses, so a failure here and a failure there name the same numbers. Component order per entry is the order
/// that probe serialises, stated on every entry because nothing but a reader can keep the two in step.
/// </remarks>
internal static class AvaloniaConformance
{
    internal const string Platform = "Avalonia";

    // 三个条目共用同一对颜色端点：阴影、笔刷、颜色。与 Samplers/AvaloniaEntries.cs 里的 ColorStart/ColorEnd 一致。
    private static readonly (int A, int R, int G, int B) ColorStart = (200, 200, 100, 50);
    private static readonly (int A, int R, int G, int B) ColorEnd = (250, 240, 180, 120);

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 单个影子：颜色按颜色的规则走（R/G/B 共用一个 0..255 的进度，Alpha 自成一界）；
        // OffsetX / OffsetY / Blur / Spread 各自按 t 线性外推；IsInset 在 t=0.5 处切换，取 0/1。
        // 分量序：A, R, G, B, OffsetX, OffsetY, Blur, Spread, IsInset。
        new("BoxShadowsSampler", "BoxShadows", t =>
        [
            .. ClosedForm.ColorAt(t, ColorStart, ColorEnd),
            ClosedForm.Lerp(2d, 12d, t),
            ClosedForm.Lerp(4d, 24d, t),
            ClosedForm.Lerp(8d, 18d, t),
            ClosedForm.Lerp(1d, 11d, t),
            t < 0.5d ? 0d : 1d,
        ]),

        // 画刷：只走实心↔实心那条路 —— 端点就是一对 SolidColorBrush，这是"给一对起点/终点"能驱动的路。
        // 颜色按颜色的规则走，不透明度自成一界并在 [0,1] 饱和。渐变分支在这里不参与。
        // 分量序：A, R, G, B, Opacity。
        new("BrushSampler", "SolidColorBrush", t =>
        [
            .. ClosedForm.ColorAt(t, ColorStart, ColorEnd),
            ClosedForm.Clamp01(ClosedForm.Lerp(0.25, 0.75, t)),
        ]),

        // 颜色：R/G/B 共用一个 0..255 的进度；Alpha 单独按 t 走并在 0/255 饱和。
        // 分量序：A, R, G, B。
        new("ColorSampler", "Color", t => ClosedForm.ColorAt(t, ColorStart, ColorEnd)),

        // 圆角：四个角各自线性外推，没有任何界。分量序是 Avalonia 的构造序 (TopLeft, TopRight, BottomRight, BottomLeft)。
        new("CornerRadiusSampler", "CornerRadius", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
            ClosedForm.Lerp(30d, 330d, t),
            ClosedForm.Lerp(40d, 440d, t),
        ]),

        // 栅格长度：两端单位不同（Pixel 对 Star）→ 根本无法插值，整段保持起点。
        // 分量序：Value, GridUnitType。GridUnitType.Pixel == 1（枚举序 Auto, Pixel, Star）。
        new("GridLengthSampler", "GridLength", _ => [10d, 1d]),

        // 像素点：两个分量各按 t 线性外推，取整是向零截断（不是四舍五入）。分量序：X, Y。
        new("PixelPointSampler", "PixelPoint", t =>
        [
            Math.Truncate(ClosedForm.Lerp(10d, 110d, t)),
            Math.Truncate(ClosedForm.Lerp(20d, 220d, t)),
        ]),

        // 像素矩形：位置外推并同样向零截断；宽高共用一个进度、在 0 处停住，再各取 Max(0, ...)。
        // 分量序：X, Y, Width, Height。
        new("PixelRectSampler", "PixelRect", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                Math.Truncate(ClosedForm.Lerp(0d, 100d, t)),
                Math.Truncate(ClosedForm.Lerp(0d, 200d, t)),
                Math.Max(0d, 100d + Math.Truncate(ClosedForm.Lerp(0d, -100d, size))),
                Math.Max(0d, 50d + Math.Truncate(ClosedForm.Lerp(0d, 100d, size))),
            ];
        }),

        // 像素尺寸：宽高共用一个进度、在 0 处停住，再各取 Max(0, ...)。分量序：Width, Height。
        new("PixelSizeSampler", "PixelSize", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                Math.Max(0d, 100d + Math.Truncate(ClosedForm.Lerp(0d, -100d, size))),
                Math.Max(0d, 50d + Math.Truncate(ClosedForm.Lerp(0d, 100d, size))),
            ];
        }),

        // 点：两个分量各按 t 线性外推，没有界。分量序：X, Y。
        new("PointSampler", "Point", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),

        // 相对点：两端单位不同（Absolute 对 Relative）→ 保持起点。
        // 分量序：X, Y, RelativeUnit。RelativeUnit.Absolute == 1（Avalonia 的枚举序是 Relative, Absolute）。
        new("RelativePointSampler", "RelativePoint", _ => [10d, 20d, 1d]),

        // 相对矩形：两端单位不同（Absolute 对 Relative）→ 保持起点。
        // 分量序：X, Y, Width, Height, RelativeUnit。
        new("RelativeRectSampler", "RelativeRect", _ => [10d, 20d, 30d, 40d, 1d]),

        // 尺寸：宽高共用一个进度、在 0 处停住。Size 自身允许负值，采样器没有额外截断，停点完全由进度决定。
        // 分量序：Width, Height。
        new("SizeSampler", "Size", t =>
        {
            var size = ClosedForm.SharedProgress(t, double.PositiveInfinity, (100d, 0d), (50d, 150d));
            return
            [
                ClosedForm.Lerp(100d, 0d, size),
                ClosedForm.Lerp(50d, 150d, size),
            ];
        }),

        // 厚度：四条边各按 t 线性外推。分量序：Left, Top, Right, Bottom。
        new("ThicknessSampler", "Thickness", t =>
        [
            ClosedForm.Lerp(1d, 11d, t),
            ClosedForm.Lerp(2d, 22d, t),
            ClosedForm.Lerp(3d, 33d, t),
            ClosedForm.Lerp(4d, 44d, t),
        ]),

        // 变换：端点原样交出调用方给的实例（t==0 / t==1），其余帧对已知的同类变换逐字段外推 —— 值仍是同一份
        // 线性外推。分量序：X, Y（TranslateTransform）。
        new("TransformSampler", "TranslateTransform", t =>
        [
            ClosedForm.Lerp(10d, 110d, t),
            ClosedForm.Lerp(20d, 220d, t),
        ]),
    ];
}
