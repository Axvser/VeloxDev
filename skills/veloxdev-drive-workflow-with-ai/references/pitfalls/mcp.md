# Pitfalls — MCP

Failures the repository has actually hit. Each entry names the symptom, the rule it violates, and the fix. The correct rules are in [mcp.md](../practices/mcp.md).

## A server fails but the others load

**Symptom:** one server is `Error`, the rest are `Connected`.

**Cause:** a failing server is isolated by design. `LoadOneAsync` records the message, raises `ServerError`, and returns an empty tool set; the batch continues.

**Fix:** inspect `mcp.Status.Servers` and the `ServerError` payload. A bad config, a missing runtime, or a refused launch is local to that server.

## A server has no tools after loading

**Symptom:** the server reports `Connected`, but no tools appear.

**Cause:** the server's tools are contributed by the MCP provider, which is attached with `WithMcps`; a standalone `McpScope` exposes them through `LoadedTools` / `GetServerTools`. The server may also have started with an empty tool set.

**Fix:** attach `WithMcps` on a workflow scope; on a standalone MCP host, take the tools from the provider. Check the server's own tool list in its status.

## Every MCP tool appears twice

**Symptom:** the model sees duplicate tool names.

**Cause:** `mcp.LoadedTools` was merged into `ChatOptions.Tools` while the provider also offers them. The framework unions tool lists without deduplicating.

**Fix:** remove the manual merge. `WithMcps` is the only route that wraps and contributes them correctly.

## A typo in `Options` aborts startup

**Symptom:** the server fails with "unknown Options key(s)".

**Cause:** the options map is validated against the known names; an unknown key is a startup failure.

**Fix:** use one of `env`, `workingDirectory`, `headers`, `oauth`, `connectionTimeout`, `transportMode`, `ownsSession`, and a nested `Dictionary<string, object?>` for a section. An anonymous object is not accepted.

## The model installs software on the user's machine

**Symptom:** a local MCP server is installed and launched although the host did not intend it.

**Cause:** `AddMcpServer` with a local run mode installs and runs software; the mode decides whether the call asks, and no `Deny` rule forbids the kind.

**Fix:** write `WithPermissionRule(PermissionDecision.Deny, "mcp-add:local")`. It holds in every mode, `Bypass` included, while Http servers are unaffected. For a standalone `McpScope`, gate `AddMcpServer` / `SetMcpServerArguments` through the confirmation handler.

## A standalone `McpScope` runs management tools without asking

**Symptom:** `LoadMcpServers` / `UnloadMcpServer` run without a prompt.

**Cause:** a standalone scope has no wrapper around its tools; only `AddMcpServer` and `SetMcpServerArguments` ask for themselves.

**Fix:** gate the load/unload calls in the host, or compose the scope into a workflow agent so the policy wrapper applies.

## A reconfigured server drops its old tools

**Symptom:** after changing a server's configuration, the model's tool names change or disappear.

**Cause:** the old connection was released before the new one answered, or the new configuration offers a different tool set.

**Fix:** reconfiguration connects first and releases the old connection only after the new one answers. Tool objects are `McpToolProxy` instances, so names stay stable while the client underneath is replaced; the render invalidates only when the name set changes.

## Assigning `McpScope` to `IAsyncDisposable` does not compile

**Symptom:** `IAsyncDisposable d = mcp;` fails.

**Cause:** `McpScope` exposes `DisposeAsync()` but does not declare the interface.

**Fix:** use `await using var mcp = new McpScope()`, which works through the pattern, or call `await mcp.DisposeAsync()` explicitly.

## Unloading does not terminate a stdio server

**Symptom:** the child process keeps running.

**Cause:** the synchronous `UnloadServer` was used and the host did not wait, or the scope was not disposed.

**Fix:** prefer `UnloadServerAsync`; dispose the scope when the session ends. The synchronous overload blocks until the client is disposed.
