# MCP servers

`McpScope` installs, launches, connects and monitors external Model Context Protocol servers, and hands their tools back for you to merge into the agent's tool list.

```csharp
var mcp = new McpScope()
    .WithMcpRoot(".evn/mcp")                                     // default: AppContext.BaseDirectory + ".evn/mcp"
    .WithConnectionTimeout(TimeSpan.FromSeconds(30))
    .WithSynchronizationContext(SynchronizationContext.Current)
    .WithOAuthAuthorizationRedirect(async (authUri, redirectUri, ct) =>
    {
        await OpenBrowserAsync(authUri);
        return await WaitForCallbackAsync(redirectUri, ct);
    });

var tools = await mcp.LoadAsync(configs, cancellationToken);
```

## `McpServerConfiguration`

| Field | Meaning |
|---|---|
| `Name` | identity; also the key for `UnloadServer` and `GetServerTools` |
| `Description` | shown in the agent's MCP management tools |
| `RunMode` | how to launch — see below |
| `Package` | the package, module, dll path or executable, depending on `RunMode` |
| `Version` | honoured by `Npm` and `Pip` only; ignored by `Npx`, `Uvx`, `Dotnet`, `Exe` |
| `Arguments` | passed through to the server |
| `Endpoint` | the URL, for `Http` |
| `Options` | an anonymous object serialized to JSON — see the option table |

| `McpServerRunMode` | Launches | `Package` is |
|---|---|---|
| `Npm` | installs, then runs the package's main/bin from `node/` | an npm name |
| `Npx` | `npx -y {pkg} {args}` | an npm name |
| `Uvx` | `uvx {pkg} {args}` | a PyPI name |
| `Pip` | builds a venv, installs, runs `{venv}/python -m {module}` | a PyPI name (dashes become underscores) |
| `Dotnet` | `dotnet {dll} {args}` from `dotnet/` | a dll path under the root |
| `Exe` | executes `exe/{Package}` | any executable |
| `Http` | Streamable HTTP with an SSE fallback | unused — uses `Endpoint` |

## Options

| Key | Modes | Meaning |
|---|---|---|
| `env` | stdio | environment variables |
| `workingDirectory` | stdio | process working directory |
| `headers` | HTTP | additional request headers, e.g. `Authorization` |
| `oauth` | HTTP | `clientId`, `clientSecret`, `redirectUri`, `scopes` |
| `connectionTimeout` | HTTP | seconds (number) or a TimeSpan string |
| `transportMode` | HTTP | `AutoDetect` · `StreamableHttp` · `Sse` |
| `ownsSession` | HTTP | whether the scope owns the session lifetime |

⚙ **An unknown key throws.** The options object is validated against these names rather than silently ignored, so a typo is a startup failure and not a server that mysteriously lacks its header.

```csharp
// stdio, with an environment variable
Options = new { env = new { FILESYSTEM_ROOT = "C:/data" }, workingDirectory = "C:/data" }

// HTTP with bearer auth
Options = new { connectionTimeout = 30, headers = new { Authorization = "Bearer <token>" } }

// HTTP with OAuth 2.0
Options = new { oauth = new { clientId = "…", clientSecret = "…",
                              redirectUri = "http://localhost:1179/cb",
                              scopes = new[] { "mcp.read" } } }
```

## Lifecycle and status

```
NotStarted → Installing (local modes only) → Connecting → Connected | Error
```

`mcp.Status` is a bindable `McpStatusViewModel`: `Servers`, `ConnectedCount`, `ErrorCount`, `WorkingCount`, `IsAllReady`, `HasError`, `IsLoading`. It is updated live and marshalled to the synchronization context, so it can be bound straight to a UI panel.

| Method | Effect |
|---|---|
| `LoadAsync(configs, ct)` | clears the previously loaded tool sets, then loads each server |
| `GetServerTools(name)` | one server's tools |
| `UnloadServer(name)` | removes that server's tools and resets it to `NotStarted` |
| `LoadedTools` | the aggregate tool set |

⚙ **A failing server does not fail the batch.** `LoadOneAsync` catches everything except `OperationCanceledException`, records the message on that server's state, raises `ServerError(config, ex)`, and returns an empty array. Cancellation propagates; nothing else does.

⚙ **HTTP timeouts are enforced host-side.** The scope wraps the connect in a linked `CancellationTokenSource` and rethrows it as a `TimeoutException`, because the SDK's own internal timeouts are unreliable.

⚙ **Installation is memoized process-wide** by a static set guarded by a static `SemaphoreSlim`. Loading two servers that share an npm package installs it once. Loading a configuration whose name already exists reuses the existing status entry rather than adding a duplicate.

## Merging with the agent

Two paths, and they are not equivalent:

```csharp
// 1. the four MCP management tools — wrapped, tracked, budgeted
scope.WithTools("<MCP management prompt text>",
    [.. new McpAgentToolkit(mcp, mcpServers).CreateTools()]);

// 2. the actual server tools — raw, per conversation
new ChatClientAgentRunOptions
{
    ChatOptions = new ChatOptions { Tools = [.. _baseTools, .. mcp.LoadedTools] }
}
```

Management tools: `ListMcpServers`, `LoadMcpServers`, `UnloadMcpServer`, `DescribeMcpServer`.

⚙ **Tools merged through `ChatOptions.Tools` bypass the tracking wrapper entirely.** They are not counted against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, not marshalled onto your synchronization context, and do not raise `ToolCalled` or mark the tree dirty. If any of that matters, wrap them yourself or register them through `scope.WithTools`.

⚙ **Assembling the list per conversation is the point**, not a workaround: a server loaded mid-conversation becomes available on the next turn without rebuilding the agent. That is why the demo exposes `BuildRunOptions()` rather than baking the tools in.

⚙ **The host registers; the agent operates.** Configurations are fixed when you call `LoadAsync`, and the model can only load, unload and inspect. It cannot author a configuration or change one — keep that property if you write your own MCP layer.

## Prompting

`ProvideProgressiveContextPrompt(language)` builds the system prompt. The shipped resources are `Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and four `Safety/` documents.

⚙ **Language selection maps `Chinese → "zh"` and everything else to `"en"`, with a per-file fallback to English.** A partially translated resource set degrades document by document rather than all at once.

⚙ `GetComponentContext(fullTypeName, language)` accepts only `"English"` and `"Chinese"`, even though `AgentLanguages` has thirty-two values and the resource set has two, so that is the real limit on the tool.

⚙ The safety documents are prose injected into the prompt. See the SKILL's warning: they change what the model is *told*, not what the code lets it do.
