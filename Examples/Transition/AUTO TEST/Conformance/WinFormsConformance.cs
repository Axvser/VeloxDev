namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the WinForms adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// One sampler, and it is the smallest table here: WinForms has no brush interpolation, so the adapter registers
/// nothing but <c>PaddingSampler</c>.
/// </remarks>
internal static class WinFormsConformance
{
    internal const string Platform = "WinForms";

    // 索引器那两条的端点：同一个集合的两个槽，两对颜色必须不同，否则两条路径的闭式解无从区分。
    private static readonly (int A, int R, int G, int B) Ramp0Start = (200, 200, 100, 50);
    private static readonly (int A, int R, int G, int B) Ramp0End = (250, 240, 180, 120);
    private static readonly (int A, int R, int G, int B) Ramp1Start = (120, 30, 200, 250);
    private static readonly (int A, int R, int G, int B) Ramp1End = (200, 210, 40, 10);

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // Padding：四条边各自独立地随缓动时间线性外推，四个算式之间没有任何耦合 —— 没有共用进度、没有在 0 处
        // 停住、也没有别的上下限。分量顺序 L,T,R,B。
        // 取整是 (int) 的向零截断，不是四舍五入；Padding 只是四个 int，接受负值，所以越过 [0,1] 时照走不误。
        new("PaddingSampler", "Padding", t =>
        [
            20d + Math.Truncate(t * 80d),
            20d + Math.Truncate(t * -15d),
            60d + Math.Truncate(t * -60d),
            60d + Math.Truncate(t * -60d),
        ]),

        // 索引器路径的两条。它们验的不是采样器 —— 两条都用 SizeSampler、闭式解与任何一条尺寸路径一模一样 ——
        // 而是路径落到了哪个槽上：Controls 的第 0 与第 1 个孩子。
        //
        // 这两条必须同时在批量的帧报告里出现。索引器的 PropertyInfo 对每个下标都是同一个 "Item"，
        // 下标不并入路径身份的话两条会合成一个状态条目，后声明的那条只会静默盖掉前一条 —— 而缺席是
        // 批量那一路直接就报的。
        //
        // 分量序 A, R, G, B；颜色按颜色的规则走（R/G/B 共用一个 0..255 的进度，Alpha 自成一界）。
        new("GradientStop0Color", "Color", t => ClosedForm.ColorAt(t, Ramp0Start, Ramp0End)),

        new("GradientStop1Color", "Color", t => ClosedForm.ColorAt(t, Ramp1Start, Ramp1End)),
    ];
}
