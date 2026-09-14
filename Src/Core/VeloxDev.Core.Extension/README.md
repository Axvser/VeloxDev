# VeloxDev.Core.Extension

> **MAF (Microsoft.Extensions.AI) Workflow Agent + MCP support** — the optional companion to [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) for building **AI-controllable visual workflow editors**. Works with WPF / Avalonia / WinUI / MAUI / WinForms / Blazor.

## What's inside

| Module | Description |
|---|---|
| **Workflow Agent** | `WorkflowAgentScope` + `WorkflowAgentToolkit`: 60+ function-calling tools that let an Agent add/remove nodes & links, patch properties, execute nodes, compile routing, and lay out the canvas |
| **Compiler support** | `CompileWorkflow` / `GetCompileStatus` / `RunCompiledWorkflow` (chain-level execution) / `GetExecutionLog` |
| **MCP** | `McpScope` (stdio local + remote Streamable HTTP), `McpAgentToolkit` (Agent-managed servers), global bindable status VM (`McpStatusViewModel`) — **usable on its own** via `McpScope.CreateContextProvider()` |
| **Skills** | `SkillScope` + `ISkillSource` (embedded documents, or Agent-Skills folders on disk), per-skill enable/disable, bindable `SkillsViewModel` — **usable on its own** via `SkillScope.CreateContextProvider()` |
| **Bilingual skills/references** | `Resources/Workflow/{en,zh}/Skills|References|Safety`, embedded and merged into the Agent system prompt |

## Quick start: Workflow Agent

```csharp
var scope = tree.AsAgentScope()                       // tree: IWorkflowTreeViewModel
    .WithPromptLanguage(AgentLanguages.English)
    .WithOutputLanguage(AgentLanguages.Chinese)
    .WithAutoDiscovery(assemblyName: "MyLib")         // auto-discover components/enums/interfaces
    .WithAllowNodeExecution(true)                     // explicitly allow node business code
    .WithSynchronizationContext(SynchronizationContext.Current) // marshal tools to the UI thread
    .WithSkills("skills")                             // switchable skills (embedded + a disk root)
    .WithMcps(mcp);                                   // MCP servers join the tool set per turn

// The static skeleton is the agent's own instructions. Everything that changes — the skill list, the
// tool set, connected MCP servers — is rendered per invocation by the context providers.
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new ChatOptions { Instructions = scope.ProvideProgressiveContextPrompt() },
    AIContextProviders = scope.CreateContextProviders(),
});

// No run options: the tools arrive with the context, so a skill switched off or a server loaded
// mid-session takes effect on the very next turn without rebuilding the agent.
var response = await agent.RunAsync(message, session);
```

⚙ **Do not also pass the tools through `ChatOptions.Tools`.** The framework unions that list with what
the providers contribute and does **not** deduplicate by name, so a tool offered through both channels
reaches the model twice. The provider is the single source of tools.

⚙ **`WithSynchronizationContext` puts the entire tool call on that context — including work after an
await.** Register it while on the UI thread. The prompt, by contrast, is rendered on the agent's own
thread: bind `Mcp.Status.Servers` / `Skills.Status.Skills` from your UI, and read their `Snapshot`
counterparts from anywhere else.

## MCP and Skills on their own

Neither subsystem needs the workflow layer. Each exports its own context provider, which contributes its
prompt text and its tools on every turn:

```csharp
var mcp = new McpScope()
    .WithSynchronizationContext(SynchronizationContext.Current)
    .WithServers(new McpServerConfiguration
    {
        Name = "Microsoft Learn", RunMode = McpServerRunMode.Http,
        Endpoint = "https://learn.microsoft.com/api/mcp",
    });

var agent = chatClient.AsAIAgent([mcp.CreateContextProvider()]);

await mcp.LoadAsync(mcp.RegisteredServers);   // management + server tools arrive with it
```

```csharp
var skills = new SkillScope()
    .WithSynchronizationContext(SynchronizationContext.Current)
    .WithSkillRoot("skills");
skills.Refresh();

var agent = chatClient.AsAIAgent([skills.CreateContextProvider()]);
```

The workflow layer simply composes the two — `scope.CreateContextProviders()` returns
`[workflow, skills, mcp]` — and hands each subsystem's provider **its own** tool policy, so a tool from
MCP or a skill counts against the same budgets, raises the same `ToolCalled`, and marks the tree dirty
exactly like a built-in one. Attached on their own, the subsystems get thread-marshalling only.

⚙ **`AsAIAgent(providers, instructions)` has no tools parameter on purpose.** The framework unions a
provider's tools with `ChatOptions.Tools` without deduplicating by name, so a tool offered through both
channels reaches the model twice. Leaving the parameter out makes that mistake unavailable. (Its
parameters are ordered providers-first because MAF's own `AsAIAgent(this IChatClient, string
instructions = null, …)` would otherwise capture a single-string call.)

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

> `McpServerConfiguration.Options` is the serialization of an anonymous object. Known keys: `headers` (HTTP headers), `oauth` (OAuth2), `connectionTimeout` (seconds or TimeSpan string), `transportMode` (`AutoDetect`/`StreamableHttp`/`Sse`), `ownsSession`, `env` (stdio environment variables), `workingDirectory`. **Unknown keys are rejected** (throws, never silently ignored).

### Agent-managed servers

`McpScope.CreateContextProvider()` contributes the management tools — `ListMcpServers` (status), `DescribeMcpServer` (export tool-capability prompts), `LoadMcpServers` (install & connect), `UnloadMcpServer` (tear down mid-session) — plus the tools of every connected server. Do not also register them with `WithTools`: the framework unions tool lists without deduplicating, so they would reach the model twice.

By default (`McpSelfServiceLevel.Closed`) the model can only load, unload and inspect servers the host pre-registered, and cannot author a configuration. `WithSelfService(level)` opens that in rungs — see `McpSelfServiceLevel`; at each rung the prompt text describing what the model may do is generated to match, and `AddMcpServer` is registered only once the gate is open.

`WithServers(...)` pre-registers the configurations the Agent may load by name; `LoadAsync` and `AddAsync` also record whatever they are given, so a host that only loads directly still ends up with a reloadable set.

### Global status VM (`McpStatusViewModel`)

Bind `McpScope.Status`: per-server `Name` / `StateText` (NotStarted / Installing / Connecting / Connected / Error) / `ToolCount` / `Error`, plus aggregates `ConnectedCount` / `ErrorCount` / `WorkingCount`. Updates marshal to the UI thread via `WithSynchronizationContext`.

## Links

- Repository: https://github.com/Axvser/VeloxDev
- Dependencies: [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) · Microsoft.Extensions.AI · ModelContextProtocol · Microsoft.Agents.AI
