using Microsoft.Agents.AI;
using VeloxDev.AI.Pipelines;
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
using VeloxDev.AI.Skills;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

public class AgentHelper() : TreeHelper<TreeViewModel>(200)
{
    private const string EnvironmentVariableName = "API_KEY_DEEPSEEK";
    private const string Endpoint = "https://api.deepseek.com";
    private const string Model = "deepseek-v4-flash";

    // AIAgent rather than ChatClientAgent: the pipeline attaches as agent middleware, and that wrapper is
    // what the host now calls. Nothing here reads a ChatClientAgent-only member.
    public AIAgent? Agent;
    public AgentSession? Session;

    /// <summary>
    /// The conversation the Agent reports into — every turn, its answer, the reasoning behind it, and the
    /// tools it used, in order.
    /// <para>
    /// Maintained by the pipeline rather than by this host: the view binds
    /// <see cref="AgentTranscript.ToMarkdown"/> for a rich panel, or <see cref="AgentTranscript.Entries"/>
    /// to render its own.
    /// </para>
    /// </summary>
    public AgentTranscript Transcript { get; } = new();

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

    // The tool set is no longer assembled here. The scope's context provider renders it on every
    // invocation, which is what lets a skill switched off, a custom tool registered late, or an MCP
    // server loaded mid-session reach the model on the very next turn without rebuilding the agent.

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

    /// <summary>
    /// Records a tool call in the tree's structured transcript so the chat panel can render it collapsed.
    /// The tool-call callback fires from inside the marshalled call, so this already runs on the UI thread
    /// — which is what makes it safe to append to a bound collection.
    /// </summary>
    internal void RecordToolCall(AgentToolCallEventArgs args)
        => Component?.AppendToolCall(args.ToolName, args.Result);

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

    public static async Task<AIAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
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
                helper.RecordToolCall(args);
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
            })
            // Skills become individually switchable. The library's own prompt documents arrive with the
            // scope as embedded skills; "skills" is a disk root (resolved against the app base directory)
            // where an application drops its own Agent Skills folders — it is allowed not to exist yet.
            .WithSkills("skills")
            // MCP servers join the tool set per turn, so load/unload takes effect on the next turn.
            .WithMcps(helper.Mcp);

        // The configurations the Agent may load by name. Registering them on the scope — rather than only
        // handing them to a toolkit — is what lets LoadMcpServers bring one back after it was unloaded.
        helper.Mcp.WithServers([.. helper.McpServers]);

        // Interaction-tool aggressiveness 0~3
        scope.WithInteractionSafety(helper.InteractionSafety);
        // Register custom safety-level prompt overrides (applies to levels 1~3 only)
        foreach (var kvp in helper.InteractionSafetyPrompts)
            scope.WithInteractionSafetyPrompt(kvp.Key, kvp.Value);

        // The MCP and skill management tools are no longer registered here. Each subsystem's context
        // provider contributes its own tools and its own prompt text on every turn, so a host attaches the
        // subsystem and is done. Registering them here as well would duplicate every one of them — the
        // framework unions tool lists without deduplicating by name.
        //
        // This demo stays at McpSelfServiceLevel.Closed, so the Agent may load/unload/inspect the servers
        // below but never author one. Raising the level via Mcp.WithSelfService(...) adds AddMcpServer and
        // changes what the prompt says the model may do; see McpSelfServiceLevel.

        // Attach the conversation before the agent exists: the scope composes its stage chain from this, and
        // the agent below is wrapped with that chain — so every turn's answer, reasoning and tool calls land
        // in the transcript without this host looping the stream itself.
        scope.WithTranscript(helper.Transcript);

        // Progressive context: the static skeleton. Skills put the scope under dynamic management, so the
        // provider renders them per turn instead — the two must not both carry the corpus.
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

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

        // The static skeleton is the agent's own instructions; everything that changes — skills, the tool
        // set, connected MCP servers — is contributed per invocation by the context providers. Tools are
        // deliberately NOT passed through ChatOptions: the framework unions that list with the providers'
        // without deduplicating, so the same tool offered through both channels reaches the model twice.
        var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = contextPrompt },
            AIContextProviders = scope.CreateContextProviders(),
        });

        // The pipeline goes in the framework's own middleware slot, which wraps the whole run — so both
        // RunAsync and RunStreamingAsync are observed, and every caller above keeps calling them unchanged.
        return agent.WithPipeline(scope.Pipeline);
    }

    public override IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        return new LinkViewModel() { Sender = sender, Receiver = receiver };
    }
}
