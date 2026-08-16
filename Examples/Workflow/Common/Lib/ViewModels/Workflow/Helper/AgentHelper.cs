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
    /// 全局 MCP 服务器加载器与状态（Agent 与 UI 共享）：WorkflowView 绑定其 Status 展示面板，
    /// Agent 经 McpAgentToolkit（ListMcpServers / LoadMcpServers）管理。
    /// </summary>
    public McpScope Mcp { get; } = new();

    /// <summary>宿主预注册的 MCP 服务器配置（Agent 只能加载这些，不能任意构造）。
    /// 安全模型：配置在加载时确定一次，之后不可变更——Agent 只能加载/卸载/查看，不能中途改配置。</summary>
    public IReadOnlyList<McpServerConfiguration> McpServers { get; set; } = DemoMcpServers;

    private static readonly McpServerConfiguration[] DemoMcpServers =
    [
        new()
        {
            Name = "Microsoft Learn",
            Description = "微软官方文档检索（远程 Streamable HTTP）",
            RunMode = McpServerRunMode.Http,
            Endpoint = "https://learn.microsoft.com/api/mcp",
            Options = new { connectionTimeout = 30 },   // 秒
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

    /// <summary>加载全部预注册的 MCP 服务器（状态经 <see cref="Mcp"/> 实时驱动）。</summary>
    public async Task LoadMcpServersAsync() => await Mcp.LoadAsync(McpServers);

    // ── 动态工具集（Agent 会话中途加/移除 MCP 工具）────────────────────────

    private readonly List<AITool> _baseTools = [];

    /// <summary>
    /// 固定工具集（工作流工具 + MCP 管理工具），在 <see cref="ProvideAgent"/> 时创建一次。
    /// </summary>
    internal void SetBaseTools(IEnumerable<AITool> tools)
    {
        _baseTools.Clear();
        _baseTools.AddRange(tools);
    }

    /// <summary>
    /// 组装本次对话的运行选项：基础工具 + 当前已连接的 MCP 服务器工具。
    /// 每次对话调用都重新组装——服务器中途加载/卸载后，下一次对话即生效（无需重建 Agent）。
    /// </summary>
    public ChatClientAgentRunOptions BuildRunOptions()
        => new() { ChatOptions = new ChatOptions { Tools = [.. _baseTools, .. Mcp.LoadedTools] } };

    public async override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);

        // 初始化Agent
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

    #pragma warning disable CS0067 // 外部订阅者使用
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
        // 创建独立的工作空间
        var scope = tree.AsAgentScope()
            .WithPromptLanguage(AgentLanguages.English)   // 默认提示词语言
            .WithOutputLanguage(AgentLanguages.Chinese)   // 默认输出语言
            // 从程序集自动发现组件
            .WithAutoDiscovery(assemblyName: "VeloxDev.Core")
            .WithAutoDiscovery(assemblyName: "Lib") 
            .WithAutoMarkDirty(false)               // 视图是否自动标记为脏
            .WithMaxToolCalls(200)                  // 最大工具调用数
            .WithAllowNodeExecution(true)           // 显式允许 Agent 执行节点业务代码（安全默认关闭，演示需要）
            .WithSynchronizationContext(SynchronizationContext.Current) // 工具调用 marshal 到 UI 线程（组件是 UI 绑定）
            .WithToolCallCallback(args =>           // 工具调用回调
            {
                helper.ToolCalled?.Invoke();
                return Task.CompletedTask;
            })
            .WithSelectionHandler(async args => // Agent询问用户执行哪一项操作
            {
                if (helper.SelectionHandler is not null)
                    await helper.SelectionHandler(args);
            })
            .WithConfirmationHandler(async args => // Agent向用户确认操作权限
            {
                if (helper.ConfirmationHandler is not null)
                    await helper.ConfirmationHandler(args);
            });

        // 交互工具激进程度 0~3
        scope.WithInteractionSafety(helper.InteractionSafety);
        // 注册自定义安全等级提示词覆盖（仅对 1~3 档生效）
        foreach (var kvp in helper.InteractionSafetyPrompts)
            scope.WithInteractionSafetyPrompt(kvp.Key, kvp.Value);

        // 注册 MCP 服务器管理工具：Agent 可列出状态、加载（需要时安装并连接）、卸载、描述能力。
        // 安全模型：服务器配置在加载时确定一次、之后不可变更；Agent 只能加载/卸载/查看，不能中途改配置。
        // 工具集在每次对话时按当前加载状态动态组装。
        scope.WithTools(
            "MCP 服务器管理工具：ListMcpServers 查看各 MCP 服务器的存活/安装中/连接中/错误状态与工具数；" +
            "DescribeMcpServer 导出某台已连接服务器的工具能力提示词（不激活工具），用于向用户说明它能做什么；" +
            "LoadMcpServers 加载（需要时安装并连接）宿主预注册的服务器；" +
            "UnloadMcpServer 中途移除某台服务器（其工具从下一次对话的工具集消失，可再加载）。" +
            "服务器配置由宿主在加载时确定一次、之后不可变更——不要尝试修改或重新配置服务器（目录集等）。" +
            "加载本地服务器会安装 npm/pip 运行时，可能耗时——先向用户确认再调用。",
            [.. new McpAgentToolkit(helper.Mcp, helper.McpServers).CreateTools()]);

        // 渐进式上下文
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

        // 创建MAF工具集（固定部分），并保存为“基础工具”；MCP 服务器工具在每次对话时
        // 经 BuildChatOptions 动态并入，因此服务器中途加载/卸载无需重建 Agent。
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
