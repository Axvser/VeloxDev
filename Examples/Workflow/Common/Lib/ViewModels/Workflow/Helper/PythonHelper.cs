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
        if (string.IsNullOrWhiteSpace(Component.Script))
        {
            if (ctx is IRuntimeContext rc) rc.Warn("Python script is empty; nothing to run.");
            return null;
        }

        var payload = BuildInputPayload(ctx);
        Component.LastStatus = "Running";
        try
        {
            var raw = await InvokePythonAsync(Component.Script, payload, Component.PythonExecutable, ct);
            var parsed = ParseResult(raw);
            Component.LastStatus = "Completed";
            Component.LastRun = DateTime.Now.ToString("HH:mm:ss");
            Component.LastOutput = Truncate(raw);
            if (ctx is IRuntimeContext rc)
                rc.Log($"→ Python finished in {Component.LastRun}: {Truncate(raw, 200)}");
            return parsed;
        }
        catch (OperationCanceledException)
        {
            Component.LastStatus = "Canceled";
            throw;
        }
        catch (Exception ex)
        {
            Component.LastStatus = "Failed";
            Component.LastOutput = ex.Message;
            if (ctx is IRuntimeContext rc)
                rc.Error($"Python execution failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Maps the incoming payload to a JSON-serializable object. At a join point (several wired input ports) the
    /// engine injects <see cref="IGroupData"/> (keyed by source node); the payload is rebuilt as
    /// <c>{ portName: sourceOutput }</c> so the script sees meaningful field names. Otherwise the single upstream
    /// output is passed through as-is.
    /// </summary>
    public object? BuildInputPayload(ITaskContext ctx)
    {
        if (ctx.Data is IGroupData group && Component?.InputSlots is { } inputSlots)
        {
            var map = new Dictionary<string, object?>();
            foreach (var item in inputSlots.Items)
            {
                var source = item.Slot?.Sources?.FirstOrDefault()?.Parent;
                if (source is not null && group.TryGetValue(source, out var value))
                    map[item.Name] = value;
            }
            return map;
        }
        return ctx.Data;
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
