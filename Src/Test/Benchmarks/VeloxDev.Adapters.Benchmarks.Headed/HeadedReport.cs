using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VeloxDev.Adapters.Benchmarks.Headed;

/// <summary>One measured iteration: initial materialization, the scroll sweep, and full visibility.</summary>
/// <param name="Initial">The step that moves from the reset viewport to the content origin.</param>
/// <param name="Scroll">One snapshot per sweep step.</param>
/// <param name="Full">The step that exposes the full content bounds.</param>
public sealed record HeadedIteration(
    PipelineSnapshot Initial,
    IReadOnlyList<PipelineSnapshot> Scroll,
    PipelineSnapshot Full);

/// <summary>The artifacts written by one headed benchmark run.</summary>
/// <param name="Markdown">The rendered report.</param>
/// <param name="MarkdownPath">The Markdown file path.</param>
/// <param name="JsonPath">The raw JSON file path.</param>
public sealed record HeadedRunResult(string Markdown, string MarkdownPath, string JsonPath);

/// <summary>Writes the headed benchmark's Markdown and JSON artifacts.</summary>
public static class HeadedReport
{
    /// <summary>Renders the report and writes both artifacts next to the entry project.</summary>
    /// <param name="options">The scenario that produced <paramref name="iterations"/>.</param>
    /// <param name="iterations">The measured iterations, excluding warmup.</param>
    /// <returns>The report text and the written paths.</returns>
    public static HeadedRunResult Write(HeadedOptions options, IReadOnlyList<HeadedIteration> iterations)
    {
        var markdown = BuildMarkdown(options, iterations);
        var directory = options.OutputPath ?? Path.Combine(FindEntryProjectDirectory(), "BenchmarkDotNet.Artifacts", "view-pipeline");
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var stem = $"headed-{options.Adapter}-{stamp}";
        var markdownPath = Path.Combine(directory, stem + ".md");
        var jsonPath = Path.Combine(directory, stem + ".json");
        File.WriteAllText(markdownPath, markdown, Encoding.UTF8);
        File.WriteAllText(jsonPath, BuildJson(options, iterations), Encoding.UTF8);
        return new HeadedRunResult(markdown, markdownPath, jsonPath);
    }

