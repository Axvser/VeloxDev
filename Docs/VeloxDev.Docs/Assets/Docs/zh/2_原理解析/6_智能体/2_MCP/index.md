# MCP 配置

支持 MCP（Model Context Protocol）服务器，扩展智能体的上下文感知能力。

```csharp
using VeloxDev.AI.MCP;

var mcpServer = new McpServerConfiguration
{
    Name = "WorkflowTools",
    Transport = "stdio",
    RunMode = McpServerRunMode.SingleSession
};

scope.WithMcpServer(mcpServer);
```

MCP 允许智能体访问外部数据源（数据库、文件系统、API）以增强决策能力。
