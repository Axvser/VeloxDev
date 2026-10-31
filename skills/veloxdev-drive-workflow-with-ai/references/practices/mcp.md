# MCP servers

`McpScope` installs, launches, connects and monitors external Model Context Protocol servers, and contributes their tools to the agent through one context provider, wrapped like every other tool.

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
| `Options` | an `IReadOnlyDictionary<string, object?>` of option name → value (a primitive, a nested map, or an array) |

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

⚙ **An unknown key throws.** The options map is validated against these names, so a typo is a startup failure and not a server that mysteriously lacks its header.

⚙ **It is a dictionary, not an anonymous object.** The serialized world is closed and an anonymous type is in neither the archive format nor the generator's type set. A nested section is another `Dictionary<string, object?>`.

## Lifecycle and status

```text
NotStarted → Installing (local modes only) → Connecting → Connected | Error
```

`mcp.Status` is a bindable `McpStatusViewModel`: `Servers`, `ConnectedCount`, `ErrorCount`, `WorkingCount`, `IsAllReady`, `HasError`, `IsLoading`. It is updated live and marshalled to the synchronization context.

| Member | Effect |
|---|---|
| `LoadAsync(configs, ct)` | clears the previously loaded tool sets, then loads each server |
| `GetServerTools(name)` | one server's tools |
| `UnloadServer(name)` / `UnloadServerAsync(name)` | removes that server's tools and resets it to `NotStarted`; the async overload is the one to prefer |
| `LoadedTools` | the aggregate tool set |
| `DisposeAsync()` | disposes every connected server's client |

⚙ **`McpScope` exposes `DisposeAsync()`.** `await using var mcp = new McpScope()` works through the pattern; the class declaration does not add `IAsyncDisposable` to the public type list, so assign it through `await using` rather than through that interface.

⚙ **A failing server does not fail the batch.** `LoadOneAsync` catches everything except `OperationCanceledException`, records the message on that server's state, raises `ServerError(config, ex)`, and returns an empty array. Cancellation propagates.

⚙ **HTTP timeouts are enforced host-side.** The scope wraps the connect in a linked `CancellationTokenSource` and rethrows it as a `TimeoutException`, because the SDK's own internal timeouts are unreliable.

⚙ **Installation is memoized process-wide** by a static set guarded by a static `SemaphoreSlim`. Loading two servers that share an npm package installs it once. Loading a configuration whose name already exists reuses the existing status entry.

## Merging with the agent

One line. Attach the scope and let its context provider carry both halves:

```csharp
scope.WithMcps(mcp);
```

That contributes the management tools, the prompt text describing them, the server inventory, and the tools of every connected server — each turn, from the scope's current state.

Management tools — ten, all registered on a normal scope: `ListMcpServers`, `LoadMcpServers`, `UnloadMcpServer`, `DescribeMcpServer`, `AddMcpServer`, `SetMcpServerArguments`, plus the four resource/prompt reads `ListMcpResources`, `ReadMcpResource`, `ListMcpPrompts`, `GetMcpPrompt`. Six are `Read` (`McpAgentToolkit.ReadToolNames`); the other four are `Curate`. A granted view (the MCP surface handed to a spawned child) registers only the reading tools.

⚙ **Resources and prompts are part of the surface, not an afterthought.** A server that exposes only resources has no tools to export; without `ListMcpResources` / `ReadMcpResource` it would be invisible to the model. `ReadMcpResource` returns text (binary content is reported by size, not pasted) and caps each body by `maxChars`; `GetMcpPrompt` expands a template with a JSON argument object and caps the total message text. Both call the protocol through `HostedMcpServer`, under the same per-server rebuild gate as a tool call.

⚙ **A provider contributes the management tools.** Registering them again with `WithTools` puts every one of them in the prompt twice, because the framework unions tool lists without deduplicating by name.

⚙ **Registering configurations on the scope is what makes them reloadable.** `mcp.WithServers(...)` pre-registers the ones the Agent may load by name; `LoadAsync` and `AddAsync` also record whatever they are given, so a host that only loads directly still ends up with a reloadable set.

⚙ **`WithMcps` makes a connected server's tools first-class.** They join the per-turn tool set through the context provider, wrapped like every other tool, so they are counted against `MaxToolCalls` / `MaxReadToolCalls` / `MaxWriteToolCalls`, marshalled onto your synchronization context, and raise `ToolCalled`. It also wires the MCP management tools to the scope's own confirmation handler and hands them the scope's permission policy.

