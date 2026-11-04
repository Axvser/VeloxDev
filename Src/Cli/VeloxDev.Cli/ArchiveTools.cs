using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using VeloxDev.Serialization;

namespace VeloxDev.Cli;

/// <summary>
/// Archive-level operations that are not graph commands: reporting what is loaded, writing the document back,
/// pulling in an assembly the archive did not declare, and switching documents without restarting.
/// </summary>
[McpServerToolType]
internal sealed class ArchiveTools(WorkflowSessionHost host)
{
    [McpServerTool(Name = "ArchiveInfo", ReadOnly = true)]
    [Description("Reports the archive this process loaded: path, root type, node/link counts, execution mode, redirect target, checkpoint and log files, the dependency assemblies already loaded, the task/assembly-staging and run-log directories, and the Core version. Call this first when you are unsure which graph you are operating.")]
    public string ArchiveInfo()
    {
        using var lease = host.Acquire();
        return lease.Session.Describe();
    }

    [McpServerTool(Name = "LoadArchive", Destructive = true, OpenWorld = true)]
    [Description("Replaces the loaded workflow with another archive without restarting the server. Unsaved edits to the current graph are discarded. Assemblies listed in the new archive's manifest (and any supplied here) are loaded first; loading runs their code. Refused in --in-process-execution mode because run handles belong to the old session. The tool list stays valid; every tool resolves the new session.")]
    public async Task<string> LoadArchive(
        [Description("Path to the workflow document.")] string path,
        [Description("Optional manifest path; defaults to <archive>.manifest.json when present.")] string? manifest = null,
        [Description("Optional dependency assemblies to load before reading the document.")] string[]? assemblies = null,
        [Description("Optional concrete tree type when the document carries no root $type.")] string? rootType = null,
        [Description("Optional redirect target (0-based node index) to install for subsequent runs; omit for none.")] int? redirectOnError = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Error("An archive path is required.");

        var started = Stopwatch.GetTimestamp();
        var auditArguments = JsonSerializer.Serialize(new { path, manifest, assemblies, rootType, redirectOnError });
        try
        {
            await host.ReloadAsync(
                new ArchiveRequest(path, manifest, assemblies ?? [], rootType),
                redirectOnError,
                cancellationToken).ConfigureAwait(false);

            using var lease = host.Acquire();
            var described = lease.Session.Describe();
            host.RecordToolCall("LoadArchive", auditArguments, ok: true, described.Length,
                Stopwatch.GetElapsedTime(started));
            return described;
        }
        catch (Exception ex)
        {
            host.RecordToolCall("LoadArchive", auditArguments, ok: false, resultChars: 0,
                Stopwatch.GetElapsedTime(started), ex.Message);
            return Error($"The archive could not be loaded: {ex.Message}");
        }
    }

    [McpServerTool(Name = "GetCapabilities", ReadOnly = true)]
    [Description("Reports what this server can do in one call: the exposed tool names, execution mode, the 64 KiB-class result cap, the workspace, assembly-staging and run-log directories, and whether the loaded dependency assemblies match the host version. Use it instead of inferring limits from individual tool descriptions.")]
    public string GetCapabilities()
    {
        using var lease = host.Acquire();
        return lease.Session.DescribeCapabilities(host);
    }

    [McpServerTool(Name = "GetAuditLog", ReadOnly = true)]
    [Description("Returns the recent tool-call audit ring, newest last: tool name, UTC time, duration, success, result size and a 400-character argument summary. Archive-level tools and native graph tools are both recorded. maxEntries keeps the most recent N; 0 returns all retained entries.")]
    public string GetAuditLog(
        [Description("Maximum entries to return, keeping the most recent. 0 = all retained.")] int maxEntries = 0)
        => host.GetAuditLog(maxEntries);

