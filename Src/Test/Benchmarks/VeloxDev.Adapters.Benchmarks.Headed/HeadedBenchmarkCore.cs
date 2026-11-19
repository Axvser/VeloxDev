namespace VeloxDev.Adapters.Benchmarks.Headed;

/// <summary>The host-agnostic run loop: warmup, measured iterations, report.</summary>
public static class HeadedBenchmarkCore
{
    /// <summary>Runs one headed benchmark against <paramref name="host"/> and writes the report.</summary>
    /// <param name="options">The scenario to run.</param>
    /// <param name="host">The adapter host that owns the real window and the pipeline.</param>
    /// <returns>The rendered report and the written artifact paths.</returns>
    /// <remarks>
    /// The caller owns <paramref name="host"/> and must dispose it, on the UI thread, after this task
    /// completes. The method must be awaited from that same UI thread so every host call lands there.
    /// </remarks>
    public static async Task<HeadedRunResult> ExecuteAsync(HeadedOptions options, IHeadedBenchmarkHost host)
    {
        for (var warmup = 0; warmup < options.WarmupIterations; warmup++)
        {
            await RunIterationAsync(host);
        }

        var iterations = new List<HeadedIteration>(options.Iterations);
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            iterations.Add(await RunIterationAsync(host));
        }

        var result = HeadedReport.Write(options, iterations);
        Console.WriteLine(result.Markdown);
        Console.WriteLine($"\nRaw results: {result.JsonPath}");
        return result;
    }

    private static async Task<HeadedIteration> RunIterationAsync(IHeadedBenchmarkHost host)
    {
        await host.ResetScenarioAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initial = await host.MeasureInitialAsync();
        var scroll = await host.MeasureScrollAsync();
        var full = await host.MeasureFullAsync();
        return new HeadedIteration(initial, scroll, full);
    }
}
