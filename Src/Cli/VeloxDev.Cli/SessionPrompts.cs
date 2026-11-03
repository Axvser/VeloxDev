using System.ComponentModel;
using ModelContextProtocol.Server;

namespace VeloxDev.Cli;

/// <summary>A reusable operator prompt so a client can start a turn with the server's own working rules.</summary>
[McpServerPromptType]
internal sealed class SessionPrompts(WorkflowSessionHost host)
{
    [McpServerPrompt(Name = "workflow-operator")]
    [Description("Instructions for operating the loaded workflow with this server's tools: orient, edit through native commands, save, run, and configure the redirect fallback.")]
    public string WorkflowOperator()
    {
        using var lease = host.Acquire();
        return lease.Session.BuildInstructions();
    }
}
