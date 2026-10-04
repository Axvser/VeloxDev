using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using VeloxDev.Adapters.NativeSamplers;
using VeloxDev.TransitionSystem;
using VeloxDev.TransitionSystem.Abstractions;
using VeloxDev.TransitionSystem.NativeSamplers;

namespace Demo
{
    // 把该适配器发布的每一个采样器各推一帧序列，把结果写成机器可读的载荷。
    // 重绘，于是"画出来"这件事不需要另一套映射代码。
    // 采样器写值 → 读回"这条真实的 UI 交互路径，而不是让 app 在启动时闷头算完一遍。
    // 端点是工厂而不是实例：引用类型的端点若跨帧复用，一个就地改动端点的采样器会把后面的帧一起带偏，
    // 而「不得改动交给 InsertFrame 的 start/end」正是库对采样器的硬约束 —— 每帧一对全新端点才看得见它。
    internal static class SamplerProbe
    {
        // 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
        // 数，所以时间本身不引入误差。
        private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

        // 每条采样器的名字 —— 取自采样器类型名，所以把手令牌与载荷键两处说的是同一件事。
        internal static class Kinds
        {
            internal const string PaddingSampler = nameof(PaddingSampler);

            // 索引器路径的两条：它们验的不是某个采样器，而是**路径能落到具体的槽上**。
            // 相邻下标必须是两条不同的路径：索引器的 PropertyInfo 对每个下标都是同一个 "Item"，
            internal const string GradientStop0Color = "GradientStop0Color";

            internal const string GradientStop1Color = "GradientStop1Color";
        }

        // 一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。
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

        // 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
        // 端点与 Samplers/WinFormsEntries.cs 里的一致。
        private static readonly ProbeSpec[] Probes =
        [
            new(Kinds.PaddingSampler, "内边距：四条边各自线性外推，取整是向零截断而不是四舍五入 —— 差 1 的那一处就是它。", nameof(SamplerSubject.Inset), () => new PaddingSampler(),
                () => new Padding(20, 20, 60, 60), () => new Padding(100, 5, 0, 0)),

            // 两条索引器路径。验的是「路径落到哪个槽上」：Controls 的第 0 与第 1 个孩子，用同一个采样器、
            // 不同的端点，并行跑一次批量就该各落各的。下标是编译期常量，所以这条路径的身份是与值解耦的。
            new(Kinds.GradientStop0Color,
                "索引器路径：写渐变第 0 个停靠点的颜色（this[0]）—— 索引真的落到槽上，而不是被当成整条集合。",
                null, () => new ColorSampler(), () => Ramp0Start, () => Ramp0End, Path: () => GradientStopPath(0)),
            new(Kinds.GradientStop1Color,
                "索引器路径：相邻下标必须互不覆盖（this[1]）—— 与上一行同一个采样器、同一刻并行跑。",
                null, () => new ColorSampler(), () => Ramp1Start, () => Ramp1End, Path: () => GradientStopPath(1)),
        ];

        // 每条采样器的名字，也是它把手的令牌后缀。界面由它生成。
        internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

        // 某一条的文字描述，给案例列表那一行用。
        internal static string Description(string samplerName) => Spec(samplerName).Description;

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

        // 这一条采样器写在被写控件的哪个属性上。
        // 缓存下来就没有这层疑问。
        internal static TransitionProperty Path(string samplerName) => Paths[samplerName];

        private static readonly Dictionary<string, TransitionProperty> Paths =
            Probes.ToDictionary(
                static probe => probe.Name,
                static probe => probe.Path is null
                    ? TransitionProperty.FromProperty(typeof(SamplerSubject).GetProperty(probe.Property!)!)
                    : probe.Path());

        // 建一条 this[i]。下标是常量，所以这条路径的身份只由下标决定。
        private static TransitionProperty GradientStopPath(int index)
            => TransitionProperty.TryCreate(
                (Expression<Func<SamplerSubject, Color>>)(subject => subject[index]),
                out var property)
                ? property!
                : throw new InvalidOperationException($"索引器路径 this[{index}] 建不出来。");

