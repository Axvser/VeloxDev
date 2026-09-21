using Microsoft.Extensions.AI;
using VeloxDev.AI.Pipelines;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI.MCP;

/// <summary>
/// MCP management tools callable by the Agent: list server status, load (install and connect) the
/// host pre-registered servers. Server configuration is provided in advance by the host
/// (<see cref="McpServerConfiguration"/>); the Agent does not construct arbitrary configs — the
/// security boundary matches the workflow tools: "host registers, Agent operates".
/// <para>
/// Reach the model through <see cref="McpScope.CreateContextProvider"/>, which contributes these tools
/// — and every connected server's — already wrapped so they obey the composing host's policy. Use
/// <see cref="CreateTools(ToolPipeline)"/> directly only when assembling providers by hand; the
/// parameterless <see cref="CreateTools()"/> returns them unwrapped, with no marshalling or accounting.
/// </para>
/// </summary>
public sealed class McpAgentToolkit(McpScope scope, IReadOnlyList<McpServerConfiguration> servers)
{
    private readonly McpScope _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IReadOnlyList<McpServerConfiguration> _servers = servers ?? [];

    /// <summary>
    /// Names of the management tools, in registration order. Exposed so a host composing several tool
    /// sources can classify them without repeating the literals.
    /// <para>
    /// A granted view (see <see cref="McpScope.CreateGrantedView"/>) registers only
    /// <see cref="ListName"/> and <see cref="DescribeName"/> — the two that read. The other two are in this
    /// array because they are names the host needs to recognise, not because every scope registers them.
    /// </para>
    /// </summary>
    public static readonly string[] ToolNames =
        ["ListMcpServers", "LoadMcpServers", "UnloadMcpServer", "DescribeMcpServer"];

    /// <summary>Read-only: lists server state.</summary>
    public const string ListName = "ListMcpServers";

    /// <summary>Read-only: exports a server's tool-capability prompt.</summary>
    public const string DescribeName = "DescribeMcpServer";

    /// <summary>
    /// Name of the tool that lets the model add a server of its own. Registered only when the scope's
    /// <see cref="McpScope.SelfServiceLevel"/> has been opened past
    /// <see cref="McpSelfServiceLevel.Closed"/>.
    /// </summary>
    public const string AddToolName = "AddMcpServer";

    /// <summary>
    /// Creates the MCP management tools: query / load / unload / describe capabilities. Adding a server
    /// the host did not pre-register is a separate opt-in — see
    /// <see cref="McpScope.WithSelfService"/> — and its tool is registered only once the host has opened
    /// that gate.
    /// <para>
    /// A granted view registers the two reading tools only. It is the MCP surface handed to a spawned
    /// child, and a child runs while its parent's turn is suspended: loading installs and launches
    /// software, and unloading tears down a connection the parent may still be using. Neither is a
    /// decision a background child gets to make, so neither tool exists on its scope.
    /// </para>
    /// </summary>
    public IList<AITool> CreateTools()
    {
        var tools = new List<AITool> { AIFunctionFactory.Create(ListServers, ListName) };

        if (!_scope.IsGrantedView)
        {
            tools.Add(AIFunctionFactory.Create(LoadServers, ToolNames[1]));
            tools.Add(AIFunctionFactory.Create(UnloadServer, ToolNames[2]));
        }

        tools.Add(AIFunctionFactory.Create(DescribeServer, DescribeName));

        // At Closed the tool is absent rather than present-and-refusing: a tool the model can see but
        // never use only wastes prompt budget and invites retries.
        if (_scope.SelfServiceLevel != McpSelfServiceLevel.Closed && !_scope.IsGrantedView)
            tools.Add(AIFunctionFactory.Create(AddServer, AddToolName));

        return tools;
    }

    /// <summary>
    /// Creates the management tools wrapped so that every call obeys <paramref name="policy"/> —
    /// marshalled onto the host's thread, gated, and reported afterwards. This is what a context provider
    /// contributes; registering the unwrapped set by hand gets none of it.
    /// </summary>
    public IList<AITool> CreateTools(ToolPipeline tools, AgentPipeline? pipeline = null)
    {
        if (tools is null) throw new ArgumentNullException(nameof(tools));
        return [.. CreateTools().Select(tool =>
            tool is AIFunction function ? (AITool)new TrackedAIFunction(function, tools, pipeline) : tool)];
    }

