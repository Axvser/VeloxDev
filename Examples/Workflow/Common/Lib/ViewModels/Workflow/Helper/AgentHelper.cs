using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.MCP;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class AgentHelper() : TreeHelper<TreeViewModel>(200)
{
    private const string EnvironmentVariableName = "API_KEY_DEEPSEEK";
    private const string Endpoint = "https://api.deepseek.com";
    private const string Model = "deepseek-v4-flash";

    public ChatClientAgent? Agent;
    public AgentSession? Session;

    /// <summary>
    /// Global MCP server loader and status (shared by the Agent and the UI): WorkflowView binds its Status panel,
    /// and the Agent manages servers through McpAgentToolkit (ListMcpServers / LoadMcpServers).
    /// </summary>
    public McpScope Mcp { get; } = new();

    /// <summary>MCP server configurations pre-registered by the host (the Agent may only load these, never construct arbitrary ones).
    /// Security model: configuration is fixed once at load time and cannot change afterwards — the Agent can only load/unload/inspect, never reconfigure mid-session.</summary>
    public IReadOnlyList<McpServerConfiguration> McpServers { get; set; } = DemoMcpServers;

    private static readonly McpServerConfiguration[] DemoMcpServers =
    [
        new()
        {
            Name = "Microsoft Learn",
            Description = "微软官方文档检索（远程 Streamable HTTP）",
            RunMode = McpServerRunMode.Http,
            Endpoint = "https://learn.microsoft.com/api/mcp",
            Options = new { connectionTimeout = 30 },   // seconds
        },
        new()
        {
            Name = "Remote 示例",
            Description = "远程 Streamable HTTP 服务器",
            RunMode = McpServerRunMode.Http,
            Endpoint = "https://mcp.example.invalid/mcp",
            Options = new
            {
                connectionTimeout = 8,
                headers = new { Authorization = "Bearer demo-token" },
            },
        },
        new()
        {
            Name = "Filesystem (npx)",
            Description = "本地 npx 启动的文件系统服务器",
            RunMode = McpServerRunMode.Npx,
            Package = "@modelcontextprotocol/server-filesystem",
            Arguments = [AppContext.BaseDirectory],
            Options = new { env = new { FILESYSTEM_ROOT = AppContext.BaseDirectory } },
        },
    ];

    /// <summary>Loads all pre-registered MCP servers (status is driven live through <see cref="Mcp"/>).</summary>
    public async Task LoadMcpServersAsync() => await Mcp.LoadAsync(McpServers);

    // ── Dynamic tool set (add/remove MCP tools mid-session) ──────────────────────

    private readonly List<AITool> _baseTools = [];

    /// <summary>
    /// Fixed tool set (workflow tools + MCP management tools), created once in <see cref="ProvideAgent"/>.
    /// </summary>
    internal void SetBaseTools(IEnumerable<AITool> tools)
    {
        _baseTools.Clear();
        _baseTools.AddRange(tools);
    }

    /// <summary>
    /// Assembles the run options for this conversation: base tools + tools of the currently connected MCP servers.
    /// Re-assembled on every conversation call — servers loaded/unloaded mid-session take effect on the next conversation (no Agent rebuild needed).
    /// </summary>
    public ChatClientAgentRunOptions BuildRunOptions()
        => new() { ChatOptions = new ChatOptions { Tools = [.. _baseTools, .. Mcp.LoadedTools] } };

    public async override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);

        // Initialize the agent
        Agent = await ProvideAgent(tree, this);
        Session = await Agent.CreateSessionAsync();
    }

    public override void Uninstall(IWorkflowTreeViewModel tree)
    {
        base.Uninstall(tree);

        Agent = null;
        Session = null;
    }

    /// <summary>
    /// Raised after each agent tool call. Subscribe from the View to trigger virtualization with a fresh viewport.
    /// </summary>
    public event Action? ToolCalled;

    #pragma warning disable CS0067 // used by external subscribers
    /// <summary>
    /// Raised when the Agent calls the <c>RefreshVisualSlotAnchors</c> tool.
    /// Subscribe from the View layer to force all visible node views to re-sync slot anchor positions.
    /// </summary>
    public event Action? VisualRefreshRequested;