        // 索引器那两条的端点：同一个集合的两个槽，两对颜色必须不同 —— 否则两条路径的闭式解一模一样，
        // "各落各的槽"这件事就无从断言。
        private static readonly Color Ramp0Start = Color.FromArgb(200, 200, 100, 50);
        private static readonly Color Ramp0End = Color.FromArgb(250, 240, 180, 120);
        private static readonly Color Ramp1Start = Color.FromArgb(120, 30, 200, 250);
        private static readonly Color Ramp1End = Color.FromArgb(200, 210, 40, 10);

        // 这一条采样器的实例。每次都要新的：采样器本身可能带状态。
        internal static ISampler Create(string samplerName) => Spec(samplerName).Create();

        // 这一条采样器声明的起点。
        internal static object Start(string samplerName) => Spec(samplerName).Start();

        // 这一条采样器声明的终点。
        internal static object End(string samplerName) => Spec(samplerName).End();

        // 被写控件上这个属性**此刻**持有的值，按分量读出来。
        internal static Measurement Read(SamplerSubject subject, string samplerName)
            => Measure(Path(samplerName).GetValue(subject));

        // 这一行此刻的值是否**就是**它声明的起点。
        // 所以"已经回到起点"必然是逐位相同，不需要容差去猜。离散型的那几条永远停在起点，于是它们恒为真 ——
        internal static bool MatchesStart(SamplerSubject subject, string samplerName)
            => SameComponents(Measure(Start(samplerName)).Components, Read(subject, samplerName).Components);

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

        // 在被写控件上跑一条采样器的五个固定缓动时间，产出载荷文本，形如
        // v=1;seq=1;n=5;s.PaddingSampler.0=Padding,20,20,60,60;。
        internal static string Run(SamplerSubject subject, string samplerName, long sequence)
            => $"v=1;seq={sequence};n={Times.Length};" + RunFrames(subject, samplerName);

        // 只要那五帧的字段，不带头部 —— 批量载荷把每一行的这一段拼在一起（逐行载荷与它同一套字段名，
        // 所以两边共用同一个解析器）。
        internal static string RunFrames(SamplerSubject subject, string samplerName)
        {
            var payload = new StringBuilder();

            for (var index = 0; index < Times.Length; index++)
            {
                payload.Append($"s.{samplerName}.{index}={Describe(Frame(subject, samplerName, Times[index]))};");
            }

            return payload.ToString();
        }

        // 在被写控件上跑一条采样器的一帧，返回**从控件读回**的值。
        // 界面上看到的和断言里读的必然是同一个数，不可能各说各话。
        internal static object? Frame(SamplerSubject subject, string samplerName, double t)
        {
            var probe = Spec(samplerName);
            var property = Path(samplerName);

            // 每帧一对全新端点：端点实例跨帧复用会被采样器原地改动污染。
            object? working = null;
            probe.Create().InsertFrame(subject, property, ref working, probe.Start(), probe.End(), null, t);

            return property.GetValue(subject);
        }

        // 把一个产物读成类型名 + 分量，顺序固定。
        // 而不是把两种情况都读成"数值对不上"。
        // 在一个属性回调里抛出去只会把 demo 打挂，把异常变成一次崩溃。所以照实报类型名、分量留空，让测试侧去说。
        internal static Measurement Measure(object? value) => value switch
        {
            Padding padding => new("Padding", [padding.Left, padding.Top, padding.Right, padding.Bottom]),
            Color color => new("Color", [color.A, color.R, color.G, color.B]),
            null => new("null", []),
            _ => new(value.GetType().Name, []),
        };

        // 载荷里的规范文本：类型名打头，后面是逗号分隔的分量。
        private static string Describe(object? value)
        {
            var (typeTag, components) = Measure(value);
            return components.Length == 0 ? typeTag : $"{typeTag},{Vector(components)}";
        }

        // 往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。
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

        // 真动画那一段时间里对控件属性的采样累积。
        // 非有限值单独计数、**不喂给 min/max** —— 一个 NaN 喂进去会把那个分量的包络永久粘住，
        // 于是"有过 NaN"这件事就再也看不出来了。
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

            // 这段动画的机器可读结果：控件属性这段时间里被写成了什么。
            internal string Digest(string sampler, long sequence)
                => $"v=1;seq={sequence};done=1;sampler={sampler};" + RowFields();

            // 这一行那组字段，前缀是 l.<采样器名>. —— 批量载荷里十几行并排，靠它分得开。
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
}
