using System;
using System.Collections.Generic;
using System.Threading;
using Demo.ViewModels.Workflow.Helper;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// Stands in for the Python interpreter, answering by node title — what it returns is what the real script would
/// return, including the diagnostic fields the host reacts to (<c>warn</c> / <c>error</c> / <c>redirect</c>) and the
/// engine-state keys it was handed (<c>_attempt</c> / <c>_drive</c>). Everything around it — the payload the helper
/// builds, the drive counter, the error and redirect handling — is the real code; <c>DemoScriptTests</c> runs the
/// real scripts.
/// </summary>
internal sealed class StubPythonHelper : PythonHelper
{
    private readonly Dictionary<string, int> _drives = [];

    /// <summary>One line per invocation — what the node was handed, which is what a failure here is about.</summary>
    public List<string> Trail { get; } = [];

    /// <summary>
    /// When positive, the invocation with this number stops the run the way a host's Stop does. The engine reads
    /// an <see cref="OperationCanceledException"/> as cancellation rather than failure, so the run ends at that
    /// node boundary and everything already driven stays in the checkpoint.
    /// </summary>
    public int CancelAtInvocation { get; set; } = -1;

    private int _invocations;

    /// <summary>
    /// Called on this stub's own invocation number — a run is held or stopped from inside it, which is where a
    /// host's Pause and Stop land anyway: the engine only ever looks at the gate at a node boundary.
    /// </summary>
    public Action<int>? OnInvocation { get; set; }

    public int Drives(string title) => _drives.TryGetValue(title, out var n) ? n : 0;

    /// <summary>How long each call pretends to take — so a test can catch a run while it is still in flight.</summary>
    public TimeSpan PerCallDelay { get; set; }

    protected override async Task<string> InvokePythonAsync(string script, object? payload, string pythonExe, CancellationToken ct)
    {
        if (PerCallDelay > TimeSpan.Zero) await Task.Delay(PerCallDelay, ct);
        var input = payload as IDictionary<string, object?> ?? new Dictionary<string, object?>();
        var title = Component?.Title ?? string.Empty;
        var attempt = Read(input, "_attempt");
        var drive = Read(input, "_drive");
        _drives[title] = drive;
        Trail.Add($"{title}|drive={drive}|attempt={attempt}|payload={payload?.GetType().Name ?? "null"}");

        var invocation = ++_invocations;
        OnInvocation?.Invoke(invocation);
        if (CancelAtInvocation > 0 && invocation >= CancelAtInvocation)
            throw new OperationCanceledException(ct);

        // A failed delivery, not a reported one: the point of this node is the retry policy, and only a throw
        // reaches it — a script that reports an error is asking the run to stop, which is a different thing.
        if (title == "Publish" && drive == 1)
            throw new InvalidOperationException("transient: the report sink refused the connection (simulated)");

        return Answer(title, input, attempt, drive);
    }

    private static int Read(IDictionary<string, object?> input, string key)
        => input.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;

    /// <summary>Writes what the real script would write — including what it forwards, since the audit reads it.</summary>
    private static string Answer(string title, IDictionary<string, object?> input, int attempt, int drive) => title switch
    {
        // First pass: a flat pair of samples, which the audit below is meant to refuse.
        "Generate Dataset" => attempt == 1
            ? """{"samples":[220.0,220.0],"count":2,"note":"quick pass"}"""
            : """{"samples":[175.0,320.0,178.0],"count":3,"note":"full pass"}""",

        "Load Sample File" => """{"samples":[220.0],"count":1,"unit":"V"}""",

        "Numeric Stats" => """{"count":3,"mean":224.3,"median":178.0,"stdev":68.1,"min":175.0,"max":320.0,"p95":320.0,"range":145.0}""",

        "Frequency Dist" => attempt == 1
            ? """{"histogram":[{"bin":"220-240","count":2}],"total":2,"warn":"empty histogram bin(s): <200, >240"}"""
            : """{"histogram":[{"bin":"<200","count":1},{"bin":"220-240","count":1},{"bin":">240","count":1}],"total":3}""",

        "Anomaly Scan" => attempt == 1
            ? """{"anomalies":[],"count":0,"threshold_z":2.0,"warn":"no anomaly in 2 samples: the set looks flat"}"""
            : """{"anomalies":[{"index":1,"value":320.0,"z":2.4}],"count":1,"threshold_z":2.0}""",

        "Merge Report" => attempt == 1
            ? """{"summary":{"count":2},"sample_count":2,"anomaly_count":0,"mean_voltage":220.0,"grade":"Zero","High":0,"Low":0,"Zero":1}"""
            : """{"summary":{"count":40},"sample_count":40,"anomaly_count":1,"mean_voltage":224.3,"grade":"Low","High":0,"Low":1,"Zero":0}""",

        // Carries the report on, exactly as the real script does: the audit behind this node reads it.
        "Publish" => Forward(input, new() { ["published"] = true, ["attempt"] = attempt }),

        // The audit: refuses the thin set by naming the node to fall back to, and passes the report through
        // untouched once the data is good (the selector below routes on the flags it carries).
        "Audit" => Read(input, "sample_count") < 20
            ? """{"verified":false,"sample_count":2,"error":"too few samples got published, run the pipeline again","redirect":"Generate Dataset"}"""
            : Forward(input, new() { ["verified"] = true }),

        // The payload is null when the audit sent the run back, and the real script warns instead of crashing.
        "Report High" or "Report Low" or "Report Zero" => input.Count == 0
            ? """{"warn":"nothing to archive this pass: the audit sent the run back"}"""
            : """{"saved_to":"report_low.csv","grade":"Low","records":4}""",

        _ => "{}",
    };

    private static string Forward(IDictionary<string, object?> input, Dictionary<string, object?> extra)
    {
        var output = new Dictionary<string, object?>(input);
        foreach (var entry in extra) output[entry.Key] = entry.Value;
        return System.Text.Json.JsonSerializer.Serialize(output);
    }
}
