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
| `Options` | an `IReadOnlyDictionary<string, object?>` of option name → value (a primitive, a nested map, or an array) — see the option table |

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

⚙ **An unknown key throws.** The options map is validated against these names rather than silently ignored, so a typo is a startup failure and not a server that mysteriously lacks its header.

⚙ **It is a dictionary, not an anonymous object.** The serialized world is closed and an anonymous type is in neither the archive format nor the generator's type set, so an anonymous object no longer compiles here — a nested section is another `Dictionary<string, object?>`.

```csharp
// stdio, with an environment variable
Options = new Dictionary<string, object?>
{
    ["env"] = new Dictionary<string, object?> { ["FILESYSTEM_ROOT"] = "C:/data" },
    ["workingDirectory"] = "C:/data",
}

// HTTP with bearer auth
Options = new Dictionary<string, object?>
{
    ["connectionTimeout"] = 30,
    ["headers"] = new Dictionary<string, object?> { ["Authorization"] = "Bearer <token>" },
}

// HTTP with OAuth 2.0
Options = new Dictionary<string, object?>
{
    ["oauth"] = new Dictionary<string, object?>
    {
        ["clientId"] = "…", ["clientSecret"] = "…",
        ["redirectUri"] = "http://localhost:1179/cb",
        ["scopes"] = new[] { "mcp.read" },
    },
}
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

One line. Attach the scope and let its context provider carry both halves:

```csharp
scope.WithMcps(mcp);
```

That contributes the management tools, the prompt text describing them, the server inventory, and the tools of every connected server — each turn, from the scope's current state.

Management tools: `ListMcpServers`, `LoadMcpServers`, `UnloadMcpServer`, `DescribeMcpServer`; `AddMcpServer` from `RemoteConfirmed` up; `SetMcpServerArguments` from `AllConfirmed` up (a remote server takes no launch arguments, and a local one is not reconfigurable below that rung — so it is not registered a rung earlier, where every call would be refused).

⚙ **Do not also register them with `WithTools`.** The provider already contributes them, and the framework unions tool lists without deduplicating by name — registering them twice puts every one of them in the prompt twice.

⚙ **Registering configurations on the scope is what makes them reloadable.** `mcp.WithServers(...)` pre-registers the ones the Agent may load by name; `LoadAsync` and `AddAsync` also record whatever they are given, so a host that only loads directly still ends up with a reloadable set.

⚙ **`WithMcps` is what makes a connected server's tools first-class.** They join the per-turn tool set through the context provider, wrapped like every other tool, so they are counted against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, marshalled onto your synchronization context, and raise `ToolCalled`. It also wires MCP self-service to the scope's own confirmation handler, so approval is configured once.

⚙ **Merging `mcp.LoadedTools` into `ChatOptions.Tools` by hand is now the wrong path twice over** — it bypasses the wrapper, and it duplicates every tool, because the provider already offers them and the framework unions tool lists without deduplicating by name.

⚙ **A server loaded mid-conversation becomes available on the next turn** without rebuilding the agent: the provider re-renders whenever `McpScope.Version` advances.

⚙ **There is no ladder any more: the session's permission mode decides, and the tools are always registered.** `AddMcpServer` and `SetMcpServerArguments` reach the model on every scope; whether a call runs is the mode's `Curate` row (asking under `Auto`, `AutoEdit` and `Manual`; allowed under `Bypass`) plus the host's rules, and a refusal names which of the two refused it. A **standalone** `McpScope` has no wrapper around its tools, so those two ask for themselves — and with no confirmation handler an unanswerable prompt denies.

⚙ **The mode is a runtime setting, and the prompt follows it.** `WithPermissionMode` advances `Version`, and `BuildInteractionSafetyPrompt` reads the mode when it renders — so a mode moved mid-session shows up on the next turn, together with the truth table and the host's rules it prints. A static `[Description]` cannot track that, which is why the descriptions that would depend on the mode defer to the prompt for what is allowed.

⚙ **A configuration is not fixed when it is loaded.** `WithServers` re-registers by name at any time, and a server that is already connected follows the new configuration on its next call — connecting first and releasing the old connection only once the new one answers, so a configuration that cannot be reached leaves the working connection serving. `SetMcpServerArguments` does the same for one local server's launch arguments.

⚙ **A server's tools keep their identity across that.** The model holds `McpToolProxy` instances owned by the scope, not the connection's own tools: a rebuild replaces the client underneath, the tool names and objects do not move, and the render is invalidated only when the set of names changes. So a parameter change is invisible to the prompt unless it changes what the server offers.

⚙ **`ListMcpServers` reports how each server was launched** — run mode, package, version, launch arguments, endpoint — so the model can answer questions about them from the real configuration rather than guessing. The host's connection `Options` are deliberately **not** projected: they carry authorization headers, an OAuth client secret and environment variables.

⚙ **Unloading now tears the connection down.** `UnloadServerAsync` disposes the client, which for stdio modes terminates the child process. The synchronous `UnloadServer` does the same, blocking; prefer the async overload. `McpScope` implements `IAsyncDisposable`, and the clients are retained for as long as their tools are offered — an `McpClientTool` holds a reference to its client, so dropping one would break the other.

## Prompting

`ProvideProgressiveContextPrompt(language)` builds the system prompt. The shipped resources are `Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and four `Safety/` documents.

⚙ **Language selection maps `Chinese → "zh"` and everything else to `"en"`, with a per-file fallback to English.** A partially translated resource set degrades document by document rather than all at once.

⚙ `GetComponentContext(fullTypeName, language)` accepts only `"English"` and `"Chinese"`, even though `AgentLanguages` declares thirty-four names over thirty-three distinct values (`Chinese` is an alias of `ChineseSimplified`) and the resource set has two, so that is the real limit on the tool.

⚙ The safety documents are prose injected into the prompt. See the SKILL's warning: they change what the model is *told*, not what the code lets it do.
