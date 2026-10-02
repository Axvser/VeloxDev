# VeloxDev.Core.Extension

> **MAF (Microsoft.Extensions.AI) Workflow Agent + MCP support** — the optional companion to [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) for building **AI-controllable visual workflow editors**. Works with WPF / Avalonia / WinUI / MAUI / WinForms / Blazor.

## What's inside

| Module | Description |
|---|---|
| **Workflow Agent** | `WorkflowAgentScope` + `WorkflowAgentToolkit`: 60+ function-calling tools that let an Agent add/remove nodes & links, patch properties, execute nodes, compile routing, and lay out the canvas |
| **Compiler support** | `CompileWorkflow` / `GetCompileStatus` / `RunCompiledWorkflow` (chain-level execution) / `GetExecutionLog`; **holding and carrying on** a run (`StartCompiledWorkflow`, `PauseCompiledRun`, `ResumeCompiledRun`, `StopCompiledRun`, `GetCompiledRunStatus`, `ContinueCompiledWorkflow`) together with the checkpointing behind them (`CheckpointEx`, `FileCheckpointStore`) |
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

## Telemetry

Nothing in this library emits telemetry: `AgentEvent` and `AgentTranscript` are the in-process surface, and
neither leaves the process. Traces and metrics come from the Agent Framework — this only makes the wiring one
call. The host still owns the provider, the exporter, flushing and shutdown.

```csharp
var agent = chatClient.AsAIAgent(new ChatClientAgentOptions { /* … */ })
    .WithAgentTelemetry("MyApp.WorkflowAgent");
// or, when already using a builder: .AsBuilder().UseAgentTelemetry("MyApp.WorkflowAgent").Build()

// Register the same source name yourself — the framework creates the activities, it does not export them.
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyApp.WorkflowAgent")
    .AddOtlpExporter()
    .Build();
```

⚙ `EnableSensitiveData` is **off** by default. Turned on, the spans carry the whole conversation — prompts,
responses, tool arguments and their results — so it belongs in development and testing only.

⚙ Instrument the agent **or** the chat client, not both under one source name: the chat context would be
captured by each instrumented layer and every span would appear twice.

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

## Execution entries (do not confuse them)

| Entry | Tool | Semantics |
|---|---|---|
| **Node-level** | `ExecuteNode` | A single node's `ReceiveCommand` (EXEC/RECV) |
| **Chain-level, waits** | `RunCompiledWorkflow` | Drive the whole compiled chain via `CompilerViewModel` + `RuntimeEngine`, and return when it is over |
| **Chain-level, returns a handle** | `StartCompiledWorkflow` | The same run, handed back as a handle while it is still going |

## Holding a run

`RunCompiledWorkflow` only returns once the run is over, so nothing the model could say would reach a run in flight.
`StartCompiledWorkflow` starts the same run and returns a handle at once; the tools below act on it — this is how the
Agent drives the capabilities that need a hand (`IExecutionGate`, the checkpoint store), rather than only the ones
that configure themselves.

```text
StartCompiledWorkflow(startNodeIndex)        → { handle }
PauseCompiledRun(handle)                     → held at the next node boundary
ResumeCompiledRun(handle)                    → let go
StopCompiledRun(handle)                      → ends as Cancelled, its place stays in the store
GetCompiledRunStatus(handle)                 → isRunning / outcome / isPaused / failures / log tail / logFile
ContinueCompiledWorkflow(startNodeIndex)     → a new run from the last checkpoint: the nodes it records as done are not driven again
```

⚙ **`outcome` is the precise ending, `runStatus` is not.** `Status` has to share one word — `"Stopped"` — between a
failure and a cancellation, so a result carries both and the Agent reads `outcome`
(Completed / Cancelled / Failed).

⚙ **Failures travel as records, not only as lines.** Every result and status carries `failures`: phase, level
(Warning / Error), message, attempt and compile order. What a failure *means* is the host's business, so the library
hands over the fields and stops there.

⚙ **Reading the log depends on how the host configured it, and the result says which.** With a file-backed
`ILogWriter` the result carries `logFile` — an absolute path — and the model opens it with whatever file tool its host
gave it; there is nothing special for the library to do, because a path is a path. With the default in-memory log the
run's `logs` are the record, and no file tool is needed at all.

