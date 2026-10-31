# Framework-native providers

Three capabilities are the Agent Framework's own, attached through the scope rather than reimplemented.

| Method | What it adds |
|---|---|
| `WithTodoTracking(options?)` | the model's `todos_*` plan, so a long task keeps its place. Exposed as `scope.Todo` (a `TodoProvider`) for a host UI. |
| `WithAgentModes(options)` | `mode_get` / `mode_set`, with the modes and starting mode the host supplies. Exposed as `scope.AgentMode` (an `AgentModeProvider`). |
| `WithContextCompaction(maxContextWindowTokens, maxOutputTokens)` | old tool results summarised and old turns dropped as the window fills — pass the model's real numbers. |
| `WithContextProvider(factory)` | a provider of your own, appended after the framework-native ones. The factory receives the scope. |

⚙ **Their tools are not wrapped.** They neither read nor write the tree, so no budget accounting, UI marshalling or dirty marking applies.

⚙ **`WithContextProvider` contributes prompt text or tools of your own.** Pass the result to `ChatClientAgentOptions.AIContextProviders` alongside `scope.CreateContextProviders()`; the framework chains the providers so each sees the context built by the previous one. Contribute only tool names the scope does not already provide; the framework unions tool lists without deduplicating.

⚙ **The todo and agent-mode providers are ordered after the context sources** (skills, MCP, sub-agents) and before host factories, so the prompt reads "context first, instructions after" regardless of the order the host attached things.
