# 编译

`WorkflowCompiler` 将节点图转换为有序执行计划。

## 编译四维度

| 维度 | 可选值 |
|------|--------|
| Mode（模式） | `BFS` / `DFS` |
| Direction（方向） | `Forward` / `Reverse` |
| Scope（范围） | `FromNode` / `Omni` |
| CycleHandling（环路处理） | `Throw` / `Trim` / `Allow` |

4 个维度组合产生 **24 种编译策略**。

```csharp
using VeloxDev.WorkflowSystem.Compilation;

var compiler = new WorkflowCompiler();
var results = compiler.Compile(startNode, CompileMode.BFS,
    CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Throw);
var plan = results[0];
var finalResult = await plan.ExecuteAsync("seed");
```
