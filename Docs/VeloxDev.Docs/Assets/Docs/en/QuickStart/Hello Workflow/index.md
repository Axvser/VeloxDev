# Hello Workflow

Build a minimal but complete workflow in 5 minutes.

---

## Step 1 — Create the ViewModels

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public partial class MyController : ControllerViewModel { }

public partial class StartNode : WorkflowNodeViewModel
{
	[VeloxProperty] private string _message = "Hello!";
}

public partial class EndNode : WorkflowNodeViewModel
{
	[VeloxProperty] private string _result = "";
}
```

## Step 2 — Wire them Together

```csharp
var controller = new MyController();
var start = new StartNode();
var end = new EndNode();

// Connect start → end via their default slots
WorkflowLinkViewModel.Connect(start.Slots[0], end.Slots[0]);
```

## Step 3 — Add to the Tree

```csharp
var tree = new WorkflowTreeViewModel();
tree.Controller = controller;
tree.Nodes.Add(start);
tree.Nodes.Add(end);
```

## Step 4 — Render in XAML

```xml
<framework:WorkflowTreeView DataContext="{Binding Tree}" />
```

## Step 5 — Run

```csharp
var compiler = new WorkflowCompiler();
var results = compiler.Compile(tree.Controller, CompileMode.BFS);
var plan = results[0];
await plan.ExecuteAsync(NetworkFlowContext.Create("payload"));
```

That's it. You've just created an executable workflow.
