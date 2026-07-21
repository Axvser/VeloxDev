# Hello Workflow

Create a new console project, paste the code, and run — you'll have an executable workflow in under 2 minutes.

---

## Step 1 — Create a Project

```shell
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package VeloxDev.Core
```

## Step 2 — Paste This Entire File into `Program.cs`

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

// ── 1. Define your Node ViewModels ──────────────────────────────

// Controller — the workflow entry point (no inputs, only outputs)
public partial class ControllerNode : NodeDefaultViewModel
{
	public ControllerNode() => InitializeWorkflow();

	[VeloxProperty] private string _seed = "Hello from VeloxDev!";
}

// Processor — receives data, transforms it, and forwards it
public partial class ProcessorNode : NodeDefaultViewModel
{
	public ProcessorNode() => InitializeWorkflow();

	[VeloxProperty] private string _result = "";
}

// ── 2. Wire, Compile, and Execute ───────────────────────────────

var ctrl  = new ControllerNode();
var proc  = new ProcessorNode();

// Link: ctrl's default slot → proc's default slot
var link = new LinkDefaultViewModel
{
	Sender   = ctrl.Slots[0],
	Receiver = proc.Slots[0]
};

var tree = new TreeDefaultViewModel();
tree.Nodes.Add(ctrl);
tree.Nodes.Add(proc);
tree.Links.Add(link);

// Compile (BFS, forward, from controller)
var compiler  = new WorkflowCompiler();
var results   = compiler.Compile(ctrl, CompileMode.BFS);
var plan      = results[0];

// Execute — the context object flows through each node in order
var finalResult = await plan.ExecuteAsync("payload");
Console.WriteLine($"Workflow completed. Final result: {finalResult}");
```

## Step 3 — Run

```shell
dotnet run
```

Output:
```
Workflow completed. Final result: payload
```

The payload passed through `ControllerNode` (broadcast via Slot[0]) → `ProcessorNode` (received and returned). No GUI needed — the engine works headlessly.
