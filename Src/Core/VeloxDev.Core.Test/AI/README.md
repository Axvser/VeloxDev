# AI 测试

针对 `VeloxDev.AI` 命名空间的单元测试 —— 框架无关的通用 Agent 工具集。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `AgentContextReaderTests.cs` | `AgentContextReader` | 多语言 `[AgentContext]` 类型/成员读取、`HasAgentContext` 判断 |
| `AgentToolCallEventArgsTests.cs` | `AgentToolCallEventArgs` | 构造函数属性设置、null ToolName 异常、null Result 默认空串、EventArgs 继承 |
| `AgentLanguagesTests.cs`
| `AgentTypeResolverTests.cs` | `AgentTypeResolver` | 已知类型/跨程序集/null/空/不存在类型的解析 |
| `AgentPropertyAccessorTests.cs` | `AgentPropertyAccessor` | 属性发现/读取/设置/批量补丁/标量复制、过滤器、拒绝集、null 安全 |
| `AgentMethodInvokerTests.cs` | `AgentMethodInvoker` | 方法发现/调用/默认参数/void/返回值、null/方法不存在 |
| `AgentCommandDiscovererTests.cs` | `AgentCommandDiscoverer` | 命令发现/执行/CanExecute/FindBackingCommand、`[AgentContext]` 描述读取 |