⚙ **The host's session configuration reaches Agent runs.** `WithSessionConfiguration(context => ...)` sets the retry
policy, the observer, the error sink, the compensator, the gate and the checkpoint store on the session a run is about
to use — they are host policy, not the model's to guess. What the tools need is only filled in where the host left it
unset: the pause gate and the checkpoint store are the Agent's own only when nobody else set one.

## The compiled graph as a document

`CompiledGraph` is an ordinary VeloxDev view model — segments holding nodes in `ObservableCollection`s, with no slots
and no links of its own. So it binds to a list (nested `ItemsControl`s over `Entries`, `BranchSegment.Options` and
`ParallelSegment.Branches`) and it serializes:

```csharp
var json = graph.SerializeCompiledGraph();                    // snapshot — the default
var restored = json.DeserializeCompiledGraph();

var full = graph.SerializeCompiledGraph(includeTree: true);   // keeps what a re-mount needs
```

⚙ **A snapshot is not re-mountable.** It drops the two outward edges — a node's `Parent` (which would otherwise drag
the whole tree in, and come back as a second, orphan tree) and a slot's `Targets`/`Sources` (which would otherwise
drag in every node connected to the graph) — so the document is the segment structure plus each node's own state. The
restored nodes have no `Parent`: their geometry no longer collapses for the canvas zoom, and moving them no longer
marks a tree dirty. Reach for `includeTree: true` when the result must go back onto a canvas, and expect that
document to cost about the size of the tree.

⚙ **A restored node gets a fresh `RuntimeId` and carries no compile identity.** Neither member is writable, so a round
trip cannot preserve them — `Order` is `-1` on a restored node, the same silent degradation a node that does not
implement `ICompileTimeAware` already shows.

⚙ **Branch keys are the exception that is repaired.** An enum key would come back as its number — JSON has no notion
of an enum inside an `object` member, measured, and `TypeNameHandling.All` does not help — which would leave a
*dynamic* branch matching no option at all. The compiler therefore records the key's type beside it.

⚙ **A flat outline is one call away.** `CompiledOutline.Of(graph)` returns one read-only row per segment — its depth,
its kind (`Execute` / `Branch` / `Parallel`), a label and the nodes it names — for a list that shows the whole
structure at once rather than a nested `ItemsControl`. The graph is frozen once compiled, so the outline is computed
once and never has to be kept in step.

## Compiled-run logs

```csharp
// Through the Agent:
var scope = tree.AsAgentScope().WithLogWriter(TextWriterLogWriter.For("workflow.log"));

// Or driving the engine yourself:
var session = new RuntimeContext { LogWriter = TextWriterLogWriter.For("workflow.log"), MaxRetainedLogs = 500 };
```

Lines reach the writer **in the order they happened** — a fan-out's branches interleave — and the same lines land in
the session's `Logs` in the same order, so the file and the in-memory view compare line for line.

⚙ `MaxRetainedLogs` bounds only the in-memory copy (`0` keeps none, the writer still gets everything). The default is
unbounded on purpose: the Agent's `RunCompiledWorkflow` reports `Logs` back to the model, so trimming by default would
quietly change what the model is shown.

⚙ A writer that throws does not break the run — the line is dropped and `RuntimeContext.LogWriteFailed` reports it.
Diagnostics never change what a run does.

⚙ `Write` is called on the thread driving the run (normally the UI thread). Queue the line inside your writer if that
IO must not happen there; `TextWriterLogWriter.For` flushes per line, so a host reading the file sees it immediately.

## Checkpointing a run

```csharp
var store = new FileCheckpointStore("run.place.json");
var session = new RuntimeContext { CheckpointStore = store };

await new RuntimeEngine().RunAsync(graph, session, ct);        // writes its place after each node succeeds
// … the process stops, the app closes, the host decides to carry on tomorrow …
var place = await store.LoadAsync(ct);
await new RuntimeEngine().RunAsync(graph, newSession, ct, place);
```

⚙ **Resume skips by node, not by position.** The checkpoint records which nodes are done; those are not driven
again, and what they produced is restored for the nodes behind them. Position would have been simpler and wrong — a
fan-out's branches carry interleaved compile orders, so any single threshold skips siblings that never ran.

