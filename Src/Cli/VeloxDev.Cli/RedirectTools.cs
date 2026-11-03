using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>
/// The agent-visible side of the host-level redirect policy. Registered only when execution is enabled, because a
/// fallback has no meaning without a run.
/// </summary>
[McpServerToolType]
internal sealed class RedirectTools(WorkflowSessionHost host)
{
    [McpServerTool(Name = "SetRedirectOnError", Destructive = true)]
    [Description("Sets the host-level fallback used when a node reports an error but does not implement IRedirectable: the run re-runs from this upstream node instead of ending. The engine validates the target against every failure — it must be an upstream node in the compiled graph, and an invalid answer ends the run rather than being ignored. Use ListNodes for the index. Applies to every run started afterwards; nodes that implement IRedirectable keep their own decision, including declining.")]
    public string SetRedirectOnError(
        [Description("0-based node index from ListNodes. It should be upstream of the node expected to fail.")] int nodeIndex)
    {
        try
        {
            using var lease = host.Acquire();
            lease.Session.SetRedirectOnError(nodeIndex);
            return JsonSerializer.Serialize(new
            {
                status = "ok",
                redirectOnError = lease.Session.Redirect.NodeIndex,
                message = "Subsequent runs will fall back to this node when a node without its own redirect contract fails.",
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                status = "error",
                message = $"The redirect target was not accepted: {ex.Message}",
            });
        }
    }

    [McpServerTool(Name = "ClearRedirectOnError", Destructive = true)]
    [Description("Removes the host-level redirect fallback. Subsequent runs end when a node without IRedirectable reports an error, as before.")]
    public string ClearRedirectOnError()
    {
        using var lease = host.Acquire();
        lease.Session.SetRedirectOnError(null);
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            redirectOnError = (int?)null,
        });
    }
}
