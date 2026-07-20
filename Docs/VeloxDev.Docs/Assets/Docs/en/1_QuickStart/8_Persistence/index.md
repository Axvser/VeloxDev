# Persistence

Save and restore workflow state to JSON using `VeloxDev.Core.Extension` — works in any project type.

---

## Step 1 — Install

```shell
dotnet add package VeloxDev.Core.Extension
```

## Step 2 — Paste into `Program.cs`

```csharp
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

// ── Build a workflow ───────────────────────────────────────────────

var tree = new TreeViewModelBase();
var ctrl = new ControllerNode();
tree.Nodes.Add(ctrl);
tree.Links.Add(new LinkViewModelBase
{
    Sender = ctrl.Slots[0],
    Receiver = ctrl.Slots[0]
});

// ── Serialize to JSON ──────────────────────────────────────────────

var json = tree.Serialize();
Console.WriteLine(json);

// ── Deserialize back ───────────────────────────────────────────────

var restored = json.Deserialize<TreeViewModelBase>();
Console.WriteLine($"Restored tree with {restored.Nodes.Count} node(s)");

// ── Safe deserialization ───────────────────────────────────────────

if (json.TryDeserialize<TreeViewModelBase>(out var safe))
{
    Console.WriteLine($"Safe load: {safe.Nodes.Count} node(s)");
}

// ── Async with indented output ─────────────────────────────────────

var pretty = tree.Serialize(SerializationOptions.Create().WithIndented());
await File.WriteAllTextAsync("workflow.json", pretty);

var fromFile = (await File.ReadAllTextAsync("workflow.json"))
    .Deserialize<TreeViewModelBase>();
Console.WriteLine($"Loaded from file: {fromFile.Nodes.Count} node(s)");

// ── Supporting node ────────────────────────────────────────────────

public partial class ControllerNode : NodeViewModelBase
{
    public ControllerNode() => InitializeWorkflow();
    [VeloxProperty] private string _label = "Controller";
}
```
