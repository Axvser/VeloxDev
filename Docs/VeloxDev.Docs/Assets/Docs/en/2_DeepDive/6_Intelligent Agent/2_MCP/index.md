# MCP Server Integration

VeloxDev supports MCP (Model Context Protocol) servers via `McpScope`, enabling the agent to call external tools through 6 runtime modes covering Node.js, Python, .NET, and any executable.

---

## How It Works

1. `McpScope` installs/prepares servers based on `McpServerRunMode`
2. Connects via **stdio** using the MCP protocol
3. Merges server tools into the agent's tool set

All installations are isolated under `.evn/mcp/{runtime}/{package}/`.

## Six Run Modes

| Mode | Launch Method | Install Dir | Package Source |
|------|--------------|-------------|----------------|
| `Npm` | `npm install` → `node {main}` | `.evn/mcp/node/{package}/` | **Any npm package** |
| `Npx` | `npx -y {package}` | Temporary | Any npm package |
| `Uvx` | `uvx {package}` | Temporary | Any PyPI package |
| `Pip` | `python -m venv` → `pip install` → `python -m {module}` | `.evn/mcp/py/venvs/{package}/` | Any PyPI package |
| `Dotnet` | `dotnet {dll}` | `.evn/mcp/dotnet/{package}/` | User-published |
| `Exe` | Direct execution | `.evn/mcp/exe/{package}/` | Any executable |

> **Key takeaway**: Any npm-based MCP server works with `Npm`/`Npx`. Any PyPI-based MCP server works with `Pip`/`Uvx`. `Dotnet` and `Exe` give you full control.

## Configuration Examples

### npm MCP server

```csharp
using VeloxDev.AI.MCP;

var config = new McpServerConfiguration
{
	Name = "Fetch",
	RunMode = McpServerRunMode.Npm,
	Package = "@anthropic/mcp-fetch",
	Version = "latest",
};
```

### npx (temporary)

```csharp
var config = new McpServerConfiguration
{
	Name = "Playwright",
	RunMode = McpServerRunMode.Npx,
	Package = "@playwright/mcp",
};
```

### Python (pip + venv)

```csharp
var config = new McpServerConfiguration
{
	Name = "SQLite Explorer",
	RunMode = McpServerRunMode.Pip,
	Package = "sqlite-mcp",
};
```

### .NET application

```csharp
var config = new McpServerConfiguration
{
	Name = "Email Service",
	RunMode = McpServerRunMode.Dotnet,
	Package = "sharp-email-mcp/SharpEmailMcp.dll",
};
```

### Any executable

```csharp
var config = new McpServerConfiguration
{
	Name = "MyTool",
	RunMode = McpServerRunMode.Exe,
	Package = "tools/my-tool.exe",
	Arguments = ["--verbose"],
};
```

## Loading into Agent

```csharp
using VeloxDev.AI.MCP;

var scope = new McpScope()
	.WithMcpRoot(".evn/mcp");

var mcpTools = await scope.LoadAsync([config1, config2]);
var allTools = workflowTools.Concat(mcpTools).ToArray();
```

## API Reference

### McpServerConfiguration

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Server name |
| `Description` | `string` | Description |
| `RunMode` | `McpServerRunMode` | Npm/Npx/Uvx/Pip/Dotnet/Exe |
| `Package` | `string` | Package name or path |
| `Version` | `string?` | Version tag (Npm/Pip only) |
| `Arguments` | `string[]` | Extra arguments |

### McpScope

| Member | Description |
|--------|-------------|
| `WithMcpRoot(path)` | Set install root (default `.evn/mcp`) |
| `LoadAsync(servers, ct)` | Install & connect, returns `AITool[]` |
| `ServerError` | Event: fires per-server on failure |
