using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.MCP;

/// <summary>
/// MCP management tools callable by the Agent: list server status, load (install and connect) the
/// host pre-registered servers. Server configuration is provided in advance by the host
/// (<see cref="McpServerConfiguration"/>); the Agent does not construct arbitrary configs — the
/// security boundary matches the workflow tools: "host registers, Agent operates". Registered via
/// <c>WorkflowAgentScope.WithTools(...)</c>, they get the same UI-thread marshalling / call counting /
/// interaction-safety hints as the other tools.
/// </summary>
public sealed class McpAgentToolkit(McpScope scope, IReadOnlyList<McpServerConfiguration> servers)
{
    private readonly McpScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IReadOnlyList<McpServerConfiguration> _servers = servers ?? [];

    /// <summary>Creates the MCP management tools: query / load / unload / describe capabilities (read-only + restricted operations; configuration is immutable once loaded).</summary>
    public IList<AITool> CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(ListServers, "ListMcpServers"),
            AIFunctionFactory.Create(LoadServers, "LoadMcpServers"),
            AIFunctionFactory.Create(UnloadServer, "UnloadMcpServer"),
            AIFunctionFactory.Create(DescribeServer, "DescribeMcpServer"),
        ];
    }

    /// <summary>
    /// Exports a connected MCP server's tool capabilities as plain prompts — each tool's name and
    /// description — WITHOUT invoking the tools. Useful to describe to the user what a server can do
    /// before/after deciding to load or reconfigure it. The MCP tools are <see cref="AIFunction"/> /
    /// <see cref="AITool"/>, so each tool's <see cref="AITool.Description"/> is the standalone prompt.
    /// </summary>
    [Description("Exports a connected MCP server's tool capabilities as prompts (each tool's name and description) WITHOUT invoking the tools — so you can describe to the user what the server can do before deciding to load or reconfigure it. Pass the server name. For a server not yet connected, use ListMcpServers for status and the server Description.")]
    private string DescribeServer(
        [Description("Server name, e.g. \"Microsoft Learn\".")] string serverName)
    {
        var tools = _scope.GetServerTools(serverName);
        if (tools.Count == 0)
            return JsonConvert.SerializeObject(
                new { status = "error", message = $"Server '{serverName}' has no loaded tools (not connected)." }, Formatting.None);

        var arr = new JArray();
        foreach (var tool in tools)
        {
            var obj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
            };
            // An MCP tool's JSON Schema (parameter structure) can be exported from AIFunction's Declaration; here the name + description are sufficient as a prompt.
            arr.Add(obj);
        }
        return new JObject { ["status"] = "ok", ["server"] = serverName, ["toolCount"] = arr.Count, ["tools"] = arr }.ToString(Formatting.None);
    }

    /// <summary>
    /// Unloads a connected MCP server mid-session: removes its tools from the Agent's tool set
    /// (the next conversation call no longer sees them) and resets its status to NotStarted.
    /// The server can be loaded again later with <see cref="LoadServers"/>.
    /// </summary>
    [Description("Unloads a connected MCP server mid-session: removes its tools from the Agent's tool set and resets its status to NotStarted. Pass the server name. The server can be loaded again later via LoadMcpServers. Useful to free resources or drop a server no longer needed.")]
    private string UnloadServer(
        [Description("Server name to unload, e.g. \"Microsoft Learn\".")] string serverName)
    {
        var removed = _scope.UnloadServer(serverName);
        return JsonConvert.SerializeObject(
            new
            {
                status = removed ? "ok" : "not-found",
                message = removed ? $"Unloaded '{serverName}' — its tools are removed from the Agent tool set." : $"No loaded tools found for '{serverName}'.",
            },
            Formatting.None);
    }

    [Description("Lists the configured MCP servers and their current status: name, run mode, state (NotStarted/Installing/Connecting/Connected/Error), tool count, and error message. Also returns aggregate counts (connected/error). Pure query — call it first to see which servers are alive, still installing, connecting, or failed.")]
    private string ListServers()
    {
        var status = _scope.Status;
        var arr = new JArray();
        foreach (var s in status.Servers)
        {
            arr.Add(new JObject
            {
                ["name"] = s.Name,
                ["runMode"] = s.RunMode.ToString(),
                ["state"] = s.State.ToString(),
                ["stateText"] = s.StateText,
                ["toolCount"] = s.ToolCount,
                ["error"] = s.Error,
            });
        }

        return new JObject
        {
            ["status"] = "ok",
            ["serverCount"] = status.Servers.Count,
            ["connectedCount"] = status.ConnectedCount,
            ["errorCount"] = status.ErrorCount,
            ["servers"] = arr,
        }.ToString(Formatting.None);
    }

    /// <summary>
    /// Loads the host-registered MCP servers: local modes install their npm/pip runtime if needed
    /// then launch the process; remote modes connect over HTTP. This can take a while for a fresh
    /// local install — inform the user and prefer loading only the servers you actually need.
    /// </summary>
    [Description("Loads (installs if needed and connects) the host-registered MCP servers. By default loads ALL pre-registered servers; pass a JSON array of server names to load only those (e.g. [\"filesystem\"]). Local modes install npm/pip packages and launch the process; remote modes connect over HTTP. Can take a while for a fresh install. Returns the updated status. Only loads configurations the host pre-registered.")]
    private async Task<string> LoadServers(
        [Description("Optional JSON array of server names to load, e.g. [\"filesystem\"]. Empty or null loads all.")] string? namesJson = null,
        CancellationToken ct = default)
    {
        var subset = _servers;
        if (!string.IsNullOrWhiteSpace(namesJson))
        {
            try
            {
                var names = new HashSet<string>(
                    JArray.Parse(namesJson!)
                        .Select(t => t.Value<string>())
                        .OfType<string>()
                        .Where(n => !string.IsNullOrWhiteSpace(n)),
                    StringComparer.OrdinalIgnoreCase);
                subset = _servers.Where(s => names.Contains(s.Name)).ToArray();
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(
                    new { status = "error", message = $"Invalid names JSON: {ex.Message}" }, Formatting.None);
            }
        }

        if (subset.Count == 0)
            return JsonConvert.SerializeObject(
                new { status = "error", message = "No matching server(s) to load." }, Formatting.None);

        var tools = await _scope.LoadAsync(subset, ct);
        var loaded = _scope.Status.Servers
            .Where(s => subset.Any(c => string.Equals(c.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new
            {
                name = s.Name,
                state = s.State.ToString(),
                stateText = s.StateText,
                toolCount = s.ToolCount,
                error = s.Error,
            });

        return JsonConvert.SerializeObject(
            new { status = "ok", loadedToolCount = tools.Length, servers = loaded }, Formatting.None);
    }
}
