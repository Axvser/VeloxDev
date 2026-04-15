using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

namespace Demo.ViewModels.Workflow.Helper;

public class AgentHelper : TreeHelper<TreeViewModel>
{
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
        Agent = await ProvideAgent(tree);
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

    public void Virtualize(Viewport viewport) 
    {
        Component?.Virtualize(viewport); // 执行虚拟化
    }

    public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree)
    {
        // 以单个Tree作为Agent的接管范围（域）
        var scope = tree.AsAgentScope()
            // 在每个域中包含开发者自定义的工作流组件
            .WithComponents(AgentLanguages.Chinese,
                typeof(NodeViewModel),
                typeof(ControllerViewModel),
                typeof(SlotViewModel),
                typeof(LinkViewModel),
                typeof(TreeViewModel))
            .WithEnums(AgentLanguages.English, [])
            .WithInterfaces(AgentLanguages.Dutch, []);

        // 工作流框架级别上下文
        var framework = scope.ProvideFrameworkContext(AgentLanguages.English);
        // 开发着自定义级别上下文
        var customer = scope.ProvideCustomerContext(AgentLanguages.English);

        var apiKey = Environment.GetEnvironmentVariable(EnvironmentVariableName);

#pragma warning disable OPENAI001
        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(Endpoint)
            }).GetResponsesClient();
#pragma warning restore OPENAI001

        var agent = client.AsAIAgent(
            model: string.IsNullOrWhiteSpace(Model) ? "qwen-plus" : Model,
            tools: []);

        return agent;
    }
}