    /// <summary>
    /// The prompt text describing these tools.
    /// <para>
    /// Generated rather than fixed, because what the model may do with MCP depends on the host's
    /// <see cref="McpScope.SelfServiceLevel"/>: a paragraph promising that servers cannot be added would
    /// be a lie at every level above <see cref="McpSelfServiceLevel.Closed"/>, and one inviting the model
    /// to add them would be a lie at it. A granted view forks off the same way — it is told to use the
    /// servers it has rather than named tools it does not hold.
    /// </para>
    /// </summary>
    public string BuildPromptContext()
    {
        if (_scope.IsGrantedView)
            return "MCP server tools: ListMcpServers shows each server's state and tool count; "
                 + "DescribeMcpServer exports a connected server's tool-capability prompt (without activating the tools). "
                 + "The servers listed are the whole of your MCP surface — loading, unloading and adding servers are the "
                 + "host's decisions, no tool to take any of them exists here, and asking for one will not produce it. "
                 + "Use the tools the servers offer.";

        var sb = new StringBuilder();
        sb.Append("MCP server management tools: ListMcpServers shows each server's alive/installing/connecting/error state and tool count; ");
        sb.Append("DescribeMcpServer exports a connected server's tool-capability prompt (without activating the tools) so you can tell the user what it can do; ");
        sb.Append("LoadMcpServers loads the servers the host pre-registered (installing and connecting when needed); ");
        sb.Append("UnloadMcpServer removes a server mid-session — its tools leave the next turn's tool set, and it can be loaded again. ");
        sb.Append(_scope.SelfServiceLevel switch
        {
            McpSelfServiceLevel.Closed =>
                "You cannot add a server yourself: the configuration is fixed by the host, and no tool to author one exists. "
                + "Do not attempt to modify or reconfigure servers.",
            McpSelfServiceLevel.RemoteConfirmed =>
                "AddMcpServer can connect a REMOTE (Http) server, but only after the user confirms it — ask them first, and expect a refusal if they decline. "
                + "Locally launched servers must be pre-registered by the host.",
            McpSelfServiceLevel.AllConfirmed =>
                "AddMcpServer can connect a remote or a locally launched server, but only after the user confirms it — ask them first, and expect a refusal if they decline.",
            _ =>
                "AddMcpServer can connect any server without asking. Prefer the servers the host pre-registered, and add one of your own only when the task needs it.",
        });
        sb.Append(" Loading a local server installs npm/pip runtimes and may take time — confirm with the user before calling.");
        return sb.ToString();
    }

    /// <summary>
    /// Adds a server the host did not pre-register and connects it. Available only when the host raised
    /// <see cref="McpScope.SelfServiceLevel"/>; local run modes (which install and launch a package)
    /// require the higher rungs, and the rungs below <see cref="McpSelfServiceLevel.Unrestricted"/>
    /// require the user's agreement.
    /// </summary>
    [Description("Adds and connects an MCP server that the host did not pre-register, then makes its tools available for the rest of the session. The host must have enabled this. Run modes: 'Http' for a remote server (give endpoint), or 'Npx'/'Npm'/'Pip'/'Uvx'/'Dotnet'/'Exe' for a server launched locally (give package) — local modes install and start software on the user's machine, so they may be refused outright or require confirmation. Ask the user first when the server is one they did not mention.")]
    private async Task<string> AddServer(
        [Description("Server name. Must not already be registered or connected.")] string name,
        [Description("Run mode: 'Http' for remote, or 'Npx','Npm','Pip','Uvx','Dotnet','Exe' for local.")] string runMode,
        [Description("Remote mode only: the endpoint URL.")] string? endpoint = null,
        [Description("Local modes only: the package name, or the path for Dotnet/Exe.")] string? package = null,
        [Description("Optional JSON array of command-line arguments, e.g. [\"C:/data\"].")] string? argumentsJson = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error("A server name is required.");
        if (!Enum.TryParse<McpServerRunMode>(runMode, true, out var mode))
            return Error($"Unknown run mode '{runMode}'. Valid: {string.Join(", ", Enum.GetNames(typeof(McpServerRunMode)))}.");
        if (!_scope.CanAddServer(mode))
        {
            var isLocal = mode != McpServerRunMode.Http;
            return Error(isLocal
                ? $"Adding a local '{mode}' server is disabled by host policy. Only remote (Http) servers may be added at the current level, and local packages must be pre-registered by the host."
                : "Adding MCP servers is disabled by host policy. The host must enable it via WithSelfService.");
        }

        if (mode == McpServerRunMode.Http && string.IsNullOrWhiteSpace(endpoint))
            return Error("An 'endpoint' URL is required for an Http server.");
        if (mode != McpServerRunMode.Http && string.IsNullOrWhiteSpace(package))
            return Error($"A 'package' is required for a {mode} server.");

        string[] arguments = [];
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            try
            {
                arguments = [.. JArray.Parse(argumentsJson!).Select(t => t.Value<string>()).OfType<string>()];
            }
            catch (Exception ex)
            {
                return Error($"Invalid arguments JSON: {ex.Message}");
            }
        }

