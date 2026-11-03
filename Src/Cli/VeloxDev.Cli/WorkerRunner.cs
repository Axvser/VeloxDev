using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VeloxDev.Serialization;

namespace VeloxDev.Cli;

/// <summary>
/// Runs node business code in a child <c>veloxdev run</c> process.
/// <para>
/// The child gets a snapshot of the current in-memory graph, the dependency assemblies this session loaded, the
/// redirect setting and the checkpoint file — so an edit made a moment ago is what executes, without saving to the
/// archive. Isolation is the point: a node that hangs, calls <c>Environment.Exit</c>, or crashes a native library
/// kills only the worker; the MCP server survives and returns the failure.
/// </para>
/// </summary>
internal sealed class WorkerRunner(WorkflowSession session)
{
    private const int StderrTailChars = 8 * 1024;

    public Task<string> RunCompiledAsync(int startNodeIndex, string? seed, int? timeoutMs, CancellationToken ct)
        => RunAsync(nodeResult: false, startNodeIndex, seed, timeoutMs, ct);

    public Task<string> GetNodeResultAsync(int nodeIndex, string? seed, int? timeoutMs, CancellationToken ct)
        => RunAsync(nodeResult: true, nodeIndex, seed, timeoutMs, ct);

    private async Task<string> RunAsync(bool nodeResult, int index, string? seed, int? timeoutMs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Error("The run was cancelled before it started.");

        var directory = Path.Combine(Path.GetTempPath(), "veloxdev-worker");
        Directory.CreateDirectory(directory);
        var runId = Guid.NewGuid().ToString("N");
        var snapshot = Path.Combine(directory, runId + ".json");
        var state = Path.Combine(directory, runId + ".state.json");

        try
        {
            // Serializing through the interface writes the root $type, so the child never needs --root-type.
            File.WriteAllText(snapshot, session.Scope.Tree.Serialize());

            var startInfo = BuildStartInfo(snapshot, state, nodeResult, index, seed, timeoutMs);
            return await ExecuteAsync(startInfo, nodeResult, timeoutMs, session.WorkerKillGraceMs, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error($"The worker could not be started: {ex.Message}");
        }
        finally
        {
            try { File.Delete(snapshot); }
            catch { /* the worker has the file open on some platforms; temp cleanup is best-effort */ }
        }
    }

    private ProcessStartInfo BuildStartInfo(
        string snapshot, string state, bool nodeResult, int index, string? seed, int? timeoutMs)
    {
        var cli = typeof(WorkerRunner).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            // The child must not inherit the server's stdin: under stdio MCP that handle is the protocol stream,
            // and a worker reading from it would steal client messages.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(cli) ?? AppContext.BaseDirectory,
        };

        startInfo.ArgumentList.Add(cli);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--archive");
        startInfo.ArgumentList.Add(snapshot);
        startInfo.ArgumentList.Add("--start");
        startInfo.ArgumentList.Add(index.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--output-tree");
        startInfo.ArgumentList.Add(state);

        if (nodeResult)
            startInfo.ArgumentList.Add("--node-result");

        if (seed is not null)
        {
            startInfo.ArgumentList.Add("--seed");
            startInfo.ArgumentList.Add(seed);
        }

        if (timeoutMs is { } timeout)
        {
            startInfo.ArgumentList.Add("--timeout-ms");
            startInfo.ArgumentList.Add(timeout.ToString(CultureInfo.InvariantCulture));
        }

        if (session.Redirect.NodeIndex is { } redirect)
        {
            startInfo.ArgumentList.Add("--redirect-on-error");
            startInfo.ArgumentList.Add(redirect.ToString(CultureInfo.InvariantCulture));
        }

        if (session.CheckpointFile is { } checkpoint)
        {
            startInfo.ArgumentList.Add("--checkpoint-file");
            startInfo.ArgumentList.Add(checkpoint);
        }

        if (session.LogFile is { } logFile)
        {
            startInfo.ArgumentList.Add("--log-file");
            startInfo.ArgumentList.Add(logFile);
        }

        foreach (var assembly in session.Plugins.LoadedPaths)
        {
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assembly);
        }

        return startInfo;
    }

    private static async Task<string> ExecuteAsync(
        ProcessStartInfo startInfo, bool nodeResult, int? timeoutMs, int killGraceMs, CancellationToken ct)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return Error("The worker process could not be started.");

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeoutMs is { } timeout)
            limit.CancelAfter(TimeSpan.FromMilliseconds(timeout + killGraceMs));

        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* already gone */ }

            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* nothing left to wait for */ }

            var stoppedStdErr = (await stderr.ConfigureAwait(false)).Trim();
            var cancelled = ct.IsCancellationRequested;
            var reason = cancelled
                ? "The run was cancelled; the worker process was stopped."
                : $"The run did not finish within {timeoutMs} ms and the worker process was stopped.";

            // A stopped run is an outcome, not an infrastructure error: the tool answers with the same shape a
            // completed run has, so the client can tell "cancelled" from "the worker could not start".
            return JsonSerializer.Serialize(new
            {
                status = "ok",
                role = nodeResult ? "Terminal" : "Root",
                runStatus = "Stopped",
                outcome = "Cancelled",
                endedWithError = false,
                cancelled,
                timedOut = !cancelled,
                attempts = 0,
                data = (object?)null,
                failures = Array.Empty<object>(),
                logs = Array.Empty<string>(),
                logFile = (string?)null,
                stderr = Tail(stoppedStdErr, StderrTailChars),
                message = reason,
            });
        }

        var output = (await stdout.ConfigureAwait(false)).Trim();
        var errors = (await stderr.ConfigureAwait(false)).Trim();

        if (process.ExitCode == 0 && IsJson(output))
            return output;

        return JsonSerializer.Serialize(new
        {
            status = "error",
            message = $"The worker exited with code {process.ExitCode}.",
            output = Trim(output),
            stderr = Tail(errors, StderrTailChars),
        });
    }

    private static bool IsJson(string text)
    {
        if (text.Length == 0 || text[0] is not ('{' or '[')) return false;

        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Trim(string text)
        => text.Length <= 2_000 ? text : text[..2_000] + "…";

    private static string Tail(string text, int max)
        => text.Length <= max ? text : text[^max..];

    private static string Error(string message)
        => JsonSerializer.Serialize(new { status = "error", message });
}
