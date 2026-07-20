# 创建智能体

> **💡V5.1.1已发生API变更，可前往【版本】章节查看**

```csharp
public static async Task<ChatClientAgent> ProvideAgent(IWorkflowTreeViewModel tree, AgentHelper helper)
{
    // 以单个Tree组件作为Agent的接管范围（域），你可使用 Fluent API 添加工作流需要的各种定义
    var scope = tree.AsAgentScope()
        .WithComponents(AgentLanguages.English,
            typeof(NodeViewModel),
            typeof(ControllerViewModel),
            typeof(SlotViewModel),
            typeof(LinkViewModel),
            typeof(TreeViewModel))
        .WithEnums(AgentLanguages.English, [])
        .WithInterfaces(AgentLanguages.English, [])
        .WithToolCallCallback((_, _) => helper.ToolCalled?.Invoke())
        .WithTools(
            "Your Prompt",
            AIFunctionFactory.Create(Your Agent Tools)
                  ));

    // 渐进式上下文
    var contextPrompt = scope.ProvideProgressiveContextPrompt(AgentLanguages.English);

    // 创建MAF工具集
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
```