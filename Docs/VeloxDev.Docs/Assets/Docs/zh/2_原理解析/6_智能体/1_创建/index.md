# 创建智能体

绑定到任意 `IChatClient`（如 OpenAI、Azure OpenAI、DeepSeek）。

```csharp
var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery("VeloxDev.Core")
    .WithMaxToolCalls(200);

var tools = scope.ProvideTools();

// 绑定到 LLM
var agent = chatClient.AsAIAgent(
    instructions: scope.ProvideProgressiveContextPrompt(),
    tools: tools);

var response = await agent.RunAsync("连接节点A的输出到节点B的输入");
```

MAF 框架自动将 `[VeloxProperty]`、`[AgentCommandParameter]` 等注解转换为 LLM 可理解的工具定义。
