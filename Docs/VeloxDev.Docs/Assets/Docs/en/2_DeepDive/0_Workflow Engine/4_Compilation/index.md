# Compilation

The `WorkflowCompiler` translates the node graph into an ordered execution plan.

## Compilation Dimensions

| Dimension | Values |
|-----------|--------|
| Mode | `BFS` / `DFS` |
| Direction | `Forward` / `Reverse` |
| Scope | `FromNode` / `Omni` |
| CycleHandling | `Throw` / `Trim` / `Allow` |

Combined they create **24 compilation strategies**.

```csharp
var compiler = new WorkflowCompiler();
var results = compiler.Compile(controllerNode, CompileMode.BFS);
var plan = results[0];
await plan.ExecuteAsync(NetworkFlowContext.Create("seed"));
```
