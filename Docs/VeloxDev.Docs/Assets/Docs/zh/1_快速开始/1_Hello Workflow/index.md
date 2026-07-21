# Hello Workflow

2 分钟搭建一个可执行的工作流 —— 定义节点、通过标准命令挂载、编译、运行。

---

## Demo 效果

所有节点通过 **标准命令**（`CreateNode` / `SendConnection` / `ReceiveConnection`）挂载到 Tree。

## 操作步骤

### 1. 创建项目

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

### 2. 编写代码

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// 自定义节点：声明 Slot 为 partial 属性
// 生成器在 InitializeWorkflow() 中自动注册到 Slots 集合

[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerNode
{
	public ControllerNode() => InitializeWorkflow();
	[VeloxProperty] public partial SlotViewModel OutputSlot { get; set; }
}

[WorkflowBuilder.Node<NodeHelper>]
public partial class ProcessorNode
{
	public ProcessorNode() => InitializeWorkflow();
	[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }
}

// 使用标准命令挂载到 Tree 并连接
var tree = new TreeDefaultViewModel();
var ctrl = new ControllerNode();
var proc = new ProcessorNode();
var helper = tree.GetHelper();

helper.CreateNode(ctrl);      // 标准命令：添加节点
helper.CreateNode(proc);      // 标准命令：添加节点
helper.SendConnection(ctrl.OutputSlot);   // 标准命令：设置发送方
helper.ReceiveConnection(proc.InputSlot); // 标准命令：设置接收方

// 编译并执行
var compiler = new WorkflowCompiler();
var plan = compiler.Compile(ctrl, CompileMode.BFS)[0];

var result = await plan.ExecuteAsync("payload");
Console.WriteLine($"执行完毕：{result}");
```
