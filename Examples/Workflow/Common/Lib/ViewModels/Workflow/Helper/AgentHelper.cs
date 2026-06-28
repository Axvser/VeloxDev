using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using VeloxDev.AI;
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

    /// <summary>
    /// Raised when the Agent calls the <c>RefreshVisualSlotAnchors</c> tool.
    /// Subscribe from the View layer to force all visible node views to re-sync slot anchor positions.
    /// </summary>
    public event Action? VisualRefreshRequested;

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

        // 渐进式上下文
        var contextPrompt = scope.ProvideProgressiveContextPrompt();

        // 创建MAF工具集
        var tools = scope.ProvideTools();

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

        var agent = chatClient.AsAIAgent(
            instructions: contextPrompt,
            tools: tools);

        return agent;
    }

    public override IWorkflowLinkViewModel CreateLink(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        return new LinkViewModel() { Sender = sender, Receiver = receiver };
    }
}
