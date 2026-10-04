using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Reports;

namespace VeloxDev.Serialization.Benchmarks;

/// <summary>
/// Writes the Markdown report a run leaves behind: the environment it was measured on, the document sizes, one
/// table per data magnitude, and the notes that say what the numbers do and do not mean.
/// </summary>
/// <remarks>
/// <para>
/// Built from BenchmarkDotNet's structured summaries rather than from its console output, so a change of console
/// layout cannot silently produce an empty report.
/// </para>
/// <para>
/// The notes are part of the report on purpose. These numbers are easy to quote out of context — they are measured
/// in-process, on one corpus, with the three serializers configured to preserve references — and a table without
/// that paragraph invites a conclusion the table does not support.
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
        AppendNotes(text, summaries, measured);

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

            // 只有归档那个类跑过时（`--filter "*SerializationBenchmarks*"`）文档大小就没量到 —— 那时写「—」而不是 0。
            int? archiveSize = null, stjSize = null, newtonsoftSize = null;
            if (DocumentSizes.All.TryGetValue(group.Key, out var sizes))
            {
                archiveSize = sizes.Archive;
                stjSize = sizes.Stj;
                newtonsoftSize = sizes.Newtonsoft;
            }

            text.AppendLine($"## {Scales.NameOf(group.Key)} · {group.Key:N0} 节点");
            text.AppendLine();
            text.AppendLine("| 序列化器 | 写 · 耗时 | 读 · 耗时 | 写 · 分配 | 读 · 分配 | 文档大小 |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

            Row("VeloxDev（本仓库）", ours: true, ourWrite, ourRead, archiveSize);
            Row("System.Text.Json", ours: false, Find(reports, "Stj_Serialize"), Find(reports, "Stj_Deserialize"), stjSize);
            Row("System.Text.Json（源生成）", ours: false, write: Find(reports, "StjSourceGen_Serialize"), read: null, size: stjSize);
            Row("Newtonsoft.Json", ours: false, Find(reports, "Nst_Serialize"), Find(reports, "Nst_Deserialize"), newtonsoftSize);

            text.AppendLine();

            void Row(string label, bool ours, BenchmarkReport? write, BenchmarkReport? read, int? size)
            {
                text.AppendLine(
                    $"| {Bold(label, ours)} | {Time(write, ourWrite, ours)} | {Time(read, ourRead, ours)} " +
                    $"| {Storage(write, ours)} | {Storage(read, ours)} | {Chars(size, ours)} |");
            }
        }

        text.AppendLine("**粗体行 = 本仓库。** 「文档大小」是字符数，不是字节；括号里是相对本仓库同方向的倍数。");
        text.AppendLine("三家的成员集不同（归档写生成契约，另外两家写公开面），所以差距里有一部分是「写得少」。");
        text.AppendLine("`System.Text.Json（源生成）` 与上一行是同一个序列化器、同一份文档，只是元数据来自源生成而不是反射。");
        text.AppendLine($"System.Text.Json 的「读」在本语料上不可用 —— 见备注。分配量若整列是「—」，说明这一项没取到（键名以 `{AllocatedMetric}` 为准）。");
        text.AppendLine();
    }

    private static string Bold(string value, bool bold) => bold ? $"**{value}**" : value;

    private static string Time(BenchmarkReport? report, BenchmarkReport? ourReport, bool bold)
    {
        if (report is null) return "—";

        var text = Milliseconds(MeanOf(report));
        if (!bold && ourReport is not null) text += $"（{Ratio(MeanOf(ourReport), MeanOf(report))}）";

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

    private static void AppendNotes(StringBuilder text, IEnumerable<Summary> summaries, IReadOnlyList<BenchmarkReport> measured)
    {
        var skipped = summaries
            .SelectMany(static summary => summary.Reports)
            .Where(static report => report.ResultStatistics is null)
            .Select(MethodOf)
            .Distinct()
            .ToList();

        text.AppendLine("## 备注");
        text.AppendLine();
        text.AppendLine("**这些数字是什么**：同一个对象图过三家序列化器，三家都开着引用保留");
        text.AppendLine("（`ReferenceHandler.Preserve` / `PreserveReferencesHandling.Objects`，Newtonsoft 另加 `TypeNameHandling.Auto`）");
        text.AppendLine("—— 归档对每个对象都写 `$id`、对多态成员写 `$type`，不对齐这两项就是拿不同的活来比。");
        text.AppendLine();
        text.AppendLine("**进程内测量**：共享 JIT 与 GC，绝对值不如独立进程干净。它够用来做**同一台机器上的前后对比**，");
        text.AppendLine("不要把它当成可对外引用的绝对数字。跨会话比之前先看上面的「测量环境」是不是同一台机器。");
        text.AppendLine();
        text.AppendLine("**不能推广到所有场景**：语料是一棵带引用保留、带多态、还带接口键嵌套字典的工作流树 ——");
        text.AppendLine("那正是这套格式存在的理由。换成「五个属性、不开引用保留的 POCO」，System.Text.Json 会近得多。");
        text.AppendLine();
        text.AppendLine("**三家的配置差异**：System.Text.Json 的源生成在这一档与反射持平（成本在引用保留与图的形状上，");
        text.AppendLine("不在元数据查找上），所以两者都列出来。");
        text.AppendLine();
        text.AppendLine("**System.Text.Json 的默认设置在语料上会失败**，两处都实测过：它写不了含 NaN/±Infinity 的图");
        text.AppendLine("（要 `AllowNamedFloatingPointLiterals`，归档把非有限值写成字符串），也读不了主构造器类");
        text.AppendLine("（`Offset(double left, double top)` 暴露的是 `Horizontal`/`Vertical`，`IncludeFields` 也救不了）——");
        text.AppendLine("要用它读这套库自己的图，得先给库的类型加注解或写转换器。");

        if (skipped.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"**本次没有产出的用例**：{string.Join("、", skipped.Select(static name => $"`{name}`"))}。");
        }

        text.AppendLine();
        text.AppendLine("**这一步量不到什么**：语料没有条件写出、没有 `EnumName`、没有必填成员、没有数组，");
        text.AppendLine("所以新特性的开销这里看不见 —— 它们只在文档真的用到时才多走一步。要量它们，得先给 `Corpus` 加形状。");
        text.AppendLine();
        text.AppendLine("**自带一处自校**：同一个归档写被两个类各量了一次（`Serialize` 与 `Archive_Serialize`），");
        text.AppendLine("两张表的这一对互校就能看出这一档的噪声有多大：");

        AppendSelfCheck(text, measured);
        text.AppendLine();
    }

    /// <summary>
    /// Prints how far the two measurements of the archive's write drifted apart, per scale.
    /// </summary>
    /// <remarks>
    /// The same code, measured twice, differing is the run telling you how much of the table is heap state. It
    /// matters most at the largest scale: a 30 000-node document is 40 MB (and Newtonsoft's is 190 MB), so those
    /// cases are dominated by large-object and Gen2 behaviour rather than by the serializer.
    /// </remarks>
    private static void AppendSelfCheck(StringBuilder text, IReadOnlyList<BenchmarkReport> measured)
    {
        var byScale = measured
            .Select(report => (Report: report, Nodes: NodeCountOf(report)))
            .Where(static pair => pair.Nodes > 0)
            .GroupBy(static pair => pair.Nodes)
            .OrderBy(static group => group.Key);

        foreach (var group in byScale)
        {
            var a = group.FirstOrDefault(pair => MethodOf(pair.Report) == "Serialize");
            var b = group.FirstOrDefault(pair => MethodOf(pair.Report) == "Archive_Serialize");

            if (a.Report is null || b.Report is null) continue;

            var low = Math.Min(MeanOf(a.Report), MeanOf(b.Report));
            var high = Math.Max(MeanOf(a.Report), MeanOf(b.Report));
            if (low <= 0d) continue;

            var drift = (high / low - 1d) * 100d;

            text.AppendLine(
                $"- {Scales.NameOf(group.Key)}（{group.Key:N0} 节点）：{Milliseconds(low)} vs {Milliseconds(high)}，差 {drift:N1}%");
        }

        text.AppendLine();
        text.AppendLine("差得越多，这一档越该往后放 —— 文档越大，量到的越接近 GC 而不是序列化器。");
        text.AppendLine("**要做决定时以「大」那一列为准**，「超大」只当指示。");
    }

    private static int NodeCountOf(BenchmarkReport report)
        => report.BenchmarkCase.Parameters.Items
            .Where(static parameter => parameter.Name == "NodeCount")
            .Select(static parameter => parameter.Value)
            .OfType<int>()
            .DefaultIfEmpty(0)
            .First();

    private static string MethodOf(BenchmarkReport report) => report.BenchmarkCase.Descriptor.WorkloadMethod.Name;

    private static string ClassNameOf(BenchmarkReport report) => report.BenchmarkCase.Descriptor.Type.Name;

    private static double MeanOf(BenchmarkReport report) => report.ResultStatistics?.Mean ?? 0d;

    private static double StandardDeviationOf(BenchmarkReport report) => report.ResultStatistics?.StandardDeviation ?? 0d;

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
