# VeloxDev.Core.Extension

VeloxDev.Core.Extension 为 `VeloxDev.Core` 提供两个实用扩展：

- ViewModel 的 JSON（基于 `Newtonsoft.Json`）序列化/反序列化，保留运行时类型、处理对象引用并支持复杂字典。
- 面向 Agent 的 Workflow 上下文收集。

以下示例展示如何快速上手。

1) ViewModel JSON 序列化

```csharp
// 同步
var json = workflow.Serialize();
if (json.TryDeserialize<MyWorkflowTree>(out var tree)) { /* use */ }

// 异步
var jsonAsync = await workflow.SerializeAsync();
var (ok, tree2) = await jsonAsync.TryDeserializeAsync<MyWorkflowTree>();

// 流式
await using var ws = File.Create("wf.json");
await workflow.SerializeToStreamAsync(ws);
```

2) 为 Agent 暴露类型上下文

```csharp
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
```