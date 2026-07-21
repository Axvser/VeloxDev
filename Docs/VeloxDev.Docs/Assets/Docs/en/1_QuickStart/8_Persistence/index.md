# Persistence

Serialize your workflow to JSON — share the same data across Desktop, Browser, and Mobile.

---

## Demo

```csharp
var json = tree.Serialize();          // Tree → JSON
var restored = json.Deserialize<TreeDefaultViewModel>(); // JSON → Tree
Console.WriteLine($"Restored tree with {restored.Nodes.Count} node(s)");
```

## Steps

### 1. Install

```shell
dotnet add package VeloxDev.Core.Extension
```

### 2. Serialize

```csharp
using VeloxDev.Extension.Serialization;

// Synchronous
string json = tree.Serialize();

// Asynchronous
string jsonAsync = await tree.SerializeAsync();

// Indented formatting
string pretty = tree.Serialize(SerializationOptions.Create().WithIndented());
await File.WriteAllTextAsync("workflow.json", pretty);

// UTF8 bytes
byte[] bytes = tree.SerializeToUtf8Bytes();
```

### 3. Deserialize

```csharp
// Safe deserialization
if (json.TryDeserialize<TreeDefaultViewModel>(out var restored))
    Console.WriteLine($"Safe load: {restored.Nodes.Count} node(s)");

// Throws on failure
var tree = json.Deserialize<TreeDefaultViewModel>();

// Load from file
var fromFile = (await File.ReadAllTextAsync("workflow.json"))
    .Deserialize<TreeDefaultViewModel>();
```

## What's Included

| Component | Serialized Fields |
|-----------|-------------------|
| **Tree** | Layout (canvas, viewport offset), VirtualLink |
| **Node** | Anchor, Size, all `[VeloxProperty]` field values |
| **Slot** | Anchor, Channel, State, Targets/Sources references |
| **Link** | Sender/Receiver slot IDs, IsVisible |

## Full API

| Method | Description |
|--------|-------------|
| `tree.Serialize()` | Serialize to JSON string |
| `tree.SerializeAsync()` | Async serialization |
| `json.Deserialize<T>()` | Deserialize (throws on failure) |
| `json.TryDeserialize<T>(out var)` | Safe deserialization |
| `tree.SerializeToUtf8Bytes()` | Serialize to UTF8 bytes |
| `tree.SerializeToStreamAsync(stream)` | Serialize to stream |
| `new SerializationOptions().WithIndented()` | Indented formatting |
