using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;

namespace Demo.Models;

// 把该适配器发布的每一个采样器都推一帧，把结果写成机器可读的载荷。
// Razor 适配器只注册 StringSampler，产物是 CSS 颜色字符串。字符串不进"分量是数字"的载荷格式，
// 所以这里把它拆成字符码点 —— 无损、无需转义，也不必让验收侧为字符串开一条特例的比较路径。
internal static class SamplerProbe
{
    // 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
    // 数，所以时间本身不引入误差。
    private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

    // 一个目标类，每条属性挂一个该适配器发布的采样器。
    // 演示台每帧读它的值当背景色。桌面那侧目标是在屏控件，浏览器里没有控件属性可写，这个对象就是那个位置。
    internal sealed class Target
    {
        public string Value { get; set; } = string.Empty;

        // 索引器那两条写的集合：两个 CSS 颜色字符串的槽。
        // 浏览器里没有控件属性可写，目标就是这个对象 —— 所以"可索引的集合"也只能长在它身上。
        // 定长数组而不是 List：路径里的元素必须在写它之前就先存在。
        public string[] Slots { get; } = [string.Empty, string.Empty];
    }

    // 一条采样器：把手令牌、它要写的属性、采样器本身，以及一对端点工厂。
    private sealed record ProbeSpec(
        string Name,
        string Description,
        string? Property,
        Func<ISampler> Create,
        Func<object> Start,
        Func<object> End,
        Func<TransitionProperty>? Path = null);

    // 一个产物读出来的样子：类型名 + 固定顺序的分量。
    internal sealed record Measurement(string TypeTag, double[] Components);

    // 端点与 Samplers/RazorEntries.cs 里的一致：8 位十六进制 #RRGGBBAA。
    private const string StartHex = "#C8640080";

    private const string EndHex = "#F0B43240";

    // 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
    private static readonly ProbeSpec[] Probes =
    [
        new(nameof(StringSampler),
            "CSS 颜色字符串：字符串没有分量可言，载荷取的是字符码点；t=0/1 直接给端点十六进制。",
            nameof(Target.Value), () => new StringSampler(), () => StartHex, () => EndHex),

        // 两条索引器路径。验的是「路径落到哪个槽上」：Slots 的第 0 与第 1 个元素，用同一个采样器、
        // 不同的端点，并行跑一次批量就该各落各的。下标是编译期常量，所以这条路径的身份是与值解耦的。
        new("GradientStop0Color",
            "索引器路径：写 Slots[0] —— 索引真的落到槽上，而不是被当成整条集合。",
            null, () => new StringSampler(), () => StartHex, () => EndHex, Path: () => SlotsPath(0)),
        new("GradientStop1Color",
            "索引器路径：相邻下标必须互不覆盖（Slots[1]）—— 与上一行同一个采样器、同一刻并行跑。",
            null, () => new StringSampler(), () => Slot1StartHex, () => Slot1EndHex, Path: () => SlotsPath(1)),
    ];

    // 索引器那两条的第二个端点对：同一个集合的两个槽，两对颜色必须不同。
    private const string Slot1StartHex = "#3C14C8FA";

    private const string Slot1EndHex = "#D2D2280A";

    // 每条采样器的名字，也是它把手的令牌后缀。界面由它生成。
    internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

    // 演示台把一条采样器跑一遍的时长。VELOXDEV_BENCH_MS 可以覆盖 —— 验收要在真 app 里把每条采样器都跑一遍，
    // 默认时长下光播放就是几百秒，快跑时用它压短。
    internal static TimeSpan BenchDuration { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("VELOXDEV_BENCH_MS"), out var milliseconds)
            ? TimeSpan.FromMilliseconds(Math.Max(1, milliseconds))
            : TimeSpan.FromMilliseconds(DefaultBenchMs);

    private const int DefaultBenchMs = 800;

    // 跑完之后的兜底上限：值还在变就继续等，等到这里为止。真动画的最后一帧是排队投递的，
    // 固定余量在负载重的机器上会读早，而"值不再变"才是它落地的判据。
    internal static readonly TimeSpan BenchSettleCap = TimeSpan.FromSeconds(2);

    // 这一条采样器的完整定义。名字对不上说明把手与探针表不同步了。
    private static ProbeSpec Spec(string samplerName)
        => Probes.FirstOrDefault(candidate => candidate.Name == samplerName)
            ?? throw new InvalidOperationException($"没有名为 {samplerName} 的采样器；把手与探针表不同步了。");

    // 这一条采样器写在目标对象的哪个属性上。
    // 缓存下来就没有这层疑问。
    internal static TransitionProperty Path(string samplerName) => Paths[samplerName];

    // 建一条 Slots[i]。下标是常量，所以这条路径的身份只由下标决定。
    private static TransitionProperty SlotsPath(int index)
        => TransitionProperty.TryCreate(
            (Expression<Func<Target, string>>)(target => target.Slots[index]),
            out var property)
            ? property!
            : throw new InvalidOperationException($"索引器路径 Slots[{index}] 建不出来。");

    private static readonly Dictionary<string, TransitionProperty> Paths =
        Probes.ToDictionary(
            static probe => probe.Name,
            static probe => probe.Path is null
                ? TransitionProperty.FromProperty(typeof(Target).GetProperty(probe.Property!)!)
                : probe.Path());

    // 这一条案例在界面上那句"这条在验什么"。
    internal static string Description(string samplerName) => Spec(samplerName).Description;

    // 这一条采样器的实例。每次都要新的：采样器本身可能带状态。
    internal static ISampler Create(string samplerName) => Spec(samplerName).Create();

    // 这一条采样器声明的起点。
    internal static object Start(string samplerName) => Spec(samplerName).Start();

