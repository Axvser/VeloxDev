using System.Globalization;
using System.Text;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo.Models;

/// <summary>
/// 把该适配器发布的每一个采样器都推一帧，把结果写成机器可读的载荷。
/// </summary>
/// <remarks>
/// Razor 适配器只注册 <c>StringSampler</c>，产物是 CSS 颜色字符串。字符串不进"分量是数字"的载荷格式，
/// 所以这里把它拆成字符码点 —— 无损、无需转义，也不必让验收侧为字符串开一条特例的比较路径。
/// </remarks>
internal static class SamplerProbe
{
    /// <summary>
    /// 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
    /// 数，所以时间本身不引入误差。
    /// </summary>
    private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

    /// <summary>一个目标类，每条属性挂一个该适配器发布的采样器。</summary>
    private sealed class Target
    {
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>一条采样器：把手令牌、它要写的属性、采样器本身，以及一对端点工厂。</summary>
    private sealed record ProbeSpec(string Name, string Property, Func<ISampler> Create, Func<object> Start, Func<object> End);

    /// <summary>端点与 Samplers/RazorEntries.cs 里的一致：8 位十六进制 <c>#RRGGBBAA</c>。</summary>
    private const string StartHex = "#C8640080";

    private const string EndHex = "#F0B43240";

    /// <summary>
    /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
    /// </summary>
    private static readonly ProbeSpec[] Probes =
    [
        new(nameof(StringSampler), nameof(Target.Value), () => new StringSampler(), () => StartHex, () => EndHex),
    ];

    /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    /// <summary>
    /// 跑一条采样器，产出载荷文本，形如 <c>v=1;seq=1;n=5;s.StringSampler.0=String,35,67,…;</c>。
    /// </summary>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
    internal static string Run(string samplerName, long sequence)
    {
        var payload = new StringBuilder($"v=1;seq={sequence};n={Times.Length};");

        for (var index = 0; index < Times.Length; index++)
        {
            payload.Append($"s.{samplerName}.{index}={Describe(FrameValue(samplerName, Times[index]))};");
        }

        return payload.ToString();
    }

    /// <summary>
    /// 跑一条采样器的一帧，返回它写出来的值。
    /// </summary>
    /// <remarks>
    /// 验收与演示台走的是同一个入口：载荷报的就是这个返回值，演出台画出来的也是这个返回值 ——
    /// 屏幕上看到的和断言里读的必然是同一个数，不可能各说各话。
    /// </remarks>
    internal static object? FrameValue(string samplerName, double t)
    {
        var probe = Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
            ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

        var property = TransitionProperty.FromProperty(typeof(Target).GetProperty(probe.Property)!);

        // 每帧全新目标、全新端点：端点实例跨帧复用会被采样器原地改动污染。
        var target = new Target();
        object? working = null;
        probe.Create().InsertFrame(target, property, ref working, probe.Start(), probe.End(), null, t);

        return property.GetValue(target);
    }

    /// <summary>
    /// 值的规范文本：类型名打头，后面是该类型的分量，顺序固定。
    /// </summary>
    /// <remarks>
    /// 字符串取其字符码点：<c>#C8640080</c> 与 <c>rgba(200, 100, 0, 0.502)</c> 都含 <c>,</c>，直接写进载荷
    /// 会把字段分隔符弄坏，而码点序列是纯数字、无歧义，也仍然是逐字符精确的。
    /// </remarks>
    private static string Describe(object? value) => value switch
    {
        string text => "String," + string.Join(",", text.Select(character => Number(character))),
        _ => throw new InvalidOperationException(
            $"SamplerProbe 不认识产物类型 {value?.GetType().Name ?? "null"}，序列化要跟着采样器一起改。"),
    };

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
