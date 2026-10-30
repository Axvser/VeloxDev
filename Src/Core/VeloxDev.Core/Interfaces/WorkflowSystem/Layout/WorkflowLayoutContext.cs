namespace VeloxDev.WorkflowSystem;

/// <summary>The execution environment a layout pipeline hands to each stage.</summary>
/// <remarks>
/// A stage may run on a worker thread, so this object carries cancellation, parallelisation and the optional
/// observers — never a model, a view or a synchronization context.
/// </remarks>
public sealed class WorkflowLayoutContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="cancellationToken">Cancels a solve between bands and between stages.</param>
    /// <param name="enableParallelism">Whether stages may fan bands out to the thread pool.</param>
    /// <param name="seed">A seed for any randomised tie-breaking a future stage may use.</param>
    /// <param name="metricSink">An optional receiver for named measurements.</param>
    /// <param name="diagnosticSink">An optional receiver for human-readable notes.</param>
    public WorkflowLayoutContext(
        CancellationToken cancellationToken = default,
        bool enableParallelism = true,
        int seed = 0,
        Action<string, double>? metricSink = null,
        Action<string>? diagnosticSink = null)
    {
        CancellationToken = cancellationToken;
        EnableParallelism = enableParallelism;
        Seed = seed;
        MetricSink = metricSink;
        DiagnosticSink = diagnosticSink;
    }

    /// <summary>Cancels a solve between bands and between stages.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Whether stages may fan bands out to the thread pool.</summary>
    public bool EnableParallelism { get; }

    /// <summary>A seed for any randomised tie-breaking a future stage may use.</summary>
    public int Seed { get; }

    /// <summary>An optional receiver for named measurements.</summary>
    public Action<string, double>? MetricSink { get; }

    /// <summary>An optional receiver for human-readable notes.</summary>
    public Action<string>? DiagnosticSink { get; }

    // 管线在一次 Solve 内收集阶段报告，最后原样放进 solution.Diagnostics；ResetDiagnostics 在入口清空，
    // 所以同一个 context 跨多次求解时不会把上一次的备注带进来。
    private readonly List<string> _collectedDiagnostics = [];

    internal IReadOnlyList<string> CollectedDiagnostics => _collectedDiagnostics;

    internal void ResetDiagnostics() => _collectedDiagnostics.Clear();

    /// <summary>Reports one named measurement when a sink is attached.</summary>
    /// <param name="name">The measurement name.</param>
    /// <param name="value">The measured value.</param>
    public void Report(string name, double value) => MetricSink?.Invoke(name, value);

    /// <summary>Records one diagnostic note and forwards it to <see cref="DiagnosticSink"/>.</summary>
    /// <param name="message">The note.</param>
    public void ReportDiagnostic(string message)
    {
        _collectedDiagnostics.Add(message);
        DiagnosticSink?.Invoke(message);
    }
}
