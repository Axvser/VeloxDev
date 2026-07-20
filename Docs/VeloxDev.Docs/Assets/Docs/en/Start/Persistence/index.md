# Persistence

VeloxDev.Core.Extension provides a set of extension methods for view models, with the following features:
    - Dictionary<,> can use any type as Key, no longer limited to string
    - Only when both the setter and getter of a property are public will that property be serialized, and serialization only processes properties

> **Synchronization**

```csharp
using VeloxDev.MVVM.Serialization;

// serialize
var json = tree.Serialize();
File.WriteAllText(path, json);

// deserialize
var loadedJson = File.ReadAllText(path);
var loadedTree = loadedJson.Deserialize<TreeViewModel>();
```

> **asynchronous**

```csharp
using VeloxDev.MVVM.Serialization;

// Serialize
var json = await tree.SerializeAsync();
await File.WriteAllTextAsync(path, json);

// Deserialize
var loadedJson = await File.ReadAllTextAsync(path);
var loadedTree = await loadedJson.DeserializeAsync<TreeViewModel>();
```

> **streaming**

```csharp
using VeloxDev.MVVM.Serialization;

// serialize
await using var writeStream = File.Create(path);
await tree.SerializeToStreamAsync(writeStream);

// deserialize
await using var readStream = File.OpenRead(path);
var loadedTree = await readStream.DeserializeFromStreamAsync<TreeViewModel>();
```

> **Configuration Quick Reference**

| Target/Scenario | Configuration | Reason | Recommended? |
|---|---|---|---|
| Debugging, viewing JSON, diffing | `SerializationOptions.Create().WithIndented().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | Best JSON readability, fully preserves polymorphism, null values, default values, best for troubleshooting workflow recovery issues. | Strongly recommended |
| Formal save to file | `SerializationOptions.Create().WithCompact().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | Smaller file, but still retains full recovery info, suitable for workflow persistence. | Recommended |
| Only pursue minimal size | `SerializationOptions.Create().WithCompact().WithNullValueHandling(NullValueHandling.Ignore).WithDefaultValueHandling(DefaultValueHandling.Ignore)` | Can reduce JSON size, but may lose fields needed for recovery, not suitable for complex workflow object graphs. | Use with caution |
| Has derived nodes, interface properties, polymorphic objects | `WithTypeNameHandling(TypeNameHandling.Auto)` | Allows restoring actual runtime types during deserialization; typically needed in Workflow scenarios. | Mandatory |
| Desire complete state consistency after recovery | `WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | Keeps `null` and default values to avoid state deviation after deserialization. | Recommended |
| Just want JSON to be more readable | `WithIndented()` | Only improves readability, does not change object semantics. | As needed |
| Just want to reduce file size | `WithCompact()` | Removes indentation and whitespace, simplest compression method. | As needed |
| Sync API usage | `var json = tree.Serialize(options);` / `var tree = json.Deserialize<TreeViewModel>(options);` | Suitable for simple save/load. | Recommended |
| Safe loading | `json.TryDeserialize<TreeViewModel>(options, out var tree)` | More robust when external files or user input may be corrupted, won't throw exceptions directly. | Strongly recommended |
| Async API usage | `await tree.SerializeAsync(options)` / `await json.DeserializeAsync<TreeViewModel>(options)` | Suitable for UI, commands, file IO chains. | Recommended |
| Stream API usage | `await tree.SerializeToStreamAsync(stream, options)` / `await stream.DeserializeFromStreamAsync<TreeViewModel>(options)` | Suitable for file streams, network streams, memory streams. | Recommended |
| Most reliable Workflow default scheme | `SerializationOptions.Create().WithIndented().WithTypeNameHandling(TypeNameHandling.Auto).WithNullValueHandling(NullValueHandling.Include).WithDefaultValueHandling(DefaultValueHandling.Include)` | If you're not sure which to choose, use this set; safest for workflow object graphs. | Default preferred |