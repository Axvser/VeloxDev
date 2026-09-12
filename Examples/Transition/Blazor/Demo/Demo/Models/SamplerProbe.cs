using System.Globalization;
using System.Reflection;
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

    /// <summary>
    /// 一个目标类，每条属性挂一个该适配器发布的采样器。
    /// </summary>
    /// <remarks>
    /// 与桌面那几侧不同，这里的目标**常驻**：真动画要把它当 <c>Transition&lt;Target&gt;</c> 的目标，
    /// 演示台每帧读它的值当背景色。桌面那侧目标是在屏控件，浏览器里没有控件属性可写，这个对象就是那个位置。
    /// </remarks>
    internal sealed class Target
    {
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>一条采样器：把手令牌、它要写的属性、采样器本身，以及一对端点工厂。</summary>
    private sealed record ProbeSpec(
        string Name,
        string Description,
        string Property,
        Func<ISampler> Create,
        Func<object> Start,
        Func<object> End);

    /// <summary>一个产物读出来的样子：类型名 + 固定顺序的分量。</summary>
    /// <param name="TypeTag">产物的运行时类型名。认不出来的类型也照报，由测试侧去说"类型不对"。</param>
    /// <param name="Components">该类型的分量。认不出的类型没有分量，所以是空数组。</param>
    internal sealed record Measurement(string TypeTag, double[] Components);

    /// <summary>端点与 Samplers/RazorEntries.cs 里的一致：8 位十六进制 <c>#RRGGBBAA</c>。</summary>
    private const string StartHex = "#C8640080";

    private const string EndHex = "#F0B43240";

    /// <summary>
    /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
    /// </summary>
    private static readonly ProbeSpec[] Probes =
    [
        new(nameof(StringSampler),
            "CSS 颜色字符串：字符串没有分量可言，载荷取的是字符码点；t=0/1 直接给端点十六进制。",
            nameof(Target.Value), () => new StringSampler(), () => StartHex, () => EndHex),
    ];

    /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    /// <summary>
    /// 演示台把一条采样器跑一遍的时长。<c>VELOXDEV_BENCH_MS</c> 可以覆盖 —— 验收要在真 app 里把每条采样器都跑一遍，
    /// 默认时长下光播放就是几百秒，快跑时用它压短。
    /// </summary>
    internal static TimeSpan BenchDuration { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("VELOXDEV_BENCH_MS"), out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Max(1, milliseconds))
            : TimeSpan.FromMilliseconds(DefaultBenchMs);

    private const int DefaultBenchMs = 800;

    /// <summary>
    /// 跑完之后的兜底上限：值还在变就继续等，等到这里为止。真动画的最后一帧是排队投递的，
    /// 固定余量在负载重的机器上会读早，而"值不再变"才是它落地的判据。
    /// </summary>
    internal static readonly TimeSpan BenchSettleCap = TimeSpan.FromSeconds(2);

    /// <summary>这一条采样器的完整定义。名字对不上说明把手与探针表不同步了。</summary>
    private static ProbeSpec Spec(string samplerName)
        => Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
            ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

    /// <summary>这一条采样器写在目标对象的哪个属性上。</summary>
    internal static PropertyInfo Property(string samplerName)
        => typeof(Target).GetProperty(Spec(samplerName).Property)!;

    /// <summary>
    /// 这一条案例在界面上那句"这条在验什么"。
    /// </summary>
    /// <remarks>
    /// 与把手、端点、采样器同一张表：加一条采样器仍然只需要改 <see cref="Probes"/> 一处，行里的文字跟着来。
    /// </remarks>
    internal static string Description(string samplerName) => Spec(samplerName).Description;

    /// <summary>这一条采样器的实例。每次都要新的：采样器本身可能带状态。</summary>
    internal static ISampler Create(string samplerName) => Spec(samplerName).Create();

    /// <summary>这一条采样器声明的起点。</summary>
    internal static object Start(string samplerName) => Spec(samplerName).Start();

    /// <summary>这一条采样器声明的终点。</summary>
    internal static object End(string samplerName) => Spec(samplerName).End();

    /// <summary>
    /// 目标对象上这个属性**此刻**持有的值，按分量读出来。
    /// </summary>
    /// <remarks>
    /// 真动画那一段时间靠它采样：采样当刻就把分量取成数字，绝不把值对象留到后面。
    /// </remarks>
    internal static Measurement Read(Target target, string samplerName)
        => Measure(Property(samplerName).GetValue(target));

    /// <summary>
    /// 这一行此刻的值是否**就是**它声明的起点。
    /// </summary>
    /// <remarks>
    /// 给顶栏那三个按钮用的可观测量。逐分量精确比较而不是带容差：重置是**把声明的那对端点原样写回去**，
    /// 所以"已经回到起点"必然是逐位相同，不需要容差去猜。离散型的那几条永远停在起点，于是它们恒为真 ——
    /// 这是它们应有的样子，不是漏报。
    /// </remarks>
    internal static bool MatchesStart(Target target, string samplerName)
        => SameComponents(Measure(Start(samplerName)).Components, Read(target, samplerName).Components);

    /// <summary>两个分量向量是否逐位相同。用来判断"这一拍和上一拍比有没有变"。</summary>
    internal static bool SameComponents(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index]) return false;
        }

        return true;
    }

    /// <summary>
    /// 跑一条采样器，产出载荷文本，形如 <c>v=1;seq=1;n=5;s.StringSampler.0=String,35,67,…;</c>。
    /// </summary>
    /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
    /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
    internal static string Run(string samplerName, long sequence)
        => $"v=1;seq={sequence};n={Times.Length};" + RunFrames(samplerName);

    /// <summary>
    /// 只要那五帧的字段，不带头部 —— 批量载荷把每一行的这一段拼在一起（逐行载荷与它同一套字段名，
    /// 所以两边共用同一个解析器）。
    /// </summary>
    internal static string RunFrames(string samplerName)
    {
        var payload = new StringBuilder();

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
        var probe = Spec(samplerName);
        var property = TransitionProperty.FromProperty(Property(samplerName));

        // 每帧全新目标、全新端点：端点实例跨帧复用会被采样器原地改动污染。
        var target = new Target();
        object? working = null;
        probe.Create().InsertFrame(target, property, ref working, probe.Start(), probe.End(), null, t);

        return property.GetValue(target);
    }

    /// <summary>
    /// 把一个产物读成类型名 + 分量，顺序固定。
    /// </summary>
    /// <remarks>
    /// 字符串取其字符码点：<c>#C8640080</c> 与 <c>rgba(200, 100, 0, 0.502)</c> 都含 <c>,</c>，直接写进载荷
    /// 会把字段分隔符弄坏，而码点序列是纯数字、无歧义，也仍然是逐字符精确的。
    /// <para>
    /// 码点序列是**表示**而不是量：它的长度随产物字符串而变，逐位取最小/最大值没有意义。
    /// 验收侧因此不对这个类型做"动没动过"的判断。
    /// </para>
    /// <para>
    /// <b>认不出的类型不抛。</b> 这里读的是"目标此刻持有什么"，而"类型变了"本身就是要报给验收的异常之一；
    /// 在定时器回调里抛出去只会把电路打断，把异常变成一次崩溃。所以照实报类型名、分量留空，让测试侧去说。
    /// </para>
    /// </remarks>
    internal static Measurement Measure(object? value) => value switch
    {
        string text => new("String", [.. text.Select(character => (double)character)]),
        null => new("null", []),
        _ => new(value.GetType().Name, []),
    };

    /// <summary>载荷里的规范文本：类型名打头，后面是逗号分隔的分量。</summary>
    private static string Describe(object? value)
    {
        var (typeTag, components) = Measure(value);
        return components.Length == 0 ? typeTag : $"{typeTag},{Vector(components)}";
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Vector(IReadOnlyList<double> values) => string.Join(",", values.Select(Number));

    /// <summary>载荷字段里不能出现分隔符，异常消息还得是一行。</summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "-";

        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            cleaned.Append(character switch
            {
                ';' or '=' => '_',
                _ when char.IsControl(character) => ' ',
                _ => character,
            });
        }

        return cleaned.ToString();
    }

    /// <summary>
    /// 真动画那一段时间里对目标属性的采样累积。
    /// </summary>
    /// <remarks>
    /// 收的是 <see cref="Measurement"/> 里已经取成数字的分量，不是值对象（见 <see cref="Read"/>）。
    /// 非有限值单独计数、**不喂给 min/max** —— 一个 NaN 喂进去会把那个分量的包络永久粘住，
    /// 于是"有过 NaN"这件事就再也看不出来了。
    /// <para>
    /// 分量个数变了就把包络重来一段（见 <see cref="Measure"/> 说的：字符串的长度随 t 变）。
    /// </para>
    /// <para>
    /// <see cref="Settled"/> 是"最后一帧已经落地"的判据：流水线的末帧是排队投递的，固定余量在负载重的机器上
    /// 会读早，而值连续几拍不再变是它真的到了。
    /// </para>
    /// </remarks>
    internal sealed class LiveWatch
    {
        /// <summary>
        /// 连续多少拍同一个值算落定。
        /// </summary>
        /// <remarks>
        /// 三十拍（这条链路上每拍 16ms，约 480ms），不是两三拍。落定要代表的是"流水线的末帧已经落到目标上"，
        /// 而末帧是排队投递的：Blazor 上它得等电路线程腾出手，慢的时候一拍与下一拍之间能隔几十毫秒。
        /// <para>
        /// 更麻烦的是产物**被量化**的时候：Blazor 的 CSS 颜色字符串按三位小数格式化，动画末段缓动又被压平，
        /// 连着好几帧能格式化出同一个字符串 —— 于是"值不再变"在动画还没跑完时就成立了。实测：bench 1600ms、
        /// 窗口 5 拍时这条会误报成"没跑到终点"（3 次错 2 次），窗口 30 拍后 3 次全对。
        /// </para>
        /// </remarks>
        private const int SettledTicks = 30;

        private double[] _min = [];
        private double[] _max = [];
        private double[] _last = [];
        private double[] _previous = [];
        private int _unchanged;
        private int _samples;
        private int _bad;
        private string _type = "-";
        private string? _error;

        /// <summary>这段时间一共采到多少拍。</summary>
        internal int Samples => _samples;

        /// <summary>值连续几拍没变了。</summary>
        internal bool Settled => _samples > 0 && _unchanged >= SettledTicks;

        /// <summary>起这条动画时就抛出来的异常。放在每一份观察里，所以十几条并发时谁的错是谁的。</summary>
        internal void Fail(string error) => _error = error;

        internal void Reset()
        {
            _min = [];
            _max = [];
            _last = [];
            _previous = [];
            _unchanged = 0;
            _samples = 0;
            _bad = 0;
            _type = "-";
            _error = null;
            }

        internal void Observe(string typeTag, IReadOnlyList<double> components)
        {
            _type = typeTag;

            if (_last.Length != components.Count)
            {
                _min = new double[components.Count];
                _max = new double[components.Count];
                _last = new double[components.Count];
                _previous = new double[components.Count];
                Array.Fill(_min, double.PositiveInfinity);
                Array.Fill(_max, double.NegativeInfinity);
                _unchanged = 0;
            }

            _samples++;

            var changed = false;
            for (var index = 0; index < components.Count; index++)
            {
                var value = components[index];
                _last[index] = value;

                // 头一拍没有"上一拍"可比，算它变了，免得连续相等的起点提前报落定。
                if (_samples == 1 || !SameAsPrevious(value, _previous[index])) changed = true;
                _previous[index] = value;

                if (!double.IsFinite(value))
                {
                    _bad++;
                    continue;
                }

                if (value < _min[index]) _min[index] = value;
                if (value > _max[index]) _max[index] = value;
            }

            _unchanged = changed ? 0 : _unchanged + 1;
        }

        /// <summary>
        /// Whether this sample's value is the same as the previous one's.
        /// </summary>
        /// <remarks>
        /// NaN 要算作没变。用 <c>!=</c> 直接比的话，NaN 永远不等于自己，于是"值一直在变"，落定判据永远不成立，
        /// 一条真的产出了 NaN 的动画会把每一次采样都拖到兜底上限。而"有没有 NaN"已经由 <c>bad</c> 单独报了。
        /// </remarks>
        private static bool SameAsPrevious(double value, double previous)
            => value == previous || (double.IsNaN(value) && double.IsNaN(previous));

        /// <summary>
        /// 这段动画的机器可读结果：目标属性这段时间里被写成了什么。
        /// </summary>
        /// <param name="sampler">跑的是哪条采样器。</param>
        /// <param name="sequence">点击序号，与 <c>over.conf</c> 共用 —— 载荷靠它证明这一份是新的。</param>
        /// <param name="error">起动画时就抛出来的异常，没有则为 null。</param>
        internal string Digest(string sampler, long sequence)

            => $"v=1;seq={sequence};done=1;sampler={sampler};" + RowFields();


        /// <summary>

        /// 这一行那组字段，前缀是 <c>l.&lt;采样器名&gt;.</c> —— 批量载荷里十几行并排，靠它分得开。

        /// </summary>

        internal string BatchFields(string sampler)

        {

            var prefix = $"l.{sampler}.";

            var payload = new StringBuilder();


            foreach (var field in RowFields().Split(';', StringSplitOptions.RemoveEmptyEntries))

            {

                payload.Append(prefix).Append(field).Append(';');

            }


            return payload.ToString();

        }


        private string RowFields()

            => $"type={_type};k={_last.Length};samples={_samples};bad={_bad};err={Sanitize(_error)};"

             + $"last={Vector(_last)};min={Vector(_min)};max={Vector(_max)};";

        /// <summary>点击那一刻先写一份，让验收侧立刻看到这一份是新的，然后等 <see cref="Digest"/>。</summary>
        internal static string Pending(string sampler, long sequence)
            => $"v=1;seq={sequence};done=0;sampler={sampler};";
    }
}
