# VeloxDev.Core.Extension

> **MAF（Microsoft.Extensions.AI）的 Workflow Agent + MCP 支持** —— 构建"AI 可控制的视觉工作流编辑器"的扩展库，是依赖零碎的 [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) 的可选伴侣。适配 WPF / Avalonia / WinUI / MAUI / WinForms / Blazor。

## 包内容

| 模块 | 说明 |
|---|---|
| **Workflow Agent** | `WorkflowAgentScope` + `WorkflowAgentToolkit`：60+ 函数调用工具，让 Agent 直接增删节点/连接/属性、执行节点、编译路由、布局 |
| **Compiler 支持** | `CompileWorkflow` / `GetCompileStatus` / `RunCompiledWorkflow`（链级运行）/ `GetExecutionLog` |
| **MCP** | `McpScope`（stdio 本地 + 远程 HTTP/Streamable HTTP）、`McpAgentToolkit`（Agent 自管理服务器）、全局可绑定状态 VM（`McpStatusViewModel`） |
| **双语技能/参考** | `Resources/Workflow/{en,zh}/Skills|References|Safety` 随包嵌入，自动并入 Agent 系统提示词 |

## 快速开始：Workflow Agent

```csharp
var scope = tree.AsAgentScope()                       // tree: IWorkflowTreeViewModel
    .WithPromptLanguage(AgentLanguages.English)
    .WithOutputLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery(assemblyName: "MyLib")         // 自动发现组件/枚举/接口
    .WithAllowNodeExecution(true)                     // 显式允许执行节点业务代码
    .WithSynchronizationContext(SynchronizationContext.Current); // UI 线程 marshal

var prompt = scope.ProvideProgressiveContextPrompt(); // 渐进式系统提示词
var baseTools = scope.ProvideTools();                 // 基础工具集

// 每次对话把工具集经 ChatOptions 传入（MCP 服务器加载/卸载后自动增减）：
var agent = chatClient.AsAIAgent(instructions: prompt);
var runOptions = new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions { Tools = [.. baseTools, .. mcp.LoadedTools] },
};
var response = await agent.RunAsync(message, session, runOptions);
```

## 双执行入口（不要混淆）

| 入口 | 工具 | 语义 |
|---|---|---|
| **节点级** | `ExecuteNode` | 单节点 `ReceiveCommand`（EXEC/RECV） |
| **链级** | `RunCompiledWorkflow` | 编译图经 `CompilerEngine` + `RuntimeContext` 驱动整条链 |

## MCP 服务器

### stdio 本地（npm / npx / pip / dotnet / exe）

```csharp
var mcp = new McpScope().WithSynchronizationContext(SynchronizationContext.Current);

var configs = new[]
{
    new McpServerConfiguration
    {
        Name = "Filesystem (npx)",
        RunMode = McpServerRunMode.Npx,
        Package = "@modelcontextprotocol/server-filesystem",
        Arguments = ["C:/data"],                              // 允许目录集
        Options = new { env = new { FILESYSTEM_ROOT = "C:/data" } },  // 逐服务器环境变量
    },
};
var tools = await mcp.LoadAsync(configs);
```

### 远程 HTTP + 鉴权

```csharp
new McpServerConfiguration
{
    Name = "Microsoft Learn",
    RunMode = McpServerRunMode.Http,
    Endpoint = "https://learn.microsoft.com/api/mcp",
    Options = new { connectionTimeout = 30 },               // 秒
};
// 带鉴权：
Options = new
{
    headers = new { Authorization = "Bearer <token>" },     // Header 鉴权
    // 或 OAuth2 PKCE（需宿主注册跳转）：
    // oauth = new { clientId = "...", clientSecret = "...", redirectUri = "...", scopes = new[] { "read" } },
};
// OAuth 时宿主必须注册授权跳转：
mcp.WithOAuthAuthorizationRedirect(async (authUri, redirectUri, ct) =>
{
    await OpenBrowserAsync(authUri);
    return await WaitForCallbackAsync(redirectUri, ct);      // 返回带 code 的回调 URL 字符串
});
```

> `McpServerConfiguration.Options` 是**匿名对象序列化结果**：`headers`（HTTP 头）、`oauth`（OAuth2）、`connectionTimeout`（秒/TimeSpan 字符串）、`transportMode`（AutoDetect/StreamableHttp/Sse）、`ownsSession`、`env`（stdio 环境变量）、`workingDirectory`。**未知 key 会被拒绝**（报错而非静默忽略）。安全模型：配置在加载时确定一次、之后不可变更；Agent 只能加载/卸载/查看，不能中途改配置。

### Agent 自管理服务器（McpAgentToolkit）

经 `WorkflowAgentScope.WithTools(...)` 注册后，Agent 可：
`ListMcpServers`（状态）、`DescribeMcpServer`（导出工具能力提示词）、`LoadMcpServers`（加载/安装）、`UnloadMcpServer`（中途移除）。服务器工具在每次对话时按 `McpScope.LoadedTools` 动态并入。

### 全局状态 VM（`McpStatusViewModel`）

绑定 `McpScope.Status`：每服务器 `Name` / `StateText`（未启动/安装中/连接中/已连接/错误）/ `ToolCount` / `Error`，聚合 `ConnectedCount` / `ErrorCount` / `WorkingCount`。状态更新经 `WithSynchronizationContext` marshal 到 UI 线程。

## 链接

- 仓库: https://github.com/Axvser/VeloxDev
- 依赖: [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) · Microsoft.Extensions.AI · ModelContextProtocol · Microsoft.Agents.AI