⚙ **Merging `mcp.LoadedTools` into `ChatOptions.Tools` by hand bypasses the wrapper and duplicates every tool**, because the provider already offers them.

⚙ **A server loaded mid-conversation becomes available on the next turn** without rebuilding the agent: the provider re-renders whenever `McpScope.Version` advances.

## Two classifications, one per origin

⚙ A connected server's own tools are third-party code and are wrapped as `AgentActionCategory.Execute`, with a source of `mcp:<server>`. A host rule such as `WithPermissionRule(PermissionDecision.Ask, "source:mcp:*")` gates them.

⚙ The management tools are `Curate` — except the six in `McpAgentToolkit.ReadToolNames` (`ListMcpServers`, `DescribeMcpServer`, and the resource/prompt reads), which are `Read`, with a source of `mcp:management`.

⚙ **The session's permission mode decides, and on a normal scope all ten management tools are always registered.** `LoadMcpServers`, `UnloadMcpServer`, `AddMcpServer` and `SetMcpServerArguments` are `Curate`: `Auto` asks for them, `Manual` and `AutoEdit` ask too, `Plan` refuses them, and `Bypass` runs them unless a `Deny` rule matches. A refusal names the mode or the rule. A **standalone** `McpScope` has no wrapper around its tools. Only `AddMcpServer` and `SetMcpServerArguments` ask for themselves, through `WithConfirmationHandler`; with no handler an unanswerable prompt denies. `LoadMcpServers` and `UnloadMcpServer` have no gate of their own, so a standalone host gates those two itself.

## Adding and reconfiguring at runtime

⚙ **`AddMcpServer` asks about the kind, not only the name.** A local run mode installs and launches software; an Http endpoint only sends data out. The tool puts a synthesised `mcp-add:local` / `mcp-add:http` call to the same policy, which is what lets the host write `WithPermissionRule(Deny, "mcp-add:local")` and leave Http servers alone.

⚙ **The mode is a runtime setting, and the prompt follows it.** `WithPermissionMode` advances `Version`, and `BuildInteractionSafetyPrompt` reads the mode when it renders. The descriptions that depend on the mode defer to the prompt for what is allowed.

⚙ **A configuration is not fixed when it is loaded.** `WithServers` re-registers by name at any time, and a server that is already connected follows the new configuration on its next call — connecting first and releasing the old connection only once the new one answers, so a configuration that cannot be reached leaves the working connection serving. `SetMcpServerArguments` does the same for one local server's launch arguments.

⚙ **A server's tools keep their identity across that.** The model holds `McpToolProxy` instances owned by the scope, not the connection's own tools: a rebuild replaces the client underneath, the tool names and objects do not move, and the render is invalidated only when the set of names changes.

⚙ **`ListMcpServers` reports how each server was launched** — run mode, package, version, launch arguments, endpoint. The host's connection `Options` are deliberately **not** projected: they carry authorization headers, an OAuth client secret and environment variables.

⚙ **Unloading tears the connection down.** `UnloadServerAsync` disposes the client, which for stdio modes terminates the child process. The synchronous `UnloadServer` does the same, blocking; prefer the async overload. The clients are retained for as long as their tools are offered — an `McpClientTool` holds a reference to its client.

## Prompting

`ProvideProgressiveContextPrompt(language)` builds the system prompt. The shipped resources are `Resources/Workflow/{en,zh}/` — seven `Skills/`, five `References/` and six `Safety/` documents.

⚙ **Language selection maps `Chinese → "zh"` and everything else to `"en"`, with a per-file fallback to English.** A partially translated resource set degrades document by document.

⚙ `GetComponentContext(fullTypeName, language)` maps a name containing `Chinese` to Chinese and every other value to English, even though `AgentLanguages` declares thirty-four names over thirty-three distinct values (`Chinese` is an alias of `ChineseSimplified`) and the resource set has two.

⚙ The safety documents are prose injected into the prompt. They change what the model is *told*, not what the code lets it do.

When a server fails, duplicates tools, or installs software unexpectedly, read [references/pitfalls/mcp.md](../pitfalls/mcp.md).
