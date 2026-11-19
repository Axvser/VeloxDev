using System.Globalization;
using VeloxDev.Adapters.NativeSamplers;

namespace VeloxDev.SamplerTest;

/// <summary>The sampler the Razor adapter ships, with the closed form it follows.</summary>
internal static class RazorEntries
{
    // 一条 string 属性的目标，让条目有真实的写入路径。
    private sealed class Target
    {
        public string Value { get; set; } = string.Empty;
    }

    // 起始端，8 位十六进制 #RRGGBBAA：R=200、G=100、B=0、A=128。
    private const string StartHex = "#C8640080";

    // 结束端：R=240、G=180、B=50、A=64。
    private const string EndHex = "#F0B43240";

    // 一组颜色通道共用一个进度：谁先出界就停在谁那里，后面的通道不能把它再拉回来 ——
    // 与库里的 BoundedProgress 同一条规则，但这里是独立重述的。
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

    // 先四舍五入再饱和到 0..255 —— 直接转 byte 会把 300 折成 44。
    private static byte Channel(double value)
    {
        if (value <= 0d) return 0;
        if (value >= 255d) return 255;
        return (byte)Math.Round(value);
    }

    // StringSampler 颜色路径的闭式解：两端先解析成 CSS 颜色，中间帧再逐通道插值并重新格式化成
    // rgba(r, g, b, a)。
    // 源码 StringSampler.InsertFrame 分两种情形。t == 0 与 t == 1 原样写回调用方给的字符串
    // （字符串有损，端点不可能被重新格式化成"值相等"的另一种写法），所以这里直接返回两个端点常量；
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
