using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper;

public class AgentHelper : TreeHelper<TreeViewModel>
{
    // ── Qwen (DashScope) 配置 ──
    // Agent 功能需要阿里云百炼平台的 Qwen API Key。
    // 获取方式：访问 https://bailian.console.aliyun.com/cn-beijing?tab=doc#/doc
    //          注册/登录后在控制台创建 API Key，然后设置到以下环境变量中：
    //
    //   Windows PowerShell:  $env:DASHSCOPE_API_KEY = "sk-xxxx"
    //   Windows 永久生效:    [System.Environment]::SetEnvironmentVariable('DASHSCOPE_API_KEY','sk-xxxx','User')
    //   macOS / Linux:       export DASHSCOPE_API_KEY="sk-xxxx"
    //
    //   设置后需重启 Visual Studio / 终端才能生效。
    private const string EnvironmentVariableName = "DASHSCOPE_API_KEY";
    private const string Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    private const string Model = "qwen-plus";

    public ChatClientAgent? Agent;
    public AgentSession? Session;

    public async override void Install(IWorkflowTreeViewModel tree)
    {
        base.Install(tree);

        // 使能空间索引，-1 状态码意味着使能失败
        if (Component is not null && tree.EnableMap(240, Component.VisibleItems) > -1)
        {
            // 240 描述一个典型网格的大小，与节点的典型大小相匹配时能获得更好的性能
            // VisibleItems 是一个可通知集合，此处与Map绑定后，虚拟化的结果将同步给该集合
        }

        // 初始化Agent
        Agent = await ProvideAgent(tree, this);
        Session = await Agent.CreateSessionAsync();
    }

    public override void Uninstall(IWorkflowTreeViewModel tree)
    {
        base.Uninstall(tree);

        // 清理空间索引，5 状态码意味着合理的情况
        if (tree.ClearMap() == 5)
        {

        }

        Agent = null;
        Session = null;
    }

    /// <summary>
    /// Raised after each agent tool call. Subscribe from the View to trigger virtualization with a fresh viewport.
    /// </summary>
    public event Action? ToolCalled;

    public void Virtualize(Viewport viewport)
    {
        Component?.Virtualize(viewport); // 执行虚拟化
    }

    public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
    {
        // 以单个Tree作为Agent的接管范围（域）
        var scope = tree.AsAgentScope()
            // 在每个域中包含开发者自定义的工作流组件
            .WithComponents(AgentLanguages.English,
                typeof(NodeViewModel),
                typeof(ControllerViewModel),
                typeof(SlotViewModel),
                typeof(LinkViewModel),
                typeof(TreeViewModel))
            .WithEnums(AgentLanguages.English, [])
            .WithInterfaces(AgentLanguages.English, [])
            .WithToolCallCallback((_, _) => helper.ToolCalled?.Invoke());

        // 工作流框架级别上下文 + 开发者自定义级别上下文
        var contextPrompt = scope.ProvideAllContexts(AgentLanguages.English);

        // 创建MAF工具集：Agent可通过这些Functions自由操作Tree内部的所有组件
        var tools = scope.ProvideTools();

        var apiKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        var chatClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Endpoint)
            }).GetChatClient(string.IsNullOrWhiteSpace(Model) ? "qwen-plus" : Model)
              .AsIChatClient();

        var agent = chatClient.AsAIAgent(
            instructions: contextPrompt,
            tools: tools);

        return agent;
    }
}