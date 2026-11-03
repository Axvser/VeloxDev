using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>
/// Archive-level operations that are not graph commands: reporting what is loaded, writing the document back,
/// pulling in an assembly the archive did not declare, and switching documents without restarting.
/// </summary>
[McpServerToolType]
internal sealed class ArchiveTools(WorkflowSessionHost host)
{
    [McpServerTool(Name = "ArchiveInfo", ReadOnly = true)]
    [Description("Reports the archive this process loaded: path, root type, node/link counts, execution mode, redirect target, checkpoint and log files, the dependency assemblies already loaded, and the Core version. Call this first when you are unsure which graph you are operating.")]
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

        try
        {
            await host.ReloadAsync(
                new ArchiveRequest(path, manifest, assemblies ?? [], rootType),
                redirectOnError,
                cancellationToken).ConfigureAwait(false);

            using var lease = host.Acquire();
            return lease.Session.Describe();
        }
        catch (Exception ex)
        {
            return Error($"The archive could not be loaded: {ex.Message}");
        }
    }

    [McpServerTool(Name = "SaveArchive", Destructive = true)]
    [Description("Writes the current graph back to an archive document in the library's JSON format. With no path, overwrites the archive this process was started with. The write is atomic (temporary file, then replace). Editing tools change the in-memory graph only; call this when the change should survive the process.")]
    public string SaveArchive(
        [Description("Destination path. Omit to overwrite the archive this process loaded.")] string? path = null)
    {
        try
        {
            using var lease = host.Acquire();
            var (written, bytes) = lease.Session.SaveArchive(path);
            return JsonSerializer.Serialize(new
            {
                status = "ok",
                path = written,
                bytes,
                message = "The graph was written to disk. The live session now treats this as its archive path.",
            });
        }
        catch (Exception ex)
        {
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

        try
        {
            using var lease = host.Acquire();
            var loaded = lease.Session.LoadAssemblies(paths);
            return JsonSerializer.Serialize(new
            {
                status = "ok",
                loaded,
                warnings = lease.Session.Plugins.Warnings,
                message = "The assemblies are loaded and their registered component types are available to ListCreatableTypes.",
            });
        }
        catch (Exception ex)
        {
            return Error($"A dependency assembly could not be loaded: {ex.Message}");
        }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new { status = "error", message });
}