    private static string BuildMarkdown(HeadedOptions options, IReadOnlyList<HeadedIteration> iterations)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Headed view-pipeline benchmark — {options.Adapter} / {options.Policy}");
        builder.AppendLine();
        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine("| | |");
        builder.AppendLine("|---|---|");
        builder.AppendLine($"| Runtime | `{Escape(RuntimeInformation.FrameworkDescription)}` |");
        builder.AppendLine($"| OS | `{Escape(RuntimeInformation.OSDescription)}` |");
        builder.AppendLine($"| Architecture | `{RuntimeInformation.ProcessArchitecture}` |");
        builder.AppendLine($"| CPU count | {Environment.ProcessorCount} |");
        builder.AppendLine($"| Machine | `{Escape(Environment.MachineName)}` |");
        builder.AppendLine();
        builder.AppendLine("## Scenario");
        builder.AppendLine();
        builder.AppendLine("| | |");
        builder.AppendLine("|---|---|");
        builder.AppendLine($"| Nodes | {options.Nodes} |");
        builder.AppendLine($"| Links | {options.Links} |");
        builder.AppendLine($"| Viewport | {Number(options.ViewportWidth)} × {Number(options.ViewportHeight)} |");
        builder.AppendLine($"| Node size | {Number(options.NodeWidth)} × {Number(options.NodeHeight)} |");
        builder.AppendLine($"| Spatial cell | {Number(options.CellSize)} |");
        builder.AppendLine($"| Policy | {options.Policy} |");
        builder.AppendLine($"| Initial batch size / time budget | {options.BatchSize} / {Number(options.FrameBudgetMilliseconds)} ms |");
        builder.AppendLine($"| Scroll steps | {options.ScrollSteps} |");
        builder.AppendLine($"| Warmup / measured iterations | {options.WarmupIterations} / {options.Iterations} |");
        builder.AppendLine();
        builder.AppendLine("## Results");
        builder.AppendLine();
        builder.AppendLine("| Phase | Median | P95 | Allocated | Gen0/1/2 | Active | Pending | Recycled | Created | Reused | Destroyed | Visible |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        AppendRow(builder, "Initial materialization", iterations.Select(item => item.Initial).ToArray());
        var scroll = iterations.SelectMany(item => item.Scroll).ToArray();
        AppendRow(builder, "Scroll step", scroll);
        AppendRow(
            builder,
            "Scroll sweep final",
            iterations.Select(item => item.Scroll.Count == 0 ? item.Initial : item.Scroll[^1]).ToArray());
        AppendRow(builder, "Full visibility", iterations.Select(item => item.Full).ToArray());
        builder.AppendLine();
        builder.AppendLine("`Created / Reused / Destroyed` are cumulative pool counters; for the sweep-final row they are read from the last step of each iteration.");
        builder.AppendLine();
        builder.AppendLine("## Method");
        builder.AppendLine();
        builder.AppendLine("- A real adapter window is shown; each measurement sets `TreeHelper.Viewport`, waits for the pool's pending batches to drain on that adapter's own scheduler, then for one host render/layout turn.");
        builder.AppendLine("- `Initial materialization` starts from an empty viewport; the always-present `VirtualLink` is materialized during reset and is not part of the measured interval.");
        builder.AppendLine("- `Scroll step` measures one viewport move across the diagonal sweep; `Scroll sweep final` reports the state after the last step.");
        builder.AppendLine("- `Created` counts views the factory actually constructed; `Reused` counts views dequeued from the pool.");
        builder.AppendLine("- Timings are UI-thread wall time including layout quiescence, not GPU frame time. Median and P95 are taken across measured iterations (warmup is discarded).");
        builder.AppendLine("- Run one adapter per process. Do not run `dotnet test` concurrently with an optimized benchmark build; see `Src/Test/README.md`.");
        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, string name, PipelineSnapshot[] snapshots)
    {
        if (snapshots.Length == 0)
        {
            return;
        }

        builder.AppendLine(
            $"| {name} | {Ms(Median(snapshots.Select(item => item.ElapsedTicks)))} | {Ms(P95(snapshots.Select(item => item.ElapsedTicks)))} | " +
            $"{Mb(Median(snapshots.Select(item => item.AllocatedBytes)))} | " +
            $"{Median(snapshots.Select(item => (long)item.Gen0))}/{Median(snapshots.Select(item => (long)item.Gen1))}/{Median(snapshots.Select(item => (long)item.Gen2))} | " +
            $"{Median(snapshots.Select(item => (long)item.ActiveViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.PendingViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.RecycledViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.CreatedViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.ReusedViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.DestroyedViews))} | " +
            $"{Median(snapshots.Select(item => (long)item.VisibleItems))} |");
    }

    private static string BuildJson(HeadedOptions options, IReadOnlyList<HeadedIteration> iterations)
    {
        var payload = new
        {
            Adapter = options.Adapter,
            Scenario = new
            {
                options.Nodes,
                options.Links,
                options.ViewportWidth,
                options.ViewportHeight,
                options.NodeWidth,
                options.NodeHeight,
                options.CellSize,
                options.Policy,
                options.BatchSize,
                options.FrameBudgetMilliseconds,
                options.ScrollSteps,
                options.WarmupIterations,
                options.Iterations,
                options.Seed,
            },
            Environment = new
            {
                Runtime = RuntimeInformation.FrameworkDescription,
                OS = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
            },
            Iterations = iterations.Select(iteration => new
            {
                Initial = iteration.Initial,
                Scroll = iteration.Scroll,
                Full = iteration.Full,
            }),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static double Median(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        return ordered.Length == 0 ? 0d : ordered[ordered.Length / 2];
    }

    private static double P95(IEnumerable<long> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return 0d;
        }

        var index = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95d) - 1);
        return ordered[index];
    }

    private static string Ms(double ticks)
        => (ticks * 1000d / System.Diagnostics.Stopwatch.Frequency).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Mb(double bytes)
        => (bytes / 1024d / 1024d).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    // 产物默认落在入口工程目录，与从哪个目录启动无关；未来 WinUI/MAUI 宿主沿用同一条规则。
    private static string FindEntryProjectDirectory()
    {
        var projectName = Assembly.GetEntryAssembly()?.GetName().Name;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(projectName))
        {
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, projectName + ".csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }
}