#pragma warning restore CS0067

    /// <summary>
    /// Set by the View layer to handle <c>RequestSelection</c> tool calls.
    /// Receives an <see cref="AgentSelectionEventArgs"/> with prompt and options;
    /// set <see cref="AgentSelectionEventArgs.SelectedOption"/> before completing.
    /// When <c>null</c>, the selection tool is not registered.
    /// </summary>
    public Func<AgentSelectionEventArgs, Task>? SelectionHandler { get; set; }

    /// <summary>
    /// Set by the View layer to handle <c>RequestConfirmation</c> tool calls.
    /// Receives an <see cref="AgentConfirmationEventArgs"/> with operation key and description;
    /// set <see cref="AgentConfirmationEventArgs.Result"/> before completing.
    /// When <c>null</c>, the confirmation tool is not registered.
    /// </summary>
    public Func<AgentConfirmationEventArgs, Task>? ConfirmationHandler { get; set; }

    /// <summary>
    /// Controls how aggressively the Agent uses interaction tools (0–3).
    /// 0 = fully autonomous; 1 = cautious (default); 2 = balanced; 3 = strict.
    /// </summary>
    public int InteractionSafety { get; set; } = 3;

    /// <summary>
    /// Optional custom prompt body text per safety level (1–3).
    /// When set, replaces the built-in default text for that level in the system prompt.
    /// Level 0 is always the built-in silent rule and cannot be overridden.
    /// </summary>
    public Dictionary<int, string> InteractionSafetyPrompts { get; } = [];

    public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
    {
        // Create an isolated workspace
        var scope = tree.AsAgentScope()
            .WithPromptLanguage(AgentLanguages.English)   // default prompt language
            .WithOutputLanguage(AgentLanguages.Chinese)   // default output language
            // Auto-discover components from assemblies
            .WithAutoDiscovery(assemblyName: "VeloxDev.Core")
            .WithAutoDiscovery(assemblyName: "Lib") 
            .WithAutoMarkDirty(false)               // whether the view auto-marks itself dirty
            .WithMaxToolCalls(200)                  // maximum tool call count
            .WithAllowNodeExecution(true)           // explicitly allow the Agent to run node business code (safely off by default; the demo needs it)
            .WithSynchronizationContext(SynchronizationContext.Current) // marshal tool calls to the UI thread (components are UI-bound)
            .WithToolCallCallback(args =>           // tool-call callback
            {
                helper.ToolCalled?.Invoke();
                return Task.CompletedTask;
            })
            .WithSelectionHandler(async args => // the Agent asks the user which action to perform
            {
                if (helper.SelectionHandler is not null)
                    await helper.SelectionHandler(args);
            })
            .WithConfirmationHandler(async args => // the Agent asks the user to confirm operation permissions
            {
                if (helper.ConfirmationHandler is not null)
                    await helper.ConfirmationHandler(args);
            });

        // Interaction-tool aggressiveness 0~3
        scope.WithInteractionSafety(helper.InteractionSafety);
        // Register custom safety-level prompt overrides (applies to levels 1~3 only)
        foreach (var kvp in helper.InteractionSafetyPrompts)
            scope.WithInteractionSafetyPrompt(kvp.Key, kvp.Value);

        // Register MCP server management tools: the Agent can list status, load (install and connect if needed),
        // unload, and describe capabilities.
        // Security model: server configuration is fixed once at load time and cannot change afterwards;
        // the Agent can only load/unload/inspect, never reconfigure mid-session.
        // The tool set is assembled per conversation from the current load state.
        scope.WithTools(
            "MCP server management tools: ListMcpServers shows each MCP server's alive/installing/connecting/error state and tool count; " +
            "DescribeMcpServer exports a connected server's tool-capability prompt (without activating the tools) so you can tell the user what it can do; " +
            "LoadMcpServers loads host pre-registered servers (installing and connecting when needed); " +
            "UnloadMcpServer removes a server mid-session (its tools disappear from the next conversation's tool set; it can be loaded again). " +
            "Server configuration is fixed once by the host at load time and cannot change afterwards — do not attempt to modify or reconfigure servers (directory sets, etc.). " +
            "Loading a local server installs npm/pip runtimes and may take time — confirm with the user before calling.",
            [.. new McpAgentToolkit(helper.Mcp, helper.McpServers).CreateTools()]);

        // Progressive context
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

        // Create the MAF tool set (fixed part) and save it as the "base tools"; MCP server tools are merged in
        // dynamically per conversation via BuildChatOptions, so servers loaded/unloaded mid-session need no Agent rebuild.
        helper.SetBaseTools(scope.ProvideTools());

        var apiKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{EnvironmentVariableName}' is not configured.");
        }

        var chatClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Endpoint)
            }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "deepseek-v4-flash" : Model)
              .AsIChatClient();

        var agent = chatClient.AsAIAgent(instructions: contextPrompt);

        return agent;
    }

    public override IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        return new LinkViewModel() { Sender = sender, Receiver = receiver };
    }
}