    // 这一条采样器声明的终点。
    internal static object End(string samplerName) => Spec(samplerName).End();

    // 目标对象上这个属性**此刻**持有的值，按分量读出来。
    internal static Measurement Read(Target target, string samplerName)
        => Measure(Path(samplerName).GetValue(target));

    // 这一行此刻的值是否**就是**它声明的起点。
    // 所以"已经回到起点"必然是逐位相同，不需要容差去猜。离散型的那几条永远停在起点，于是它们恒为真 ——
    internal static bool MatchesStart(Target target, string samplerName)
        => SameComponents(Measure(Start(samplerName)).Components, Read(target, samplerName).Components);

    // 两个分量向量是否逐位相同。用来判断"这一拍和上一拍比有没有变"。
    internal static bool SameComponents(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count != right.Count) return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index]) return false;
        }

        return true;
    }

    // 跑一条采样器，产出载荷文本，形如 v=1;seq=1;n=5;s.StringSampler.0=String,35,67,…;。
    internal static string Run(string samplerName, long sequence)
        => $"v=1;seq={sequence};n={Times.Length};" + RunFrames(samplerName);

    // 只要那五帧的字段，不带头部 —— 批量载荷把每一行的这一段拼在一起（逐行载荷与它同一套字段名，
    // 所以两边共用同一个解析器）。
    internal static string RunFrames(string samplerName)
    {
        var payload = new StringBuilder();

        for (var index = 0; index < Times.Length; index++)
        {
            payload.Append($"s.{samplerName}.{index}={Describe(FrameValue(samplerName, Times[index]))};");
        }

        return payload.ToString();
    }

    // 跑一条采样器的一帧，返回它写出来的值。
    // 屏幕上看到的和断言里读的必然是同一个数，不可能各说各话。
    internal static object? FrameValue(string samplerName, double t)
    {
        var probe = Spec(samplerName);
        var property = Path(samplerName);

        // 每帧全新目标、全新端点：端点实例跨帧复用会被采样器原地改动污染。
        var target = new Target();
        object? working = null;
        probe.Create().InsertFrame(target, property, ref working, probe.Start(), probe.End(), null, t);

        return property.GetValue(target);
    }

    // 把一个产物读成类型名 + 分量，顺序固定。
    // 字符串取其字符码点：#C8640080 与 rgba(200, 100, 0, 0.502) 都含 ,，直接写进载荷
    // 会把字段分隔符弄坏，而码点序列是纯数字、无歧义，也仍然是逐字符精确的。
    // 码点序列是**表示**而不是量：它的长度随产物字符串而变，逐位取最小/最大值没有意义。
    // 在定时器回调里抛出去只会把电路打断，把异常变成一次崩溃。所以照实报类型名、分量留空，让测试侧去说。
    internal static Measurement Measure(object? value) => value switch
    {
        string text => new("String", [.. text.Select(character => (double)character)]),
        null => new("null", []),
        _ => new(value.GetType().Name, []),
    };

    // 载荷里的规范文本：类型名打头，后面是逗号分隔的分量。
    private static string Describe(object? value)
    {
        var (typeTag, components) = Measure(value);
        return components.Length == 0 ? typeTag : $"{typeTag},{Vector(components)}";
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Vector(IReadOnlyList<double> values) => string.Join(",", values.Select(Number));

    // 载荷字段里不能出现分隔符，异常消息还得是一行。
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

    // 真动画那一段时间里对目标属性的采样累积。
    // 非有限值单独计数、**不喂给 min/max** —— 一个 NaN 喂进去会把那个分量的包络永久粘住，
    // 于是"有过 NaN"这件事就再也看不出来了。
    // 分量个数变了就把包络重来一段（见 Measure 说的：字符串的长度随 t 变）。
    // Settled 是"最后一帧已经落地"的判据：流水线的末帧是排队投递的，固定余量在负载重的机器上
    internal sealed class LiveWatch
    {
        // 连续多少拍同一个值算落定。
        // 三十拍（这条链路上每拍 16ms，约 480ms），不是两三拍。落定要代表的是"流水线的末帧已经落到目标上"，
        // 而末帧是排队投递的：Blazor 上它得等电路线程腾出手，慢的时候一拍与下一拍之间能隔几十毫秒。
        // 更麻烦的是产物**被量化**的时候：Blazor 的 CSS 颜色字符串按三位小数格式化，动画末段缓动又被压平，
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

        // 这段时间一共采到多少拍。
        internal int Samples => _samples;

        // 值连续几拍没变了。
        internal bool Settled => _samples > 0 && _unchanged >= SettledTicks;

        // 起这条动画时就抛出来的异常。放在每一份观察里，所以十几条并发时谁的错是谁的。
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

        // Whether this sample's value is the same as the previous one's.
        // NaN 要算作没变。用 != 直接比的话，NaN 永远不等于自己，于是"值一直在变"，落定判据永远不成立，
        // 一条真的产出了 NaN 的动画会把每一次采样都拖到兜底上限。而"有没有 NaN"已经由 bad 单独报了。
        private static bool SameAsPrevious(double value, double previous)
            => value == previous || (double.IsNaN(value) && double.IsNaN(previous));

        // 这段动画的机器可读结果：目标属性这段时间里被写成了什么。
        internal string Digest(string sampler, long sequence)

            => $"v=1;seq={sequence};done=1;sampler={sampler};" + RowFields();


        /// <summary>

        // 这一行那组字段，前缀是 l.<采样器名>. —— 批量载荷里十几行并排，靠它分得开。

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

        // 点击那一刻先写一份，让验收侧立刻看到这一份是新的，然后等 Digest。
        internal static string Pending(string sampler, long sequence)
            => $"v=1;seq={sequence};done=0;sampler={sampler};";
    }
}
