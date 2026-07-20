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
using VeloxDev.WorkflowSystem.Compilation;

var compiler = new WorkflowCompiler();
var results = compiler.Compile(startNode, CompileMode.BFS,
    CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Throw);
var plan = results[0];
var finalResult = await plan.ExecuteAsync("seed");
```