    [McpServerTool(Name = "ApplyBatch", Destructive = true)]
    [Description("Applies a JSON array of native graph-edit calls in one tool call: [{\"tool\":\"CreateNode\",\"arguments\":{...}}, ...]. Only Mutation and Layout tools are accepted. With stopOnError=true the server snapshots the graph first and, on the first failing operation, reloads that snapshot as the live session; the file is returned as stateFile and the previous archive is untouched. Refused with stopOnError under --in-process-execution. At most 100 operations.")]
    public async Task<string> ApplyBatch(
        [Description("JSON array of {tool, arguments} objects.")] string operationsJson,
        [Description("Stop at the first failure and reload the pre-batch snapshot. Default true.")] bool stopOnError = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationsJson))
            return Error("A JSON array of operations is required.");

        var operations = new List<(string Tool, AIFunctionArguments Arguments)>();
        try
        {
            using var document = JsonDocument.Parse(operationsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Error("operationsJson must be a JSON array.");

            foreach (var operation in document.RootElement.EnumerateArray())
            {
                if (operation.ValueKind != JsonValueKind.Object ||
                    !operation.TryGetProperty("tool", out var toolElement) ||
                    toolElement.GetString() is not { Length: > 0 } tool)
                {
                    return Error("Each operation needs a non-empty 'tool' string.");
                }

                var arguments = new AIFunctionArguments();
                if (operation.TryGetProperty("arguments", out var argumentsElement) &&
                    argumentsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in argumentsElement.EnumerateObject())
                        arguments[property.Name] = ToClr(property.Value);
                }

                operations.Add((tool, arguments));
            }
        }
        catch (JsonException ex)
        {
            return Error($"operationsJson is not valid JSON: {ex.Message}");
        }

        if (operations.Count == 0)
            return Error("At least one operation is required.");
        if (operations.Count > 100)
            return Error("A batch may contain at most 100 operations.");

        using var lease = host.Acquire();
        var session = lease.Session;
        var rejected = operations
            .Select(operation => operation.Tool)
            .Where(tool => !session.IsBatchableTool(tool))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (rejected.Count > 0)
        {
            return Error(
                $"Not batchable: {string.Join(", ", rejected)}. ApplyBatch accepts Mutation and Layout tools only; " +
                "archive, query and execution tools are not allowed.");
        }

        if (stopOnError && session.InProcessExecution)
        {
            return Error(
                "ApplyBatch with stopOnError is unavailable under --in-process-execution: rollback reloads the " +
                "archive, which that mode refuses. Pass stopOnError=false or run without --in-process-execution.");
        }

        var started = Stopwatch.GetTimestamp();
        string? snapshotPath = null;
        if (stopOnError)
        {
            try
            {
                var archiveFull = Path.GetFullPath(session.ArchivePath);
                var directory = Path.GetDirectoryName(archiveFull) ?? Path.GetTempPath();
                snapshotPath = Path.Combine(directory, $".veloxdev-batch-{Guid.NewGuid():N}.json");
                File.WriteAllText(
                    snapshotPath,
                    session.Scope.Tree.Serialize(SerializationOptions.Create().WithCompact()));
            }
            catch (Exception ex)
            {
                return Error($"Could not write the rollback snapshot: {ex.Message}");
            }
        }

        var results = new List<object>();
        var failedCount = 0;
        var rolledBack = false;
        try
        {
            for (var i = 0; i < operations.Count; i++)
            {
                var (tool, arguments) = operations[i];
                try
                {
                    var raw = await host.InvokeToolAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
                    var text = raw?.ToString() ?? string.Empty;
                    var isError = IsErrorResult(text);
                    if (isError) failedCount++;
                    results.Add(new
                    {
                        index = i,
                        tool,
                        ok = !isError,
                        result = text.Length > 1000 ? text[..1000] + "…" : text,
                    });
                    if (isError && stopOnError) break;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    results.Add(new { index = i, tool, ok = false, error = ex.Message });
                    if (stopOnError) break;
                }
            }

            if (failedCount > 0 && stopOnError && snapshotPath is not null)
            {
                await host.ReloadAsync(
                    new ArchiveRequest(snapshotPath, null, [], null),
                    redirectOnError: null,
                    cancellationToken).ConfigureAwait(false);
                rolledBack = true;
            }
        }
        finally
        {
            if (!rolledBack && snapshotPath is not null)
            {
                try { File.Delete(snapshotPath); }
                catch { /* the snapshot is disposable when the caller did not take it */ }
            }
        }

        var response = JsonSerializer.Serialize(new
        {
            status = failedCount > 0 ? "error" : "ok",
            operationCount = operations.Count,
            failedCount,
            rolledBack,
            stateFile = rolledBack ? snapshotPath : null,
            note = rolledBack
                ? "The session was reloaded from the pre-batch snapshot; the previous archive file is untouched. " +
                  "Call SaveArchive with the intended path to persist the restored graph."
                : null,
            results,
        });

        host.RecordToolCall(
            "ApplyBatch",
            JsonSerializer.Serialize(new { operationCount = operations.Count, stopOnError }),
            failedCount == 0,
            response.Length,
            Stopwatch.GetElapsedTime(started));
        return response;
    }

    [McpServerTool(Name = "SaveArchive", Destructive = true)]
    [Description("Writes the current graph back to an archive document in the library's JSON format. With no path, overwrites the archive this process was started with. The write is atomic (temporary file, then replace) and compact by default; pass dryRun to inspect the result without touching the disk, and backup=false to skip the .bak copy. Editing tools change the in-memory graph only; call this when the change should survive the process.")]
    public string SaveArchive(
        [Description("Destination path. Omit to overwrite the archive this process loaded.")] string? path = null,
        [Description("Report what would be written without touching the disk. Default false.")] bool dryRun = false,
        [Description("Keep the previous file as <path>.bak before replacing it. Default true.")] bool backup = true,
        [Description("Write compact JSON (no indentation). Default true; pass false for a readable document.")] bool compact = true)
    {
        var started = Stopwatch.GetTimestamp();
        var auditArguments = JsonSerializer.Serialize(new { path, dryRun, backup, compact });
        try
        {
            using var lease = host.Acquire();
            var result = lease.Session.SaveArchive(path, dryRun, backup, compact);
            host.RecordToolCall("SaveArchive", auditArguments, ok: true, result.Bytes,
                Stopwatch.GetElapsedTime(started));
            return JsonSerializer.Serialize(new
            {
                status = "ok",
                dryRun,
                path = result.Path,
                bytes = result.Bytes,
                format = result.Format,
                backupPath = result.BackupPath,
                archivePathAfterSave = result.ArchivePathAfterSave,
                nodeCount = result.NodeCount,
                linkCount = result.LinkCount,
                message = dryRun
                    ? "Dry run: nothing was written."
                    : "The graph was written to disk. The live session now treats this as its archive path.",
            });
        }
        catch (Exception ex)
        {
            host.RecordToolCall("SaveArchive", auditArguments, ok: false, resultChars: 0,
                Stopwatch.GetElapsedTime(started), ex.Message);
            return Error($"The archive could not be written: {ex.Message}");
        }
    }

    [McpServerTool(Name = "LoadAssemblies", Destructive = true, OpenWorld = true)]
    [Description("Loads dependency assemblies into this process so their component types become creatable. Loading runs the assembly's code (module initializers), so pass only files you trust. Use it when a type the graph or the task needs is not in ListCreatableTypes. Paths are resolved against the process working directory.")]
    public string LoadAssemblies(
        [Description("Absolute or working-directory-relative paths of .NET assemblies to load.")] string[] paths)
    {
        if (paths is null || paths.Length == 0)
            return Error("At least one assembly path is required.");

        var started = Stopwatch.GetTimestamp();
        var auditArguments = JsonSerializer.Serialize(new { paths });
        try
        {
            using var lease = host.Acquire();
            var loaded = lease.Session.LoadAssemblies(paths);
            var response = JsonSerializer.Serialize(new
            {
                status = "ok",
                loaded,
                warnings = lease.Session.Plugins.Warnings,
                message = "The assemblies are loaded and their registered component types are available to ListCreatableTypes.",
            });
            host.RecordToolCall("LoadAssemblies", auditArguments, ok: true, response.Length,
                Stopwatch.GetElapsedTime(started));
            return response;
        }
        catch (Exception ex)
        {
            host.RecordToolCall("LoadAssemblies", auditArguments, ok: false, resultChars: 0,
                Stopwatch.GetElapsedTime(started), ex.Message);
            return Error($"A dependency assembly could not be loaded: {ex.Message}");
        }
    }

    private static object? ToClr(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ToClr).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => ToClr(property.Value),
                StringComparer.Ordinal),
            _ => element.GetRawText(),
        };

    private static bool IsErrorResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("status", out var status) &&
                   string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new { status = "error", message });
}
