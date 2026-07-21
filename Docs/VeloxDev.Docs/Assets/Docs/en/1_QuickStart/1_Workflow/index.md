# Hello Workflow

Build an executable workflow in 2 minutes — define nodes, wire them, compile, and run.

---

## Demo

```
Input "payload" → ControllerNode → ProcessorNode → Output "payload"
```

A data payload **flows through** connected nodes; the compilation engine automatically plans the execution path.

## Steps

### 1. Create Project

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

### 2. Write Code

Replace `Program.cs` with:

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// ── Define Nodes ────────────────────────────────────────────────

// Entry point node (no inputs, only outputs)
[WorkflowBuilder.Node<NodeHelper>]
public partial class ControllerNode
{
	public ControllerNode() => InitializeWorkflow();

	[VeloxProperty] private string _seed = "Hello from VeloxDev!";
}

// Processing node (receives, processes, returns)
[WorkflowBuilder.Node<NodeHelper>]
public partial class ProcessorNode
{
	public ProcessorNode() => InitializeWorkflow();

	[VeloxProperty] private string _result = "";
}

// ── Wire → Compile → Execute ────────────────────────────────────

var ctrl  = new ControllerNode();
var proc  = new ProcessorNode();

var link = new LinkDefaultViewModel
{
	Sender   = ctrl.Slots[0],
	Receiver = proc.Slots[0]
};

var tree = new TreeDefaultViewModel();
tree.Nodes.Add(ctrl);
tree.Nodes.Add(proc);
tree.Links.Add(link);

// Compile: BFS forward topological sort starting from the controller
var compiler  = new WorkflowCompiler();
var plan      = compiler.Compile(ctrl, CompileMode.BFS)[0];

// Execute: payload flows through each node in order
var finalResult = await plan.ExecuteAsync("payload");
Console.WriteLine($"Workflow completed. Final result: {finalResult}");
```

### 3. Run

```shell
dotnet run
```

Output:
```
Workflow completed. Final result: payload
```

## Key Flow

| Step | What happens |
|------|-------------|
| `new ControllerNode()` | Creates entry node with one output Slot |
| `new ProcessorNode()` | Creates processing node with one input Slot |
| `new LinkDefaultViewModel { Sender, Receiver }` | Connects two slots as a directed edge |
| `compiler.Compile(ctrl, CompileMode.BFS)` | BFS traversal, produces ordered execution plan |
| `plan.ExecuteAsync("payload")` | Payload flows through each node sequentially |

## Try a GUI Demo

👉 [Examples/Workflow/WPF](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/WPF/Demo)
