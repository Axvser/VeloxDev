using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// Python helper: the node's real-computation engine. It maps the incoming payload to an input.json,
/// writes the node's <see cref="PythonScriptNodeViewModel.Script"/> into a per-run cache directory next to the
/// exe, runs it with the configured Python executable via CliWrap, and reads back the result JSON.
/// The process invocation is isolated in <see cref="InvokePythonAsync"/> so tests can stub it.
/// </summary>
public class PythonHelper : NodeHelper<PythonScriptNodeViewModel>
{
    /// <summary>Maximum wall-clock time a single script may take before the run is cancelled.</summary>
    protected virtual TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public override async Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is null) return null;
        var runtime = ctx as IRuntimeContext;

        // Every drive starts here: drop the fallback target the last script left behind, and count this drive.
        Component.RedirectTo = null;
        var drive = Drive(runtime);

        if (string.IsNullOrWhiteSpace(Component.Script))
        {
            // A warning: the other thirty branches still have a script, so the run carries on with the null this
            // returns. The async pair is also what hands the host's IExecutionErrorSink a record.
            if (runtime is not null) await runtime.WarnAsync("Python script is empty; nothing to run.");
            return null;
        }

        var payload = BuildInputPayload(ctx, runtime, drive);
        Component.LastStatus = "Running";
        try
        {
            var raw = await InvokePythonAsync(Component.Script, payload, Component.PythonExecutable, ct);
            var parsed = ParseResult(raw);
            Component.LastStatus = "Completed";
            Component.LastRun = DateTime.Now.ToString("HH:mm:ss");
            Component.LastOutput = Truncate(raw);
            if (runtime is not null)
            {
                runtime.Log($"Python finished in {Component.LastRun}: {Truncate(raw, 200)}");
                await ReportAsync(runtime, parsed);
            }
            return parsed;
        }
        catch (OperationCanceledException)
        {
            Component.LastStatus = "Canceled";
            throw;
        }
        catch (Exception ex)
        {
            // Thrown, not reported-and-swallowed. A script that fails is what INodeRetryPolicy exists for: the
            // engine logs it, offers it to the policy, and only gives up when the retries run out. Swallowing it
            // into an `Error` would instead be a stop the moment the script hiccups once.
            Component.LastStatus = "Failed";
            Component.LastOutput = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// How many times this node has been driven in the current run — a retry increments it, a redirect pass does
    /// not. Reset when the session changes, since each run of the demo is a fresh session.
    /// </summary>
    /// <remarks>Handed to the script as <c>_drive</c>; see <see cref="BuildInputPayload"/>.</remarks>
    private int Drive(IRuntimeContext? runtime)
    {
        if (runtime is null) return ++_drives;
        if (_runUid != runtime.Uid)
        {
            _runUid = runtime.Uid;
            _drives = 0;
        }
        return ++_drives;
    }

    private Guid _runUid;
    private int _drives;

    /// <summary>
    /// Turns the diagnostics a script put in its own result into a session report:
    /// <c>"warn"</c> → a warning (the run carries on), <c>"redirect"</c> → the node to fall back to, by title, and
    /// <c>"error"</c> → an error (which ends the run unless a redirect target is named).
    /// </summary>
    /// <remarks>
    /// A script cannot call the runtime, so it says what it wants in the data it returns. Keeping the convention
    /// here — in the helper, one place — is what lets a script decide the flow without a node type of its own.
    /// </remarks>
    private async Task ReportAsync(IRuntimeContext runtime, object? parsed)
    {
        if (parsed is not IDictionary<string, object?> output) return;

        if (output.TryGetValue("warn", out var warn) && warn is string warnText)
            await runtime.WarnAsync(warnText);

        if (output.TryGetValue("redirect", out var redirect) && redirect is string title)
            Component!.RedirectTo = title;

        if (output.TryGetValue("error", out var error) && error is string errorText)
            await runtime.ErrorAsync(errorText);
    }

    /// <summary>
    /// Maps the incoming payload to a JSON-serializable object. At a join point (several wired input ports) the
    /// engine injects <see cref="IGroupData"/> (keyed by source node); the payload is rebuilt as
    /// <c>{ portName: sourceOutput }</c> so the script sees meaningful field names. Otherwise the single upstream
    /// output is passed through as-is.
    /// </summary>
    /// <remarks>
    /// <b>Two keys are added when that payload is an object</b> — <c>_attempt</c> (which pass over the graph this
    /// is) and <c>_drive</c> (which time this node has been driven) — because a script has no other way to see the
    /// run's own state. They are what let the demo's generator write a quick set on the first pass and the full one
    /// after a redirect, and let its publish step fail once and succeed on the retry. A scalar or array payload is
    /// passed through untouched: there is nowhere to put them, and a script that receives an array is clearly not
    /// reading keys.
    /// </remarks>
    public object? BuildInputPayload(ITaskContext ctx, IRuntimeContext? runtime = null, int drive = 0)
    {
        var map = new Dictionary<string, object?>();
        if (ctx.Data is IGroupData group && Component?.InputSlots is { } inputSlots)
        {
            foreach (var item in inputSlots.Items)
            {
                var source = item.Slot?.Sources?.FirstOrDefault()?.Parent;
                if (source is not null && group.TryGetValue(source, out var value))
                    map[item.Name] = value;
            }
        }
        else if (ctx.Data is IDictionary<string, object?> payload)
        {
            // Copied rather than mutated: the dictionary is the upstream node's own output, and the engine state
            // the script is about to read has no business appearing in another node's result.
            foreach (var entry in payload) map[entry.Key] = entry.Value;
        }
        else
        {
            return ctx.Data;
        }

        map["_attempt"] = runtime?.Attempt ?? 1;
        map["_drive"] = drive;
        return map;
    }

    /// <summary>
    /// Runs the script: writes <c>script.py</c> + <c>input.json</c> to <c>pycache/</c> next to the exe, invokes
    /// <c>python script.py input.json output.json</c>, and returns the script's result — preferring the written
    /// <c>output.json</c>, falling back to stdout. Scratch files are cleaned up afterwards.
    /// </summary>
    protected virtual async Task<string> InvokePythonAsync(string script, object? payload, string pythonExe, CancellationToken ct)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pycache");
        Directory.CreateDirectory(dir);
        var id = $"{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(dir, $"script-{id}.py");
        var inputPath = Path.Combine(dir, $"input-{id}.json");
        var outputPath = Path.Combine(dir, $"output-{id}.json");

        File.WriteAllText(scriptPath, script);
        File.WriteAllText(inputPath, JsonConvert.SerializeObject(payload));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var result = await Cli.Wrap(pythonExe)
                .WithArguments(new[] { scriptPath, inputPath, outputPath })
                .ExecuteBufferedAsync(timeout.Token);

            if (File.Exists(outputPath))
                return File.ReadAllText(outputPath);

            var stdout = result.StandardOutput;
            return string.IsNullOrWhiteSpace(stdout) ? result.StandardError : stdout;
        }
        finally
        {
            foreach (var f in new[] { scriptPath, inputPath, outputPath })
            {
                try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { /* best-effort cleanup */ }
                catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>Parses the raw script result: JSON object → dictionary; other JSON → JToken; non-JSON → the raw string.</summary>
    public static object? ParseResult(string? raw)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        try
        {
            var token = JToken.Parse(trimmed);
            return token.Type == JTokenType.Object
                ? token.ToObject<Dictionary<string, object?>>()
                : (object?)token;
        }
        catch (JsonReaderException)
        {
            return trimmed;
        }
    }

    private static string Truncate(string? value, int max = 240)
    {
        if (value is null || value.Length == 0) return "-";
        return value.Length <= max ? value : value.Substring(0, max) + "...";
    }
}
