# Hello Workflow

Build an executable workflow in 2 minutes — define nodes, mount via standard commands, compile, and run.

---

All nodes are mounted to the Tree via **standard commands** (`CreateNode` / `SendConnection` / `ReceiveConnection`).

## Steps

### 1. Create Project

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

### 2. Write Code

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// Custom nodes with Slot declared as partial properties
// The generator auto-registers them into Slots during InitializeWorkflow()

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

// Mount to Tree and connect using standard commands
var tree = new TreeDefaultViewModel();
var ctrl = new ControllerNode();
var proc = new ProcessorNode();
var helper = tree.GetHelper();

helper.CreateNode(ctrl);        // Standard command: add node
helper.CreateNode(proc);        // Standard command: add node
helper.SendConnection(ctrl.OutputSlot);   // Standard command: set sender
helper.ReceiveConnection(proc.InputSlot); // Standard command: set receiver

// Compile and execute
var compiler = new WorkflowCompiler();
var plan = compiler.Compile(ctrl, CompileMode.BFS)[0];

var result = await plan.ExecuteAsync("payload");
Console.WriteLine($"Done: {result}");
```
