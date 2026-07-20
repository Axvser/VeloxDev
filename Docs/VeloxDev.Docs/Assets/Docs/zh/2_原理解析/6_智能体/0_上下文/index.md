# 渐进式上下文

`WorkflowAgentScope` 提供渐进式上下文提示，帮助 LLM 理解工作流拓扑。

```csharp
var scope = tree.AsAgentScope()
    .WithPromptLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery("VeloxDev.Core");

var contextPrompt = scope.ProvideProgressiveContextPrompt();
// 返回包含当前图拓扑的提示文本
// - 节点列表（类型、属性、位置）
// - 连接关系
// - 可用工具描述
```

`WithAutoDiscovery(assembly)` 自动扫描程序集中的 `[AgentContext]` 和 `[AgentCommandParameter]` 注解。
