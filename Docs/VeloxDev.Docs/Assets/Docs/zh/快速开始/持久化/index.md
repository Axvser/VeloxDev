# 持久化

```csharp
var json = WorkflowSerializer.Serialize(tree);
var loaded = WorkflowSerializer.Deserialize<WorkflowTreeViewModel>(json);
```

支持跨平台（Desktop / Browser / Mobile）的工作流状态序列化。
