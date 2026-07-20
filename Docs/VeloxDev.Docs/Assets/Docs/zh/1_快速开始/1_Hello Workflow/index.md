# Hello Workflow

创建一个新控制台项目，粘贴代码并运行 — 2 分钟内获得一个可执行的工作流。

---

## 第一步 — 创建项目

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

## 第二步 — 将以下代码完整粘贴到 `Program.cs`

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// ── 1. 定义节点 ViewModel ──────────────────────────────────────

// 控制器 — 工作流入口（无输入，仅有输出）
public partial class ControllerNode : NodeViewModelBase
{
    public ControllerNode() => InitializeWorkflow();

    [VeloxProperty] private string _seed = "你好！VeloxDev！";
}

// 处理器 — 接收数据、转换、转发
public partial class ProcessorNode : NodeViewModelBase
{
    public ProcessorNode() => InitializeWorkflow();

    [VeloxProperty] private string _result = "";
}

// ── 2. 连接、编译、执行 ──────────────────────────────────────────

var ctrl  = new ControllerNode();
var proc  = new ProcessorNode();

// 连接：ctrl 的默认输出口 → proc 的默认输入口
var link = new LinkViewModelBase
{
    Sender   = ctrl.Slots[0],
    Receiver = proc.Slots[0]
};

var tree = new TreeViewModelBase();
tree.Nodes.Add(ctrl);
tree.Nodes.Add(proc);
tree.Links.Add(link);

// 编译（BFS、正向、从控制器开始）
var compiler  = new WorkflowCompiler();
var results   = compiler.Compile(ctrl, CompileMode.BFS);
var plan      = results[0];

// 执行 — 上下文对象按序流过每个节点
var finalResult = await plan.ExecuteAsync("payload");
Console.WriteLine($"工作流执行完毕。最终结果：{finalResult}");
```

## 第三步 — 运行

```shell
dotnet run
```

输出：
```
工作流执行完毕。最终结果：payload
```

数据负载经过 `ControllerNode`（通过 Slot[0] 广播）→ `ProcessorNode`（接收并返回）。无需 GUI 即可无头运行。
