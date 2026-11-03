using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>
/// Read-only context for clients that prefer resources to a tool call. Nothing here mutates the graph; the
/// editing/execution surface stays on tools.
/// </summary>
[McpServerResourceType]
internal sealed class SessionResources(WorkflowSessionHost host)
{
    [McpServerResource(
        UriTemplate = "veloxdev://archive/info",
        Name = "ArchiveInfo",
        MimeType = "application/json")]
    [Description("The currently loaded archive: path, root type, counts, execution mode, redirect target, loaded assemblies and plugin warnings.")]
    public string ArchiveInfo()
    {
        using var lease = host.Acquire();
        return lease.Session.Describe();
    }

    [McpServerResource(
        UriTemplate = "veloxdev://archive/manifest",
        Name = "ArchiveManifest",
        MimeType = "application/json")]
    [Description("The sidecar manifest of the currently loaded archive, or an empty object when there is none.")]
    public string ArchiveManifest()
    {
        using var lease = host.Acquire();
        var path = lease.Session.ManifestPath;
        if (path is null || !File.Exists(path)) return "{}";
        return File.ReadAllText(path);
    }
}
