# Hello Workflow

2 分钟搭建一个可执行的工作流 —— 定义节点、连接、编译、运行。

---

## Demo 效果

```
输入 "payload" → ControllerNode → ProcessorNode → 输出 "payload"
```

运行后，数据负载**流经**两个节点，编译引擎自动规划执行路径。

## 操作步骤

### 1. 创建项目

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

### 2. 编写代码

打开 `Program.cs`，替换为以下内容：

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// ── 自定义节点（使用 [WorkflowBuilder.Node] 标记） ─────────

// 入口节点：工作流起点，无输入、仅有输出 Slot
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerNode
{
    public ControllerNode() => InitializeWorkflow();

    [VeloxProperty] private string _seed = "你好！VeloxDev！";
}

// 处理节点：接收数据、处理、转发
[WorkflowBuilder.Node<NodeHelper>]
public partial class ProcessorNode
{
    public ProcessorNode() => InitializeWorkflow();

    [VeloxProperty] private string _result = "";
}

// ── 连接 → 编译 → 执行 ────────────────────────────────────

var ctrl  = new ControllerNode();
var proc  = new ProcessorNode();

// LinkDefaultViewModel 连接两个 Slot（发送方 → 接收方）
var link = new LinkDefaultViewModel
{
    Sender   = ctrl.Slots[0],
    Receiver = proc.Slots[0]
};

var tree = new TreeDefaultViewModel();
tree.Nodes.Add(ctrl);
tree.Nodes.Add(proc);
tree.Links.Add(link);

// 编译：BFS 正向拓扑排序，从控制器节点出发
var compiler  = new WorkflowCompiler();
var plan      = compiler.Compile(ctrl, CompileMode.BFS)[0];

// 执行：负载沿已排序的 CompiledItem[] 顺序流经每个节点
var finalResult = await plan.ExecuteAsync("payload");
Console.WriteLine($"工作流执行完毕。最终结果：{finalResult}");
```

### 3. 运行

```shell
dotnet run
```

输出：
```
工作流执行完毕。最终结果：payload
```

## 关键流程解析

| 步骤 | 做了什么 |
|------|----------|
| `new ControllerNode()` | 创建入口节点，自带一个输出 Slot |
| `new ProcessorNode()` | 创建处理节点，自带一个输入 Slot |
| `new LinkDefaultViewModel { Sender, Receiver }` | 将两个 Slot 连接成有向边 |
| `compiler.Compile(ctrl, CompileMode.BFS)` | BFS 遍历图，生成有序执行计划 |
| `plan.ExecuteAsync("payload")` | 负载按序流经每个节点，返回结果 |

## 想试更多？

切换到有 UI 的 Demo 👉 [Examples/Workflow/WPF](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/WPF/Demo)
