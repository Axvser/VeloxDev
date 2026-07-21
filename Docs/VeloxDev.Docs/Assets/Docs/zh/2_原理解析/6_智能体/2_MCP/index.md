# MCP 服务器集成

支持通过 MCP（Model Context Protocol）接入外部服务器，扩展智能体的工具集。支持 6 种运行模式，涵盖 Node.js、Python、.NET 及任意可执行文件。

---

## 原理

`McpScope` 管理 MCP 服务器的安装和生命周期：

1. 根据 `McpServerRunMode` 自动安装/准备服务器
2. 通过 **stdio** 建立 MCP 协议连接
3. 将服务器的工具列表合并到智能体的工具集中

所有安装物隔离在 `.evn/mcp/{runtime}/{package}/` 目录下。

## 支持六种运行模式

| 模式 | 启动方式 | 安装目录 | 包来源 |
|------|----------|----------|--------|
| `Npm` | `npm install` → `node {main}` | `.evn/mcp/node/{package}/` | **任意 npm 包** |
| `Npx` | `npx -y {package}` | 临时（不持久安装） | 任意 npm 包 |
| `Uvx` | `uvx {package}` | 临时（uv 隔离环境） | 任意 PyPI 包 |
| `Pip` | `python -m venv` → `pip install` → `python -m {module}` | `.evn/mcp/py/venvs/{package}/` | 任意 PyPI 包 |
| `Dotnet` | `dotnet {dll}` | `.evn/mcp/dotnet/{package}/` | 用户自行 publish |
| `Exe` | 直接执行 | `.evn/mcp/exe/{package}/` | 任意可执行文件 |

> **关键结论**：`Npm`/`Npx` 模式可以安装**任何发布在 npm 上的 MCP 服务器**，`Pip`/`Uvx` 可以安装**任何 PyPI 上的 MCP 服务器**，`Dotnet`/`Exe` 模式完全由用户控制。并非仅限于特定厂商。

## 配置示例

### 安装 npm MCP 服务器

```csharp
using VeloxDev.AI.MCP;

var config = new McpServerConfiguration
{
    Name = "Fetch",
    Description = "Fetch web pages via MCP",
    RunMode = McpServerRunMode.Npm,
    Package = "@anthropic/mcp-fetch",
    Version = "latest",
};
```

### 使用 npx 临时运行

```csharp
var config = new McpServerConfiguration
{
    Name = "Playwright",
    RunMode = McpServerRunMode.Npx,
    Package = "@playwright/mcp",
};
```

### Python 包（pip + venv）

```csharp
var config = new McpServerConfiguration
{
    Name = "SQLite Explorer",
    RunMode = McpServerRunMode.Pip,
    Package = "sqlite-mcp",
    Version = ">=0.1.0",
};
```

### .NET 应用

```csharp
var config = new McpServerConfiguration
{
    Name = "Email Service",
    RunMode = McpServerRunMode.Dotnet,
    Package = "sharp-email-mcp/SharpEmailMcp.dll",
};
```

### 任意可执行文件

```csharp
var config = new McpServerConfiguration
{
    Name = "MyTool",
    RunMode = McpServerRunMode.Exe,
    Package = "tools/my-tool.exe",
    Arguments = ["--verbose"],
};
```

## 加载到智能体

```csharp
using VeloxDev.AI.MCP;

var scope = new McpScope()
    .WithMcpRoot(".evn/mcp");

var mcpTools = await scope.LoadAsync(
    [config1, config2, config3]);

var allTools = workflowTools.Concat(mcpTools).ToArray();
```

## API 参考

### McpServerConfiguration

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 服务器名称 |
| `Description` | `string` | 描述 |
| `RunMode` | `McpServerRunMode` | 运行模式 |
| `Package` | `string` | 包名或路径 |
| `Version` | `string?` | 版本标签（仅 Npm/Pip） |
| `Arguments` | `string[]` | 额外参数 |

### McpScope

| 成员 | 说明 |
|------|------|
| `WithMcpRoot(path)` | 设置安装根目录（默认 `.evn/mcp`） |
| `LoadAsync(servers, ct)` | 安装并连接，返回 `AITool[]` |
| `ServerError` | 事件：单服务器加载失败时触发 |

完整示例见 [Examples/Workflow/Common/Lib/](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/)