        var config = new McpServerConfiguration
        {
            Name = name,
            RunMode = mode,
            Description = $"Added by the Agent{(mode == McpServerRunMode.Http ? $" ({endpoint})" : $" ({package})")}.",
            Endpoint = endpoint,
            Package = package ?? string.Empty,
            Arguments = arguments,
        };

        if (_scope.RequiresConfirmationToAdd())
        {
            var description = mode == McpServerRunMode.Http
                ? $"Connect the Agent to the remote MCP server '{name}' at {endpoint}. Its tools become available for the rest of this session."
                : $"Install (if needed) and launch the local MCP server '{name}' from package '{package}' on this machine, and give the Agent its tools for the rest of this session.";
            // Awaited without ConfigureAwait: this tool is registered through the scope, so it runs on the
            // UI thread, and everything after this point reads the UI-bound status collection.
            if (!await _scope.ConfirmationResolver($"mcp-add:{name}", description))
                return JsonConvert.SerializeObject(
                    new { status = "denied", message = "The user declined to add this server. Do not retry without asking them." },
                    Formatting.None);
        }

        var tools = await _scope.AddAsync(config, ct);
        var status = _scope.Status.Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        return JsonConvert.SerializeObject(new
        {
            status = tools.Length > 0 ? "ok" : "error",
            server = name,
            runMode = mode.ToString(),
            toolCount = tools.Length,
            state = status?.State.ToString(),
            error = status?.Error,
            message = tools.Length > 0
                ? $"'{name}' connected — its {tools.Length} tool(s) are available now."
                : $"'{name}' could not be connected. See 'error'.",
        }, Formatting.None);
    }

    private static string Error(string message)
        => JsonConvert.SerializeObject(new { status = "error", message }, Formatting.None);

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
        // A server switched off by the host still has its tools loaded, but they are not offered to the
        // model — describing them here would advertise a capability this turn does not carry.
        if (!_scope.IsServerEnabled(serverName))
            return JsonConvert.SerializeObject(
                new { status = "error", message = $"Server '{serverName}' is switched off by host policy; its tools are not offered. The host can switch it back on." }, Formatting.None);

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
    [Description("Unloads a connected MCP server mid-session: removes its tools from the Agent's tool set, resets its status to NotStarted, and tears down the connection (for a locally-launched server this terminates its process). Pass the server name. The server can be loaded again later via LoadMcpServers. Useful to free resources or drop a server no longer needed.")]
    private async Task<string> UnloadServer(
        [Description("Server name to unload, e.g. \"Microsoft Learn\".")] string serverName)
    {
        var removed = await _scope.UnloadServerAsync(serverName);
        return JsonConvert.SerializeObject(
            new
            {
                status = removed ? "ok" : "not-found",
                message = removed ? $"Unloaded '{serverName}' — its tools are removed from the Agent tool set." : $"No loaded tools found for '{serverName}'.",
            },
            Formatting.None);
    }

    [Description("Lists the configured MCP servers and their current status: name, run mode, state (NotStarted/Installing/Connecting/Connected/Error), tool count, whether the host has switched it on, and error message. A server can be connected but switched off by the host — its tools are then NOT available to you even though it is alive. Also returns aggregate counts (connected/error). Pure query — call it first to see which servers are alive, still installing, connecting, or failed.")]
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
                // Reported as offered-tools, not loaded-tools: a switched-off server keeps its count but
                // hands the model nothing, and saying "3 tools" here would contradict the tool set.
                ["enabled"] = s.IsEnabled,
                ["toolCount"] = s.IsEnabled ? s.ToolCount : 0,
                ["loadedToolCount"] = s.ToolCount,
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
