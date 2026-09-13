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

    // 索引器那条占的第二个槽：另一对端点，否则两条路径的闭式解无从区分。
    private const string Slot1StartHex = "#3C14C8FA";
    private const string Slot1EndHex = "#D2D2280A";

    internal static IReadOnlyList<ConformanceEntry> All { get; } =
    [
        // 字符串：t == 0 与 t == 1 原样写回调用方给的字符串 —— 字符串是有损的，端点不可能被重新格式化成
        // "值相等"的另一种写法，所以这里直接返回两个端点常量。其余 t 才走颜色插值。
        new("StringSampler", "String", t => ClosedForm.CodePoints(
            CssAt(t, StartHex, EndHex, (200d, 100d, 0d), (240d, 180d, 50d), 128d, 64d))),

        // 索引器路径的两条。它们验的不是采样器 —— 两条都用 StringSampler、闭式解与 StringSampler 一模一样 ——
        // 而是路径落到了哪个槽上：Slots 的第 0 与第 1 个元素。
        //
        // 这两条必须同时在批量的帧报告里出现。索引器的 PropertyInfo 对每个下标都是同一个 "Item"，
        // 下标不并入路径身份的话两条会合成一个状态条目，后声明的那条只会静默盖掉前一条 —— 而缺席是
        // 批量那一路直接就报的。
        new("GradientStop0Color", "String", t => ClosedForm.CodePoints(
            CssAt(t, StartHex, EndHex, (200d, 100d, 0d), (240d, 180d, 50d), 128d, 64d))),

        new("GradientStop1Color", "String", t => ClosedForm.CodePoints(
            CssAt(t, Slot1StartHex, Slot1EndHex, (60d, 20d, 200d), (210d, 210d, 40d), 250d, 10d))),

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
    /// <param name="startHex">起始端点原样写回的那串十六进制。</param>
    /// <param name="endHex">结束端点原样写回的那串十六进制。</param>
    /// <param name="start">起始端点的 R/G/B（0..255）。</param>
    /// <param name="end">结束端点的 R/G/B（0..255）。</param>
    /// <param name="startAlpha">起始端点的 alpha（0..255）。</param>
    /// <param name="endAlpha">结束端点的 alpha（0..255）。</param>
    private static string CssAt(
        double t,
        string startHex,
        string endHex,
        (double R, double G, double B) start,
        (double R, double G, double B) end,
        double startAlpha,
        double endAlpha)
    {
        if (t == 0d) return startHex;
        if (t == 1d) return endHex;

        var progress = ClosedForm.SharedProgress(t, 255d, (start.R, end.R), (start.G, end.G), (start.B, end.B));

        // alpha 自成一界：它不跟 R/G/B 共用那条有界进度，而是自己钳在 [0,1] —— 端点跨度大时它会先出界，
        // 少了这一钳闭式解与实际写出的文本就对不上（实测过）。
        var alpha = Math.Clamp((startAlpha + (endAlpha - startAlpha) * t) / 255d, 0d, 1d);

        return string.Create(CultureInfo.InvariantCulture,
            $"rgba({Rounded(start.R + (end.R - start.R) * progress)}, {Rounded(start.G + (end.G - start.G) * progress)}, {Rounded(start.B + (end.B - start.B) * progress)}, {alpha:0.###})");
    }

    /// <summary>四舍五入再饱和到 0..255 —— 直接转 byte 会把 300 折成 44。</summary>
    private static double Rounded(double value)
        => value <= 0d ? 0d : value >= 255d ? 255d : Math.Round(value);
}
