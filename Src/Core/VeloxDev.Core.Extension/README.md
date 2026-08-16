# VeloxDev.Core.Extension

> **MAF (Microsoft.Extensions.AI) Workflow Agent + MCP support** — the optional companion to [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) for building **AI-controllable visual workflow editors**. Works with WPF / Avalonia / WinUI / MAUI / WinForms / Blazor.

## What's inside

| Module | Description |
|---|---|
| **Workflow Agent** | `WorkflowAgentScope` + `WorkflowAgentToolkit`: 60+ function-calling tools that let an Agent add/remove nodes & links, patch properties, execute nodes, compile routing, and lay out the canvas |
| **Compiler support** | `CompileWorkflow` / `GetCompileStatus` / `RunCompiledWorkflow` (chain-level execution) / `GetExecutionLog` |
| **MCP** | `McpScope` (stdio local + remote Streamable HTTP), `McpAgentToolkit` (Agent-managed servers), global bindable status VM (`McpStatusViewModel`) |
| **Bilingual skills/references** | `Resources/Workflow/{en,zh}/Skills|References|Safety`, embedded and merged into the Agent system prompt |

## Quick start: Workflow Agent

```csharp
var scope = tree.AsAgentScope()                       // tree: IWorkflowTreeViewModel
    .WithPromptLanguage(AgentLanguages.English)
    .WithOutputLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery(assemblyName: "MyLib")         // auto-discover components/enums/interfaces
    .WithAllowNodeExecution(true)                     // explicitly allow node business code
    .WithSynchronizationContext(SynchronizationContext.Current); // marshal tools to the UI thread

var prompt = scope.ProvideProgressiveContextPrompt(); // progressive system prompt
var baseTools = scope.ProvideTools();                 // base tool set

// Pass the tool set per call via ChatOptions — loaded MCP servers' tools join automatically:
var agent = chatClient.AsAIAgent(instructions: prompt);
var runOptions = new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions { Tools = [.. baseTools, .. mcp.LoadedTools] },
};
var response = await agent.RunAsync(message, session, runOptions);
```

## Two execution entries (do not confuse them)

| Entry | Tool | Semantics |
|---|---|---|
| **Node-level** | `ExecuteNode` | A single node's `ReceiveCommand` (EXEC/RECV) |
| **Chain-level** | `RunCompiledWorkflow` | Drive the whole compiled chain via `CompilerEngine` + `RuntimeContext` |

## MCP servers

### stdio local (npm / npx / pip / dotnet / exe)

```csharp
var mcp = new McpScope().WithSynchronizationContext(SynchronizationContext.Current);

var configs = new[]
{
    new McpServerConfiguration
    {
        Name = "Filesystem (npx)",
        RunMode = McpServerRunMode.Npx,
        Package = "@modelcontextprotocol/server-filesystem",
        Arguments = ["C:/data"],                                   // allowed directories
        Options = new { env = new { FILESYSTEM_ROOT = "C:/data" } },  // per-server env vars
    },
};
var tools = await mcp.LoadAsync(configs);
```

### Remote HTTP + auth

```csharp
new McpServerConfiguration
{
    Name = "Microsoft Learn",
    RunMode = McpServerRunMode.Http,
    Endpoint = "https://learn.microsoft.com/api/mcp",
    Options = new { connectionTimeout = 30 },                      // seconds
};
// With auth:
Options = new
{
    headers = new { Authorization = "Bearer <token>" },            // header-based auth
    // or OAuth 2.0 (PKCE):
    // oauth = new { clientId = "...", clientSecret = "...", redirectUri = "...", scopes = new[] { "read" } },
};
// For OAuth the host must register the authorization redirect:
mcp.WithOAuthAuthorizationRedirect(async (authUri, redirectUri, ct) =>
{
    await OpenBrowserAsync(authUri);
    return await WaitForCallbackAsync(redirectUri, ct);            // return the callback URL string with the code
});
```

> `McpServerConfiguration.Options` is the serialization of an anonymous object. Known keys: `headers` (HTTP headers), `oauth` (OAuth2), `connectionTimeout` (seconds or TimeSpan string), `transportMode` (`AutoDetect`/`StreamableHttp`/`Sse`), `ownsSession`, `env` (stdio environment variables), `workingDirectory`. **Unknown keys are rejected** (throws, never silently ignored). Security model: server configuration is fixed at load time and immutable — the Agent can only load / unload / inspect servers, never reconfigure them.

### Agent-managed servers (`McpAgentToolkit`)

Registered via `WorkflowAgentScope.WithTools(...)`, the Agent can call `ListMcpServers` (status), `DescribeMcpServer` (export tool-capability prompts), `LoadMcpServers` (install & connect), `UnloadMcpServer` (remove mid-session). Server tools join the tool set on every call via `McpScope.LoadedTools`.

### Global status VM (`McpStatusViewModel`)

Bind `McpScope.Status`: per-server `Name` / `StateText` (NotStarted / Installing / Connecting / Connected / Error) / `ToolCount` / `Error`, plus aggregates `ConnectedCount` / `ErrorCount` / `WorkingCount`. Updates marshal to the UI thread via `WithSynchronizationContext`.

## Links

- Repository: https://github.com/Axvser/VeloxDev
- Dependencies: [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) · Microsoft.Extensions.AI · ModelContextProtocol · Microsoft.Agents.AI
