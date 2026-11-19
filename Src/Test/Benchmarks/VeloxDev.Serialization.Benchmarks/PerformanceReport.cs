using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Reports;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// Writes the Markdown report a run leaves behind: the environment it was measured on, one table per data
/// magnitude, the self-check that says how noisy each magnitude was, and the chapter describing what the format
/// supports.
/// </summary>
/// <remarks>
/// <para>
/// Built from BenchmarkDotNet's structured summaries rather than from its console output, so a change of console
/// layout cannot silently produce an empty report.
/// </para>
/// <para>
/// The chapter at the end is the one part that does not move with the numbers, and it is the part a reader comes
/// back to — so it is a constant, written once, rather than anything derived from the run.
/// </para>
/// </remarks>
internal static class PerformanceReport
{
    /// <summary>The name MemoryDiagnoser gives the allocation metric, as BenchmarkDotNet spells it.</summary>
    private const string AllocatedMetric = "Allocated Memory";

    /// <summary>Writes the report and returns the path it landed at.</summary>
    /// <param name="summaries">What the run produced, one per benchmark class.</param>
    /// <returns>The absolute path of the report.</returns>
    internal static string Write(IEnumerable<Summary> summaries)
    {
        var measured = summaries
            .SelectMany(static summary => summary.Reports)
            .Where(static report => report.ResultStatistics is not null)
            .ToList();

        var text = new StringBuilder();
        text.AppendLine("# 归档序列化 · 性能对比报告");
        text.AppendLine();
        text.AppendLine($"生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine();

        AppendEnvironment(text);
        AppendScales(text, measured);
        AppendSelfCheck(text, summaries, measured);
        AppendSurface(text);

        Directory.CreateDirectory(Artifacts.Path);
        var path = System.IO.Path.Combine(Artifacts.Path, Artifacts.ReportFileName);
        File.WriteAllText(path, text.ToString());

        Console.WriteLine();
        Console.WriteLine(text.ToString());
        return path;
    }

    private static void AppendEnvironment(StringBuilder text)
    {
        // 硬件与运行时：数字要跨会话比就必须带上它们，否则「和上次比」这句话兑现不了。
        text.AppendLine("## 测量环境");
        text.AppendLine();
        text.AppendLine("| 项 | 值 |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| CPU | {ProcessorName()} |");
        text.AppendLine($"| 逻辑核 | {Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)} |");
        text.AppendLine($"| 内存 | {Gigabytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)} GB（GC 可见的可用量） |");
        text.AppendLine($"| 系统 | {RuntimeInformation.OSDescription} |");
        text.AppendLine($"| 运行时 | {RuntimeInformation.FrameworkDescription} |");
        text.AppendLine($"| 进程架构 | {RuntimeInformation.ProcessArchitecture} |");
        text.AppendLine($"| 工具链 | InProcessEmitToolchain，{BenchmarkConfig.Warmups} warmup + {BenchmarkConfig.Iterations} iterations，LaunchCount 1 |");
        text.AppendLine("| 耗时怎么取 | **中位数**（BenchmarkDotNet 已先丢掉上侧离群点）。不取均值：这套测量天然右偏 —— GC 与 LOH 只会往**上**加时间，均值会被尖峰拉高，中位数不会 |");
        text.AppendLine("| 分配怎么取 | 每次操作的分配总量。它是确定性量，不是平均出来的 |");
        text.AppendLine($"| 诊断器 | MemoryDiagnoser（「分配」是 GC 可见的托管分配量，键名 `{AllocatedMetric}`） |");
        text.AppendLine();
    }

    /// <summary>
    /// One table per data magnitude. Each row is a serializer; time and storage sit side by side; this library's
    /// row is set in bold.
    /// </summary>
    /// <remarks>
    /// Storage belongs in the same table as the time: allocation does not move with JIT progress, so it is the
    /// column worth believing, and a reader should not have to join two tables to see that. The ratio in
    /// parentheses is against this library's own number in the same direction — the only comparison the rows
    /// support, since the three write different member sets.
    /// </remarks>
    private static void AppendScales(StringBuilder text, IReadOnlyList<BenchmarkReport> measured)
    {
        var byScale = measured
            .Select(report => (Report: report, Nodes: NodeCountOf(report)))
            .Where(static pair => pair.Nodes > 0)
            .GroupBy(static pair => pair.Nodes)
            .OrderBy(static group => group.Key)
            .ToList();

        if (byScale.Count == 0)
        {
            text.AppendLine("## 结果");
            text.AppendLine();
            text.AppendLine("_本次运行没有产出可用的数据。_");
            text.AppendLine();
            return;
        }

        foreach (var group in byScale)
        {
            var reports = group.Select(static pair => pair.Report).ToList();
            var ourWrite = Find(reports, "Archive_Serialize") ?? Find(reports, "Serialize");
            var ourRead = Find(reports, "Archive_Deserialize") ?? Find(reports, "Deserialize");

            // 这一档的噪声先算出来，再让每一格用它。它是「这个倍率算不算数」的唯一答案，
            // 而且只由这一档自己的重复测量算得出来 —— 不能从别的当量借，也不能事后补。
            var noise = DriftIn(reports);

            // 只有归档那个类跑过时（`--filter "*SerializationBenchmarks*"`）文档大小就没量到 —— 那时写「—」而不是 0。
            int? archiveSize = null, stjSize = null, stjSourceGenSize = null, newtonsoftSize = null;
            if (DocumentSizes.All.TryGetValue(group.Key, out var sizes))
            {
                archiveSize = sizes.Archive;
                stjSize = sizes.Stj;
                stjSourceGenSize = sizes.StjSourceGen;
                newtonsoftSize = sizes.Newtonsoft;
            }

            text.AppendLine($"## {Scales.NameOf(group.Key)} · {group.Key:N0} 节点");
            text.AppendLine();
            text.AppendLine("| 序列化器 | 写 · 耗时 | 读 · 耗时 | 写 · 分配 | 读 · 分配 | 文档大小 |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

            Row("VeloxDev（本仓库）", ours: true, ourWrite, ourRead, archiveSize);
            Row("System.Text.Json", ours: false, Find(reports, "Stj_Serialize"), Find(reports, "Stj_Deserialize"), stjSize);
            Row("System.Text.Json（源生成）", ours: false, Find(reports, "StjSourceGen_Serialize"), Find(reports, "StjSourceGen_Deserialize"), stjSourceGenSize);
            Row("Newtonsoft.Json", ours: false, Find(reports, "Nst_Serialize"), Find(reports, "Nst_Deserialize"), newtonsoftSize);

            text.AppendLine();

            if (noise is { } band)
            {
                text.AppendLine($"**这一档的噪声约 ±{band:N0}%** —— 同一个归档写被两个类各量了一次，两者之差。");
                text.AppendLine("这就是这张表的读数精度：**小于它的倍率标了「噪声内」**，那不是结论，是抖动。");
                text.AppendLine("「分配」与「文档大小」是确定性量，不受这条限制 —— 同一份输入每次都得到同一个数。");
            }

            text.AppendLine();

            void Row(string label, bool ours, BenchmarkReport? write, BenchmarkReport? read, int? size)
            {
                text.AppendLine(
                    $"| {Bold(label, ours)} | {Time(write, ourWrite, ours, noise)} | {Time(read, ourRead, ours, noise)} " +
                    $"| {Storage(write, ours)} | {Storage(read, ours)} | {Chars(size, ours)} |");
            }
        }

        text.AppendLine("**粗体行 = 本仓库。** 「文档大小」是字符数，不是字节；括号里是相对本仓库同方向的倍数。");
        text.AppendLine("耗时一律取**中位数**（理由见「测量环境」）；标了「噪声内」的倍率落在这一档的读数精度之内 —— 那不是结论，是抖动。");
        text.AppendLine("三家的成员集不同（归档写生成契约，另外两家写公开面），所以差距里有一部分是「写得少」。");
        text.AppendLine();
        text.AppendLine("**文档为什么小这么多 —— 量过，三个原因，按大小排**（小档 133 K vs STJ 795 K vs NST 650 K）：");
        text.AppendLine();
        text.AppendLine("1. **成员写得少 5.2×**（4,070 vs 20,982 个成员）。归档写**生成契约** —— 一份策展过的成员集；");
        text.AppendLine("   另外两家反射**整个公开面**，于是把命令、Helper、Input、EventContext、`IsBusy` / `PendingCount`");
        text.AppendLine("   这类 UI 管道也写进文档（那两个成员在 STJ 的文档里各出现 1,922 次，在归档里 0 次）。**这一项占大头。**");
        text.AppendLine("2. **`$type` 只在必要时写**（303 vs 2,529）：归档只在**运行期类型 ≠ 声明类型**时写，另外两家对每个");
        text.AppendLine("   多态位置都盖一个。这一项的字符开销 STJ 139 K、NST 170 K，归档 24 K。");
        text.AppendLine("3. **`$id` 只在必要时写**（715 vs 3,753）：STJ 给**每个对象和每个集合**都发 id（所以还多出 507 个 `$values`），");
        text.AppendLine("   归档与 NST 只发给被引用到的。这一项 STJ 花 105 K 字符，归档 17 K。");
        text.AppendLine();
        text.AppendLine("缩进把上面每一条都放大了（5,486 行 vs 25,741 行），但**它不是原因**：把空白全部删掉，");
        text.AppendLine("归档仍是 69 K vs STJ 395 K —— 差的是内容，不是排版。");
        text.AppendLine();
        text.AppendLine("`System.Text.Json（源生成）` 与上一行是同一个序列化器、同一份文档（「文档大小」那列相等就是证据），");
        text.AppendLine("只是元数据**优先**来自源生成；多态契约那几种类型源生成答不上（它要靠类型自己贴 `[JsonDerivedType]`，");
        text.AppendLine("而框架类型贴不了），那时落到反射。");
        text.AppendLine("**两行 System.Text.Json 都带桥接配置**（`StjSerializationBridge.cs`）：不多给它两处设置，它连读都读不了 ——");
        text.AppendLine("主构造器形参绑不上属性、接口成员没有判别符。两处都是**配置**，不是替它写的序列化代码；");
        text.AppendLine("反过来，Newtonsoft 读同一张图只需要开两个开关。");
        text.AppendLine($"分配量若整列是「—」，说明这一项没取到（指标键名以 `{AllocatedMetric}` 为准）。");
        text.AppendLine();
    }

    private static string Bold(string value, bool bold) => bold ? $"**{value}**" : value;

    /// <summary>
    /// One time cell: the statistic, and — for every row but this library's — how it compares to this library's.
    /// </summary>
    /// <remarks>
    /// A ratio that sits inside the scale's own noise is labelled instead of printed bare, because 1.17× at a
    /// scale that reproduces itself to ±30% is not a finding. Printed as a number it says "Newtonsoft is 1.17 times
    /// slower here"; what the run actually said is "these two are indistinguishable at what this run can resolve".
    /// </remarks>
    private static string Time(BenchmarkReport? report, BenchmarkReport? ourReport, bool bold, double? noise)
    {
        if (report is null) return "—";

        var text = Milliseconds(MedianOf(report));

        if (!bold && ourReport is not null && MedianOf(ourReport) > 0d)
        {
            var baseline = MedianOf(ourReport);
            var within = noise is { } band && Math.Abs(MedianOf(report) / baseline - 1d) <= band / 100d;
            var ratio = Ratio(baseline, MedianOf(report));

            text += within ? $"（{ratio}，噪声内）" : $"（{ratio}）";
        }

        return Bold(text, bold);
    }

    private static string Storage(BenchmarkReport? report, bool bold)
    {
        if (report is null) return "—";

        return report.Metrics.TryGetValue(AllocatedMetric, out var metric)
            ? Bold($"{metric.Value / 1024d / 1024d:N2} MB", bold)
            : "—";
    }

    private static string Chars(int? characters, bool bold)
        => characters is { } value ? Bold(value.ToString("N0", CultureInfo.InvariantCulture), bold) : "—";

    /// <summary>One named benchmark, or <see langword="null"/> when this run did not include it.</summary>
    private static BenchmarkReport? Find(IReadOnlyList<BenchmarkReport> reports, string method)
    {
        foreach (var report in reports)
        {
            if (MethodOf(report) == method) return report;
        }

        return null;
    }

    /// <summary>
    /// Prints how far the two measurements of the archive's write drifted apart, per scale.
    /// </summary>
    /// <remarks>
    /// The same code, measured twice, differing is the run telling you how much of the table is heap state. It
    /// matters most at the largest scale: a 30 000-node document is 40 MB (and Newtonsoft's is 190 MB), so those
    /// cases are dominated by large-object and Gen2 behaviour rather than by the serializer.
    /// </remarks>
    private static void AppendSelfCheck(
        StringBuilder text, IEnumerable<Summary> summaries, IReadOnlyList<BenchmarkReport> measured)
    {
        text.AppendLine("## 自校");
        text.AppendLine();
        text.AppendLine("同一个归档写被两个类各量了一次（`Serialize` 与 `Archive_Serialize`），两者之差就是这一档的噪声 ——");
        text.AppendLine("**每张表下面印的那个「噪声约 ±x%」就是它**。每组顺带给出两侧各自的四分位距，好分辨那个差是怎么来的：");
        text.AppendLine();

        var byScale = measured
            .Select(report => (Report: report, Nodes: NodeCountOf(report)))
            .Where(static pair => pair.Nodes > 0)
            .GroupBy(static pair => pair.Nodes)
            .OrderBy(static group => group.Key);

        foreach (var group in byScale)
        {
            var reports = group.Select(static pair => pair.Report).ToList();
            var a = Find(reports, "Serialize");
            var b = Find(reports, "Archive_Serialize");
            if (a is null || b is null) continue;

            var low = Math.Min(MedianOf(a), MedianOf(b));
            var high = Math.Max(MedianOf(a), MedianOf(b));
            if (low <= 0d) continue;

            text.AppendLine(
                $"- {Scales.NameOf(group.Key)}（{group.Key:N0} 节点）：{Milliseconds(low)} vs {Milliseconds(high)}，" +
                $"差 {(high / low - 1d) * 100d:N1}%；两侧各自的 IQR 是 {RelativeInterquartileOf(a):N1}% 与 {RelativeInterquartileOf(b):N1}%");
        }

        text.AppendLine();
        text.AppendLine("**怎么读**：第一个数（差）就是这一档的读数精度，也是每张表下印的那个数。后两个数是每一侧自己的抖动。");
        text.AppendLine("**差远大于 IQR** ⇒ 那个差来自两次测量之间的整体漂移（谁先跑、当时堆是什么样、JIT 到了哪一层），");
        text.AppendLine("不是某几次迭代的尖峰 —— **加迭代次数治不了它**，只能把它印出来让读者按它折算。");
        text.AppendLine("两者差不多大 ⇒ 这一档本来就抖。**哪一档最差每次都不一样**，所以永远看当次这一节给的数，别记上一次的。");

        var skipped = summaries
            .SelectMany(static summary => summary.Reports)
            .Where(static report => report.ResultStatistics is null)
            .Select(MethodOf)
            .Distinct()
            .ToList();

        if (skipped.Count > 0)
        {
            text.AppendLine($"本次没有产出的用例：{string.Join("、", skipped.Select(static name => $"`{name}`"))}。");
        }

        text.AppendLine();
    }

    /// <summary>Appends the chapter describing what the format supports: one rule, one snippet.</summary>
    /// <remarks>
    /// Nothing in it moves with the numbers, so it is written once as a constant rather than derived from the run,
    /// and it sits last because a reader arrives for the numbers and stays for the rules. Its newlines are
    /// normalised because a raw string literal carries the source file's line endings while every other line here
    /// goes through <see cref="StringBuilder.AppendLine"/> — the two agree on this machine and need not on the next.
    /// </remarks>
    private static void AppendSurface(StringBuilder text)
        => text.Append(Surface.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));

    // 行为说明，不是测量结果 —— 所以是常量而不是拼出来的表格。
    private const string Surface = """
        ## 归档序列化 · 行为与支持

        这一章与上面的数字无关，讲的是这套序列化本身。**每节只给用法与关键结论。**

        ### 一、什么类型进得了文档

        闭世界：**只有生成器为它编出了读写器的类型才写得出去**，没有反射、也没有兜底。四条路进得来：

        ```csharp
        using VeloxDev.Serialization;

        [Archivable]                                   // ① 普通文档类型自报家门（检查点就是这种）
        public partial class Doc { public string? Title { get; set; } }

        public partial class Model
        {
            [VeloxProperty] private int count;          // ② 带 [VeloxProperty] 的类本身就是根
        }

        [WorkflowBuilder.Node]                         // ③ 工作流组件：组件接口由另一个生成器补上，
        public partial class MyNode { }                //    所以这里认的是作者写下的那个特性

        [Archivable(typeof(Extra))]                    // ④ 顺带把走不到的类型点名收进来（可链式）
        public partial class Root { public Extra? Body { get; set; } }
        ```

        **能「走到」的比声明本身宽三条** —— 这三条是自动的，不需要写任何东西：

        ```csharp
        public partial class Zoo
        {
            [VeloxProperty] private Animal? pet;            // 声明成基类、装的是 Dog
            [VeloxProperty] private Dictionary<ISlot, int>? weights;
        }

        [Archivable]
        public partial class Host<T> where T : Instrument    // T 的约束那一族
        {
            public T? Value { get; set; }
        }
        ```

        | 规则 | 收进来的是 |
        | --- | --- |
        | 派生类向下展开 | 本程序集里 `Animal` 的派生类、`ISlot` 的实现类 —— 文档里的 `$type` 写的是**运行期类型** |
        | 字典的键与值同等 | 接口键写成键对象的引用 id，所以实现类必须有条目 |
        | 类型参数的约束 | `Instrument` 那一族（**只有带根标记的开放泛型**参与） |

        **深不深无所谓**：闭包是工作表算法，`Zoo → Animal → Dog → Collar → Fastener → Buckle` 一路跟到底。
        **跨程序集不行**：派生类 / 实现类只在同一个程序集里被自动收进来，别家的要自己贴 `[Archivable]`。

        ### 二、写与读

        ```csharp
        string json = model.Serialize();                    // 缩进
        string flat = model.Serialize(SerializationOptions.Create().WithCompact());
        Model back = json.Deserialize<Model>();             // 失败抛
        if (json.TryDeserialize<Model>(out var safe)) { }   // 失败返回 false，不抛

        byte[] utf8 = model.SerializeToUtf8Bytes();
        Model same = utf8.DeserializeFromUtf8Bytes<Model>();

        await model.SerializeToStreamAsync(stream);
        Model fromStream = stream.DeserializeFromStream<Model>();

        model.SerializeToTextWriter(writer);
        Model fromText = reader.DeserializeFromTextReader<Model>();
        ```

        这一套（`ViewModelSerializer`）全是**扩展方法**，写与读各有同步 / 异步两份。

        ### 三、选项

        ```csharp
        var options = SerializationOptions.Create()
            .WithCompact()                                  // 或 WithIndented()
            .WithExcludedPropertyTypes(typeof(Snapshot));   // 按**声明类型**整批排除

        model.Serialize(options);
        ```

        ### 四、哪些成员进文档

        默认规则：**public、有 public setter 的属性**，按声明顺序 —— 手写的在前，`[VeloxProperty]` 提升出来的按字段顺序在后，继承的再往后。

        ```csharp
        public partial class Model
        {
            [VeloxProperty] private int count;      // → Count
            public string? Name { get; set; }       // → Name
            public string? Computed => Name;        // 默认丢弃：没有 public setter
            private int scratch;                    // 默认丢弃：字段不单独进
        }
        ```

        **成员顺序是逐字节契约**，改它会打翻已存的文档。

        ### 五、`[Archive]` —— 只动它标的那一个成员

        ```csharp
        [Archive(ArchiveOptions.KeepProperty)] public string? Computed => Name;  // 写出去，读不回（没有 setter 可赋值）
        [Archive(ArchiveOptions.KeepField)]    private int hidden;               // 放行没有对应属性的字段
        [Archive(ArchiveOptions.IgnoreField)]  private int scratch;              // 整个排除
        [Archive(ArchiveOptions.ReName, "n")]  public int Number { get; set; }   // 文档里改叫 n
        [Archive(ArchiveOptions.EnumName)]     public Day Kind { get; set; }     // 枚举写值名（默认写底层整数）
        ```

        它**不重排、不改其余成员的取舍**。

        ### 六、STJ 的两个特性直接认

        用不着为了接入这套格式改写已经给 System.Text.Json 写的代码。

        ```csharp
        [JsonIgnore]                                                    // 排除
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]             // 显式放行
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]   // 条件写出
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]

        public required string Id { get; set; }                         // 或者 [JsonRequired]
        ```

        **必填成员缺了就读不过去**（抛）。必填与「排除」「条件写出」互斥 —— 三者一起用会让文档永远无法满足，生成器报错。

        ### 七、生命周期钩子

        BCL 那四个特性，沿基类链**先基后派生**：

        ```csharp
        [OnSerializing]   void BeforeWrite(StreamingContext context) { }
        [OnSerialized]    void AfterWrite() { }
        [OnDeserializing] void BeforeRead(StreamingContext context) { }
        [OnDeserialized]  void AfterRead() { }
        ```

        参数可以没有，也可以是一个 `StreamingContext`。**生成器调不到就报错** —— 钩子是用来改文档内容的，静默不跑等于悄悄改字节。

        ### 八、容器与键

        ```csharp
        public T[]?                            A { get; set; }
        public List<T>?                        B { get; set; }   // IList<T> / ICollection<T> / IReadOnlyList<T>
        public IReadOnlyCollection<T>?         C { get; set; }   // IEnumerable<T> / ObservableCollection<T>
        public HashSet<int>?                   D { get; set; }   // Queue<T> / Stack<T> 同一个白名单
        public byte[]?                         E { get; set; }   // 标量：写 base64 字符串，不是数字数组
        public Dictionary<string, T>?          F { get; set; }   // IDictionary / IReadOnlyDictionary 同
        ```

        键按声明类型分四种写法：

        | 键 | 写法 |
        | --- | --- |
        | `string` / `object` | 键文本直接当属性名，读回是同一个字符串 |
        | 枚举 | 写值名，读 `Enum.Parse(ignoreCase: true)` |
        | 其它 | 写读同为不变区域性 |
        | **接口** | 键写成**键对象的引用 id** —— 那个对象得在文档别处以完整对象出现过 |

        ### 九、泛型

        只有**封闭实例**才有条目，条目由**见过那个组合的那一边**发出：

        ```csharp
        public partial class Envelope<T>
        {
            public class Inner<U> { public string? Tag { get; set; } }
        }

        public partial class Model
        {
            [VeloxProperty] private Envelope<int>.Inner<string>? nested;   // 这个组合本程序集见过 → 有条目
        }
        ```

        `$type` 的名字**逐层带上实参**：`Envelope<int>.Inner<string>` 与挤平后的写法是两个类型，必须区分。

        ### 十、文档的拼写契约

        | 项 | 写法 |
        | --- | --- |
        | `$id` / `$type` | 对象的**前两个成员**；`$type` 是注册表的键（`Namespace.Type, Assembly`，泛型再带实参） |
        | 重复对象 | 写 `{"$ref":"n"}`；`$ref` **只向后指**，所以单遍前向读就够 |
        | `double` | 最短往返，整数值补 `.0` |
        | NaN / ±Infinity | 写成**字符串** |
        | 枚举 | 默认**底层整数**；要写名字用 `ArchiveOptions.EnumName` |
        | `DateTime` | 写 `"O"`，读带 `RoundtripKind` |
        | 非 ASCII | **不转义** |
        | 空容器 | `{}` / `[]` |

        ### 十一、失败长什么样

        | 情况 | 结果 |
        | --- | --- |
        | 写一个进不了闭世界的类型 | 抛 `MissingWriter`，错误信息自己说明原因 |
        | 读一个没有读写器的 `$type` | 抛 `MissingReader` |
        | 类型没有公开无参构造 | 写得出去；读时 `Create()` 抛 `NotSupportedException` |
        | 成员声明成 `object` | 能写；读回来降级成 `Dictionary<string, object?>` |

        **这是能裁剪的前提，不是缺陷** —— 正因为没有反射兜底，裁剪与 AOT 才安全。

        ### 十二、与别家序列化器的边界

        **与 System.Text.Json 形状不通；与 Newtonsoft.Json 近得多 —— 小的文档两个方向都能无损互读，大的不行。**
        下表是实测的，不是推断的：

        | | 归档 | System.Text.Json | Newtonsoft |
        | --- | --- | --- | --- |
        | `$type` 的值 | 注册表键 `Namespace.Type, Assembly`（泛型带实参） | `FullName`，无程序集 | **与归档同形**：`FullName, SimpleAssemblyName` |
        | 集合 | 裸数组 | `{"$id":…,"$values":[…]}` | 裸数组 |
        | 字典的 `$id` | **不写** | 写 | **写** |
        | 成员集 | **生成契约** | 反射公开面 | 反射公开面 |

        Newtonsoft 那一行不是巧合：`TypeNameAssemblyFormatHandling` 的默认值是 `Simple`，写的正是
        `Namespace.Type, AssemblyName` —— 与归档注册表键同一个形式。

        **互读实测**（四份冻结文档，读成模型后过 Newtonsoft 再喂回来）：`canvas-layout` 与 `compiled-graph`
        两个方向**都通**，而且 Newtonsoft 写完再读回归档、重新写出来，与冻结文档**逐字节相同**。
        `tree` 那份 Newtonsoft 读得进、**归档读不出**（`FormatException`）—— 而这一条**不是 `$type` 造成的**：
        把类型名关掉之后，它仍在同一个位置失败。

        **成员集才是那条不可逆的线**：`tree` 的 Newtonsoft 文档是归档的 **4.66×**（`$type` 出现 65 次对 7 次），
        而 `compiled-graph` 两边**一样大** —— 那份的公开面与生成契约恰好重合。差的全在这里少写的那些
        （命令、`Helper`、`EventContext`、`IsBusy` 这类 UI 管道），在别家的文档里有、在这里没有。

        **喂进来会怎样**：`$type` 进不了注册表 → 退回**声明类型** —— 声明类型是具体类时**静默丢多态**
        （读到的是基类实例、不报错），是接口或抽象时抛 `MissingReader`。System.Text.Json 认不出这里的
        `$type` 值，所以这条对它成立；Newtonsoft 的名字落得进注册表。

        **词表与形状这一层是可转换的**：枚举两边都是底层整数、`byte[]` 两边都是 base64、空容器两边都是
        `{}` / `[]`；几处标量拼写不同（`double` 的整数值这里补 `.0`、`DateTime` 这里写 `"O"`、非 ASCII 不转义）
        —— 都是文本差异，宽容的解析器吃得下。**不可逆的是成员集**，所以转换是有损的。

        **这是立场，不是缺陷**：闭世界与零反射是这套格式能裁剪、能 AOT 的前提，代价就是不与别家同源。
        库从未承诺过互操作。

        ### 十三、诊断

        | ID | 级别 | 说的什么 |
        | --- | --- | --- |
        | `VELOX_JSON_ARCH001` | Error | `[Archivable(typeof(…))]` 点名的类型发不出条目 |
        | `VELOX_JSON_MEMBER001` | Error | 成员声明用不了（不可达、`ReName` 没给名字、`[JsonIgnore]` 条件不认识…） |
        | `VELOX_JSON_MEMBER002` | Warning | `KeepField` 标在一个已经有对应属性的字段上 |
        | `VELOX_JSON_HOOK001` | Warning | 同一时刻挂了多个回调，只跑第一个 |
        | `VELOX_JSON_HOOK002` | Error | 回调生成代码调不到，或签名不对 |
        | `VELOX_JSON_GENERIC001` | Warning | 类型参数没有能落成类 / 接口的约束 |
        | `VELOX_JSON_INCLUDE001` | Info | **没有声明点名过**却被收进来的类型，原因写在消息里 |

        最后两条：`INCLUDE001` 只报「向下展开带进来的」那一类（它是唯一从源码上看不出来的），**全量清单**
        （类型名 + `$type` + 出处）在生成文件 `*_VeloxJson.g.cs` 的**文件头注释**里。

        ### 十四、关掉它

        ```xml
        <PropertyGroup>
          <VeloxJsonSerialization>false</VeloxJsonSerialization>
        </PropertyGroup>
        ```

        工作流领域的两个封装在 `VeloxDev.Core.Extension`：

        ```csharp
        string text = checkpoint.SerializeCheckpoint();
        ExecutionCheckpoint? back = text.DeserializeCheckpoint();

        string graph = compiled.SerializeCompiledGraph(includeTree: false);   // 默认不带整棵树
        CompiledGraph? restored = graph.DeserializeCompiledGraph();
        ```
        """;

    private static int NodeCountOf(BenchmarkReport report)
        => report.BenchmarkCase.Parameters.Items
            .Where(static parameter => parameter.Name == "NodeCount")
            .Select(static parameter => parameter.Value)
            .OfType<int>()
            .DefaultIfEmpty(0)
            .First();

    private static string MethodOf(BenchmarkReport report) => report.BenchmarkCase.Descriptor.WorkloadMethod.Name;

    private static string ClassNameOf(BenchmarkReport report) => report.BenchmarkCase.Descriptor.Type.Name;

    /// <summary>The statistic every time in this report is: the median, never the mean.</summary>
    /// <remarks>
    /// A throughput measurement is right-skewed by construction — the GC and the large object heap only ever add
    /// time, never take it away — and a mean over a right-skewed series reports the spikes as if they were the
    /// measurement. BenchmarkDotNet has already dropped the upper outliers by this point; the median is what keeps
    /// the rest of the spread from moving the number.
    /// </remarks>
    private static double MedianOf(BenchmarkReport report) => report.ResultStatistics?.Median ?? 0d;

    /// <summary>One measurement's own spread, as a percentage of its median.</summary>
    /// <remarks>
    /// Quartiles rather than the standard deviation, for the same reason the statistic is the median: a standard
    /// deviation is built from squared deviations, so one spike moves it further than the number it is meant to
    /// qualify.
    /// </remarks>
    private static double RelativeInterquartileOf(BenchmarkReport report)
    {
        var statistics = report.ResultStatistics;
        if (statistics is null || statistics.Median <= 0d) return 0d;

        return (statistics.Q3 - statistics.Q1) / statistics.Median * 100d;
    }

    /// <summary>How far apart this scale's two measurements of the same archive write were, in percent.</summary>
    /// <remarks>
    /// One operation, measured by two benchmark classes that share nothing but the process and the corpus. Their
    /// difference therefore bounds everything the table cannot control — which case ran first, what the heap looked
    /// like when it did, how far the JIT had tiered — so it is the number that says how large a ratio has to be
    /// before it means anything. Computed from medians, for the same reason the table is.
    /// </remarks>
    private static double? DriftIn(IReadOnlyList<BenchmarkReport> reports)
    {
        var a = Find(reports, "Serialize");
        var b = Find(reports, "Archive_Serialize");
        if (a is null || b is null) return null;

        var low = Math.Min(MedianOf(a), MedianOf(b));
        var high = Math.Max(MedianOf(a), MedianOf(b));
        if (low <= 0d) return null;

        return (high / low - 1d) * 100d;
    }

    private static string Milliseconds(double nanoseconds)
        => (nanoseconds / 1_000_000d).ToString("N3", CultureInfo.InvariantCulture) + " ms";

    private static string Ratio(double baseline, double value)
        => baseline <= 0d || value <= 0d ? "—" : (value / baseline).ToString("N2", CultureInfo.InvariantCulture) + "×";

    private static string Gigabytes(long bytes)
        => (bytes / 1024d / 1024d / 1024d).ToString("N1", CultureInfo.InvariantCulture);

    /// <summary>The CPU's marketing name, falling back to the identifier Windows exposes as an environment variable.</summary>
    /// <remarks>
    /// The registry is the only place Windows writes the brand string. <c>PROCESSOR_IDENTIFIER</c> gives
    /// <c>Intel64 Family 6 Model 141 Stepping 1</c>, which identifies the CPU just as uniquely but is harder to
    /// read back a year later — and this table exists to be read back.
    /// </remarks>
    private static string ProcessorName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                if (key?.GetValue("ProcessorNameString") is string brand && !string.IsNullOrWhiteSpace(brand))
                {
                    return brand.Trim();
                }
            }
            catch (Exception)
            {
                // 读不到就退回标识符：报告里少一分好看，不该让一次基准跑不完。
            }
        }

        var identifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        return string.IsNullOrWhiteSpace(identifier) ? "未知" : identifier;
    }
}
