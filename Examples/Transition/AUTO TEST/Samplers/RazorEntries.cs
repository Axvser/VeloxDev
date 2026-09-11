using System.Globalization;
using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.SamplerTest;

/// <summary>The sampler the Razor adapter ships, with the closed form it follows.</summary>
internal static class RazorEntries
{
    /// <summary>一条 string 属性的目标，让条目有真实的写入路径。</summary>
    private sealed class Target
    {
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>起始端，8 位十六进制 <c>#RRGGBBAA</c>：R=200、G=100、B=0、A=128。</summary>
    private const string StartHex = "#C8640080";

    /// <summary>结束端：R=240、G=180、B=50、A=64。</summary>
    private const string EndHex = "#F0B43240";

    /// <summary>
    /// 一组颜色通道共用一个进度：谁先出界就停在谁那里，后面的通道不能把它再拉回来 ——
    /// 与库里的 <c>BoundedProgress</c> 同一条规则，但这里是独立重述的。
    /// </summary>
    /// <param name="maximum">该组的上界；颜色通道是 255，下界固定为 0。</param>
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

    /// <summary>先四舍五入再饱和到 0..255 —— 直接转 byte 会把 300 折成 44。</summary>
    private static byte Channel(double value)
    {
        if (value <= 0d) return 0;
        if (value >= 255d) return 255;
        return (byte)Math.Round(value);
    }

    /// <summary>
    /// StringSampler 颜色路径的闭式解：两端先解析成 CSS 颜色，中间帧再逐通道插值并重新格式化成
    /// <c>rgba(r, g, b, a)</c>。
    /// </summary>
    /// <remarks>
    /// 源码 <c>StringSampler.InsertFrame</c> 分两种情形。t == 0 与 t == 1 原样写回调用方给的字符串
    /// （字符串有损，端点不可能被重新格式化成"值相等"的另一种写法），所以这里直接返回两个端点常量；
    /// 其余 t 才走颜色插值。<c>InterpolateColor</c> 里 R/G/B 共用一个 [0,255] 有界进度：起始 B=0 让
    /// t &lt; 0 时整组被 B 先拽回 0，而 t &gt; 1 时由 R 先顶到 255 停住；alpha 自成一界、不做组内钳制
    /// （本组端点下 A 始终落在 0..255 内）。随后每个通道先四舍五入再饱和到 0..255，alpha 除以 255 后
    /// 以 <c>0.###</c> 写成 0..1。
    /// </remarks>
    private static string Expected(double t)
    {
        if (t == 0d) return StartHex;
        if (t == 1d) return EndHex;

        var progress = SharedProgress(t, 255d, (200d, 240d), (100d, 180d), (0d, 50d));
        var alpha = 128d + (64d - 128d) * t;

        return string.Create(CultureInfo.InvariantCulture,
            $"rgba({Channel(200d + 40d * progress)}, {Channel(100d + 80d * progress)}, {Channel(0d + 50d * progress)}, {alpha / 255d:0.###})");
    }

    internal static IReadOnlyList<SamplerEntry> All { get; } =
    [
        EntryFactory.Create<StringSampler, Target, string>(
            new StringSampler(),
            "Razor",
            SamplerRule.Saturate,
            StartHex,
            EndHex,
            target => target.Value,
            Expected),
    ];
}
