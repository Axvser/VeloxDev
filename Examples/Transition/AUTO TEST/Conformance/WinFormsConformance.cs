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
    ];
}
