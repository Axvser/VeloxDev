# 持久化

使用 `VeloxDev.Core.Extension` 的序列化扩展保存和恢复工作流 — 适用于任何项目类型。

---

## 第一步 — 安装

```shell
dotnet add package VeloxDev.Core.Extension
```

## 第二步 — 粘贴到 `Program.cs`

```csharp
using VeloxDev.MVVM;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

// 构建工作流
var tree = new TreeViewModelBase();
var ctrl = new ControllerNode();
tree.Nodes.Add(ctrl);
tree.Links.Add(new LinkViewModelBase
{
    Sender = ctrl.Slots[0],
    Receiver = ctrl.Slots[0]
});

// 序列化为 JSON
var json = tree.Serialize();
Console.WriteLine(json);

// 反序列化还原
var restored = json.Deserialize<TreeViewModelBase>();
Console.WriteLine($"还原了 {restored.Nodes.Count} 个节点");

// 安全加载
if (json.TryDeserialize<TreeViewModelBase>(out var safe))
    Console.WriteLine($"安全加载：{safe.Nodes.Count} 个节点");

// 异步 + 格式化
var pretty = tree.Serialize(SerializationOptions.Create().WithIndented());
await File.WriteAllTextAsync("workflow.json", pretty);

var fromFile = (await File.ReadAllTextAsync("workflow.json"))
    .Deserialize<TreeViewModelBase>();
Console.WriteLine($"从文件加载：{fromFile.Nodes.Count} 个节点");

// 节点定义
public partial class ControllerNode : NodeViewModelBase
{
    public ControllerNode() => InitializeWorkflow();
    [VeloxProperty] private string _label = "控制器";
}
```

## 第三步 — 运行

```shell
dotnet run
```

`VeloxDev.Core.Extension` 提供跨平台序列化支持 — Desktop、Browser、Mobile。
