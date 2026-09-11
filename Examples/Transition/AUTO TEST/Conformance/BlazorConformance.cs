using System.Globalization;

namespace VeloxDev.AT.Conformance;

/// <summary>
/// The samplers the Razor adapter ships, with the closed form each one has to satisfy.
/// </summary>
/// <remarks>
/// One sampler, and its product is a string rather than a value: the adapter registers only <c>StringSampler</c>, which
/// interpolates a CSS colour. The expectation is therefore written as the CSS text the sampler must produce, and
/// compared as character code points.
/// </remarks>
internal static class BlazorConformance
{
    internal const string Platform = "Blazor";

    // 端点，8 位十六进制 #RRGGBBAA：起始 R=200 G=100 B=0 A=128，结束 R=240 G=180 B=50 A=64。
    private const string StartHex = "#C8640080";
    private const string EndHex = "#F0B43240";

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 字符串：t == 0 与 t == 1 原样写回调用方给的字符串 —— 字符串是有损的，端点不可能被重新格式化成
        // "值相等"的另一种写法，所以这里直接返回两个端点常量。其余 t 才走颜色插值。
        new("StringSampler", "String", t => ClosedForm.CodePoints(CssAt(t))),
    ];

    /// <summary>
    /// StringSampler 颜色路径的闭式解：两端先解析成 CSS 颜色，中间帧再逐通道插值并重新格式化成
    /// <c>rgba(r, g, b, a)</c>。
    /// </summary>
    /// <remarks>
    /// R/G/B 共用一个 [0,255] 有界进度：起始 B=0 让 t &lt; 0 时整组被 B 先拽回 0，而 t &gt; 1 时由 R 先顶到
    /// 255 停住；alpha 自成一界、不做组内钳制。随后每个通道先四舍五入再饱和到 0..255，alpha 除以 255 后以
    /// <c>0.###</c> 写成 0..1 —— 小数位数是这条闭式解的一部分，不能省。
    /// </remarks>
    private static string CssAt(double t)
    {
        if (t == 0d) return StartHex;
        if (t == 1d) return EndHex;

        var progress = ClosedForm.SharedProgress(t, 255d, (200d, 240d), (100d, 180d), (0d, 50d));
        var alpha = (128d + (64d - 128d) * t) / 255d;

        return string.Create(CultureInfo.InvariantCulture,
            $"rgba({Rounded(200d + 40d * progress)}, {Rounded(100d + 80d * progress)}, {Rounded(0d + 50d * progress)}, {alpha:0.###})");
    }

    /// <summary>四舍五入再饱和到 0..255 —— 直接转 byte 会把 300 折成 44。</summary>
    private static double Rounded(double value)
        => value <= 0d ? 0d : value >= 255d ? 255d : Math.Round(value);
}
