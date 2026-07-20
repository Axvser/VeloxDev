# Persistence

Save and restore workflow state.

```csharp
// Save
var json = WorkflowSerializer.Serialize(tree);
await File.WriteAllTextAsync("workflow.json", json);

// Load
var loaded = WorkflowSerializer.Deserialize<WorkflowTreeViewModel>(json);
```

VeloxDev.Core.Extension provides serialization services that work across all platforms — Desktop, Browser, and Mobile.
