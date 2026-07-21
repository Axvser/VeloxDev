# 条件 Slot 与路由

工作流引擎支持**条件型连接点**——Slot 不固定，而是根据数据或配置动态匹配。

---

## 场景

```csharp
// 示例：布尔路由节点有一个输出口 Selector
// selector 根据输入条件自动路由到不同的下游 Slot
```

## 核心类型

### `SlotEnumerator<TSlot>`

`IConditionalSlotProvider<TSlot>` 的默认实现。管理一组 `ConditionalSlot<TSlot>` 动态收集。

```csharp
public partial class SlotEnumerator<TSlot> : IConditionalSlotProvider<TSlot>
	where TSlot : IWorkflowSlotViewModel, new()
{
	[VeloxProperty] private IWorkflowNodeViewModel? _parent;
	[VeloxProperty] private Dictionary<object, TSlot> conditionMap = [];
	[VeloxProperty] public partial ObservableCollection<ConditionalSlot<TSlot>> Items { get; set; }
}
```

### `ConditionalSlot<TSlot>`

配对条件值与实际 Slot 的包装。

```csharp
public partial class ConditionalSlot<TSlot> where TSlot : IWorkflowSlotViewModel, new()
{
	[VeloxProperty] private string _name = string.Empty;   // 条件名称
	[VeloxProperty] private object? _value;                 // 路由键
	[VeloxProperty] private TSlot _slot = new();            // 后端 Slot 实例
}
```

### `SlotDefinition`

自定义 Slot 提供者的输出条目。

```csharp
public sealed class SlotDefinition(object value, string label = "")
{
	public object Value { get; }       // 路由键
	public string Label { get; }      // 显示标签
}
```

## 使用示例

```csharp
// 定义节点，使用 SlotEnumerator 作为条件输出
[WorkflowBuilder.Node<BoolSelectorHelper>]
public partial class BoolSelectorNode
{
	public BoolSelectorNode() => InitializeWorkflow();

	[VeloxProperty] public partial SlotEnumerator<SlotViewModel> Selector { get; set; }
}
```

生成器遇到 `SlotEnumerator<TSlot>` 类型的 `[VeloxProperty]` 时，自动：
1. 在 `InitializeWorkflow()` 中创建 `SlotEnumerator` 实例
2. 调用 `enumerator.Install(this, "PropertyName")` 绑定到父节点
3. 将所有 `ConditionalSlot<TSlot>.Slot` 注册到节点的 `Slots` 集合

## 接口

```csharp
public interface IConditionalSlotProvider<TSlot> where TSlot : IWorkflowSlotViewModel
{
	bool TrySelect(object? condition, [MaybeNullWhen(false)] out TSlot slot);
}
```

`SlotEnumerator<TSlot>` 遍历 `Items`，根据 `condition` 匹配 `ConditionalSlot.Value`。

完整 Demo 见 [Examples/Workflow/Common/Lib/ViewModels/Workflow/BoolSelectorNodeViewModel.cs](https://github.com/Axvser/VeloxDev/tree/master/Examples/Workflow/Common/Lib/ViewModels/Workflow/BoolSelectorNodeViewModel.cs)