⚙ **A checkpoint belongs to one graph.** Its `Shape` is the graph's nodes in drive order, and resuming onto a graph
whose shape differs is refused with an `InvalidOperationException` *before the session is touched* — the alternative
is driving these nodes with that graph's outputs.

⚙ **A graph that came back from serialization is refused — until it is re-keyed.** Restoring a graph gives every node
a fresh `RuntimeId`, so the shape no longer matches, and the refusal is right: those really are different node
objects. `ExecutionCheckpoint.Rekey(place, restoredGraph)` is the opt-in that says *I know they are, and here is the
mapping* — positional, checked against the node types in drive order. That is the shape a crash recovery has.

⚙ **Saving is best effort.** The store is written from inside the drive, so a fan-out's branches can save at once
(they interleave rather than run on threads, but an `await` is enough to overlap them) — implementations serialise
their own writes, and `FileCheckpointStore` does. A store that throws costs one log line and changes nothing else.

⚙ **What a file does not keep.** Payload values round-trip through JSON, which has one integer type: an `int` comes
back as a `long`, a `float` as a `double` (measured — `TypeNameHandling.All` does not change it). The engine's own
fields are exact, and `InMemoryCheckpointStore` keeps the object graph as it is. A group payload is filed by node
key, since a node reference cannot be written down.

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
        Options = Map(("env", Map(("FILESYSTEM_ROOT", "C:/data")))),    // per-server env vars
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
    Options = Map(("connectionTimeout", 30)),                       // seconds
};
// With auth:
Options = Map(
    ("headers", Map(("Authorization", "Bearer <token>"))));        // header-based auth
// or OAuth 2.0 (PKCE):
// Options = Map(("oauth", Map(
//     ("clientId", "..."), ("clientSecret", "..."),
//     ("redirectUri", "..."), ("scopes", new[] { "read" }))));
// For OAuth the host must register the authorization redirect:
mcp.WithOAuthAuthorizationRedirect(async (authUri, redirectUri, ct) =>
{
    await OpenBrowserAsync(authUri);
    return await WaitForCallbackAsync(redirectUri, ct);            // return the callback URL string with the code
});
```

> `McpServerConfiguration.Options` is a name → value map (`IReadOnlyDictionary<string, object?>`) — a nested map for a sub-section, an array for a list. Known keys: `headers` (HTTP headers), `oauth` (OAuth2), `connectionTimeout` (seconds or TimeSpan string), `transportMode` (`AutoDetect`/`StreamableHttp`/`Sse`), `ownsSession`, `env` (stdio environment variables), `workingDirectory`. **Unknown keys are rejected** (throws, never silently ignored).

### Agent-managed servers

`McpScope.CreateContextProvider()` contributes the management tools — `ListMcpServers` (status), `DescribeMcpServer` (export tool-capability prompts), `LoadMcpServers` (install & connect), `UnloadMcpServer` (tear down mid-session) — plus the tools of every connected server. Do not also register them with `WithTools`: the framework unions tool lists without deduplicating, so they would reach the model twice.

By default (`McpSelfServiceLevel.Closed`) the model can only load, unload and inspect servers the host pre-registered, and cannot author a configuration. `WithSelfService(level)` opens that in rungs — see `McpSelfServiceLevel`; at each rung the prompt text describing what the model may do is generated to match, and `AddMcpServer` is registered only once the gate is open.

`WithServers(...)` pre-registers the configurations the Agent may load by name; `LoadAsync` and `AddAsync` also record whatever they are given, so a host that only loads directly still ends up with a reloadable set.

### Global status VM (`McpStatusViewModel`)

Bind `McpScope.Status`: per-server `Name` / `StateText` (NotStarted / Installing / Connecting / Connected / Error) / `ToolCount` / `Error`, plus aggregates `ConnectedCount` / `ErrorCount` / `WorkingCount`. Updates marshal to the UI thread via `WithSynchronizationContext`.

## Links

- Repository: https://github.com/Axvser/VeloxDev
- Dependencies: [VeloxDev.Core](https://www.nuget.org/packages/VeloxDev.Core) · Microsoft.Extensions.AI · ModelContextProtocol · Microsoft.Agents.AI
