# Tree Helper

`IWorkflowTreeViewModelHelper` — Tree 组件的行为委托接口，默认实现为 `TreeHelper<TTree>`。

---

## Helper 接口：`IWorkflowTreeViewModelHelper`

| 方法 | 说明 |
|------|------|
| `CreateNode(node)` | 添加节点到 Nodes 集合 |
| `Submit(actionPair)` | 执行操作并压入撤销栈 |
| `Undo()` | 弹出撤销栈并反向执行 |
| `Redo()` | 弹出重做栈并执行 |
| `SendConnection(slot)` | 设置 VirtualLink.Sender |
| `ReceiveConnection(slot)` | 设置 VirtualLink.Receiver |
| `ResetVirtualLink()` | 重置虚拟连线 |
| `SetPointer(anchor)` | 设置指针位置 |
| `Install(component)` | 安装 Helper（绑定命令、事件） |
| `Uninstall(component)` | 卸载 Helper |
| `CloseAsync()` | 通知所有节点关闭 |

## 默认实现：`TreeHelper<TTree>`

```csharp
public class TreeHelper<TTree> : IWorkflowTreeViewModelHelper where TTree : IWorkflowTreeViewModel
{
    public TTree? Component { get; private set; }
    public virtual void Install(IWorkflowTreeViewModel component) { Component = (TTree)component; }
    public virtual void CreateNode(IWorkflowNodeViewModel node) { Component?.Nodes.Add(node); }
    public virtual void Submit(IWorkflowActionPair actionPair) { /* 执行并压入撤销栈 */ }
    public virtual void Undo() { /* 弹出撤销栈 */ }
    public virtual void Redo() { /* 弹出重做栈 */ }
    // …
}
```

所有方法均为 `virtual`，可按需重写。`Component` 属性指向关联的 Tree 实例。

## 自定义示例

```csharp
public class AgentHelper : TreeHelper<TreeViewModel>
{
    public override void CreateNode(IWorkflowNodeViewModel node)
    {
        base.CreateNode(node);
        // 自定义逻辑（如日志）
    }
}

[WorkflowBuilder.Tree<AgentHelper>]
public partial class TreeViewModel
{
    public TreeViewModel() => InitializeWorkflow();
    [VeloxProperty] private bool isWorkflowRunning = false;
}
```
