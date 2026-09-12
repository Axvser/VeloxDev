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
    /// <summary>
    /// 把该适配器发布的每一个采样器各推一帧序列，把结果写成机器可读的载荷。
    /// </summary>
    /// <remarks>
    /// 这是采样式验收在真 app 里的落点：采样器要写的对象需要一个活着的框架运行时，而这里正是有运行时的进程。
    /// <para>
    /// 被写对象是 <see cref="SamplerSubject"/> —— 一个**真正在窗体里的控件**，每条采样器一条同类型的属性。
    /// 采样器直接写在它上面，载荷再从该属性读回，所以断言依据的是界面上那个控件实际持有的值。控件自己按属性
    /// 重绘，于是"画出来"这件事不需要另一套映射代码。
    /// </para>
    /// <para>
    /// 每条采样器由界面上的一个把手点击驱动，一次只跑一条 —— 验收侧因此走的是"点控件 → 触发处理函数 →
    /// 采样器写值 → 读回"这条真实的 UI 交互路径，而不是让 app 在启动时闷头算完一遍。
    /// </para>
    /// <para>
    /// 端点是工厂而不是实例：引用类型的端点若跨帧复用，一个就地改动端点的采样器会把后面的帧一起带偏，
    /// 而「不得改动交给 InsertFrame 的 start/end」正是库对采样器的硬约束 —— 每帧一对全新端点才看得见它。
    /// </para>
    /// </remarks>
    internal static class SamplerProbe
    {
        /// <summary>
        /// 每个采样器被推的缓动时间。与纯数据套件用同一组值，两边的失败信息才好对照；都是二进制可精确表示的
        /// 数，所以时间本身不引入误差。
        /// </summary>
        private static readonly double[] Times = [0d, 0.5d, 1d, 1.5d, -0.5d];

        /// <summary>
        /// 每条采样器的名字 —— 取自采样器类型名，所以把手令牌与载荷键两处说的是同一件事。
        /// </summary>
        internal static class Kinds
        {
            internal const string PaddingSampler = nameof(PaddingSampler);

            /// <summary>
            /// 索引器路径的两条：它们验的不是某个采样器，而是**路径能落到具体的槽上**。
            /// </summary>
            /// <remarks>
            /// 名字不再取自采样器类型名 —— 两条用的是同一个 SizeSampler，区分它们的是路径里那个下标。
            /// 相邻下标必须是两条不同的路径：索引器的 PropertyInfo 对每个下标都是同一个 "Item"，
            /// 下标不并入身份的话这两行会合成一个状态条目，一条动画静默盖掉另一条 —— 而批量那一路
            /// 会立刻报出来（表里的条目在帧报告里缺席）。
            /// </remarks>
            internal const string GradientStop0Color = "GradientStop0Color";

            internal const string GradientStop1Color = "GradientStop1Color";
        }

        /// <summary>一条采样器：把手令牌、它在被写控件上的属性，以及一对端点工厂。</summary>
        /// <param name="Property">
        /// 被写的属性名；带索引器的路径用它表达不了，那些条目的这一项是 null、改由 <paramref name="Path"/> 给出路径。
        /// </param>
        /// <param name="Path">
        /// 一条完整路径，用于"属性名"说不清的那些条目 —— 尤其是带索引器的：<c>Controls[0].Size</c>。
        /// </param>
        private sealed record ProbeSpec(
            string Name,
            string Description,
            string? Property,
            Func<ISampler> Create,
            Func<object> Start,
            Func<object> End,
            Func<TransitionProperty>? Path = null);

        /// <summary>一个产物读出来的样子：类型名 + 固定顺序的分量。</summary>
        /// <param name="TypeTag">产物的运行时类型名。认不出来的类型也照报，由测试侧去说"类型不对"。</param>
        /// <param name="Components">该类型的分量。认不出的类型没有分量，所以是空数组。</param>
        internal sealed record Measurement(string TypeTag, double[] Components);

        /// <summary>
        /// 顺序即界面顺序，也是套件点击的顺序。加一条采样器只需在这里加一行 —— 把手是从这张表生成的。
        /// 端点与 Samplers/WinFormsEntries.cs 里的一致。
        /// </summary>
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

        /// <summary>每条采样器的名字，也是它把手的令牌后缀。界面由它生成。</summary>
        internal static IReadOnlyList<string> SamplerNames { get; } = [.. Probes.Select(probe => probe.Name)];

        /// <summary>某一条的文字描述，给案例列表那一行用。</summary>
        internal static string Description(string samplerName) => Spec(samplerName).Description;

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

        /// <summary>这一条采样器写在被写控件的哪个属性上。</summary>
        /// <summary>这一条采样器写的路径 —— 单属性，或者一条带索引器的路径。</summary>
        /// <remarks>
        /// 每条只建一个实例。路径是状态字典的键，每次新建一个虽然按值相等，但索引实参来自闭包时未必相等；
        /// 缓存下来就没有这层疑问。
        /// </remarks>
        internal static TransitionProperty Path(string samplerName) => Paths[samplerName];

        private static readonly Dictionary<string, TransitionProperty> Paths =
            Probes.ToDictionary(
                static probe => probe.Name,
                static probe => probe.Path is null
                    ? TransitionProperty.FromProperty(typeof(SamplerSubject).GetProperty(probe.Property!)!)
                    : probe.Path());

        /// <summary>建一条 <c>this[i]</c>。下标是常量，所以这条路径的身份只由下标决定。</summary>
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

        /// <summary>这一条采样器的实例。每次都要新的：采样器本身可能带状态。</summary>
        internal static ISampler Create(string samplerName) => Spec(samplerName).Create();

        /// <summary>这一条采样器声明的起点。</summary>
        internal static object Start(string samplerName) => Spec(samplerName).Start();

        /// <summary>这一条采样器声明的终点。</summary>
        internal static object End(string samplerName) => Spec(samplerName).End();

        /// <summary>
        /// 被写控件上这个属性**此刻**持有的值，按分量读出来。
        /// </summary>
        /// <remarks>
        /// 真动画那一段时间靠它采样：采样当刻就把分量取成数字，绝不把值对象留到后面 ——
        /// 引用型产物每帧写的是同一个 scratch 实例、就地改，存下实例等于读到"后来"的状态。
        /// </remarks>
        internal static Measurement Read(SamplerSubject subject, string samplerName)
            => Measure(Path(samplerName).GetValue(subject));

        /// <summary>
        /// 这一行此刻的值是否**就是**它声明的起点。
        /// </summary>
        /// <remarks>
        /// 给顶栏那三个按钮用的可观测量。逐分量精确比较而不是带容差：重置是**把声明的那对端点原样写回去**，
        /// 所以"已经回到起点"必然是逐位相同，不需要容差去猜。离散型的那几条永远停在起点，于是它们恒为真 ——
        /// 这是它们应有的样子，不是漏报。
        /// </remarks>
        internal static bool MatchesStart(SamplerSubject subject, string samplerName)
            => SameComponents(Measure(Start(samplerName)).Components, Read(subject, samplerName).Components);

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
        /// 在被写控件上跑一条采样器的五个固定缓动时间，产出载荷文本，形如
        /// <c>v=1;seq=1;n=5;s.PaddingSampler.0=Padding,20,20,60,60;</c>。
        /// </summary>
        /// <param name="subject">这一格的在屏控件 —— 采样器写在它上面，值也从它读回。</param>
        /// <param name="samplerName">要跑的那条采样器，取 <see cref="SamplerNames"/> 里的名字。</param>
        /// <param name="sequence">激活次数，由调用方递增 —— 载荷靠它证明这一次是新的。</param>
        internal static string Run(SamplerSubject subject, string samplerName, long sequence)
            => $"v=1;seq={sequence};n={Times.Length};" + RunFrames(subject, samplerName);

        /// <summary>
        /// 只要那五帧的字段，不带头部 —— 批量载荷把每一行的这一段拼在一起（逐行载荷与它同一套字段名，
        /// 所以两边共用同一个解析器）。
        /// </summary>
        internal static string RunFrames(SamplerSubject subject, string samplerName)
        {
            var payload = new StringBuilder();

            for (var index = 0; index < Times.Length; index++)
            {
                payload.Append($"s.{samplerName}.{index}={Describe(Frame(subject, samplerName, Times[index]))};");
            }

            return payload.ToString();
        }

        /// <summary>
        /// 在被写控件上跑一条采样器的一帧，返回**从控件读回**的值。
        /// </summary>
        /// <remarks>
        /// 验收与演示台走的是同一个入口：载荷报的就是这个返回值，屏幕上那个控件持有的也是它 ——
        /// 界面上看到的和断言里读的必然是同一个数，不可能各说各话。
        /// </remarks>
        internal static object? Frame(SamplerSubject subject, string samplerName, double t)
        {
            var probe = Spec(samplerName);
            var property = Path(samplerName);

            // 每帧一对全新端点：端点实例跨帧复用会被采样器原地改动污染。
            object? working = null;
            probe.Create().InsertFrame(subject, property, ref working, probe.Start(), probe.End(), null, t);

            return property.GetValue(subject);
        }

        /// <summary>
        /// 把一个产物读成类型名 + 分量，顺序固定。
        /// </summary>
        /// <remarks>
        /// 打头带上类型名是有意的 —— 采样器一旦换了产物的类型，或者这里的分量顺序被改动，验收侧会立刻发现，
        /// 而不是把两种情况都读成"数值对不上"。
        /// <para>
        /// <b>认不出的类型不抛。</b> 这里读的是"控件此刻持有什么"，而"类型变了"本身就是要报给验收的异常之一；
        /// 在一个属性回调里抛出去只会把 demo 打挂，把异常变成一次崩溃。所以照实报类型名、分量留空，让测试侧去说。
        /// </para>
        /// </remarks>
        internal static Measurement Measure(object? value) => value switch
        {
            Padding padding => new("Padding", [padding.Left, padding.Top, padding.Right, padding.Bottom]),
            Color color => new("Color", [color.A, color.R, color.G, color.B]),
            null => new("null", []),
            _ => new(value.GetType().Name, []),
        };

        /// <summary>载荷里的规范文本：类型名打头，后面是逗号分隔的分量。</summary>
        private static string Describe(object? value)
        {
            var (typeTag, components) = Measure(value);
            return components.Length == 0 ? typeTag : $"{typeTag},{Vector(components)}";
        }

        /// <summary>往返格式 + 不变文化：文本要能无损地还原成同一个 double，且不受区域设置影响。</summary>
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
        /// 真动画那一段时间里对控件属性的采样累积。
        /// </summary>
        /// <remarks>
        /// 收的是 <see cref="Measurement"/> 里已经取成数字的分量，不是值对象（见 <see cref="Read"/>）。
        /// 非有限值单独计数、**不喂给 min/max** —— 一个 NaN 喂进去会把那个分量的包络永久粘住，
        /// 于是"有过 NaN"这件事就再也看不出来了。
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
            /// 这段动画的机器可读结果：控件属性这段时间里被写成了什么。
            /// </summary>
            /// <param name="sampler">跑的是哪条采样器。</param>
            /// <param name="sequence">点击序号，与 <c>over.conf</c> 共用 —— 载荷靠它证明这一份是新的。</param>
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
}
