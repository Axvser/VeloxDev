## Skill: SlotEnumerator（枚举插槽集合）

节点类型可声明 **SlotEnumerator 属性**（如 `SlotEnumerator<SlotViewModel> OutputSlots`）。根据配置的选择器类型（枚举或 bool）自动为每个值生成一个输出插槽。

### 创建节点

使用 **`CreateAndConfigureNode`**，同时传入 `enumSlotProperty` 和 `enumTypeName`——这是唯一正确的创建+配置方式：

```
CreateAndConfigureNode(fullTypeName, ..., enumSlotProperty="OutputSlots", enumTypeName="Demo.ViewModels.MyEnum")
```

- 节点的 `[AgentContext]` 描述中列出了 `enumSlotProperty` 名称和 `allowedSelectorTypes`——**调用前必须先读取**。
- 不要在创建前调用 `ListSlotProperties`，允许的类型已在 `[AgentContext]` 中给出。

### 修改已有节点的枚举类型

调用 `SetEnumSlotCollection(nodeIndex, propertyName, fullEnumTypeName)`。**不要删除并重新创建节点**。

> ⚠️ 切换枚举类型会销毁旧输出插槽上的所有连接，完成后必须重新连线。

### 规则

- 禁止手动增删 SlotEnumerator 属性上的插槽。
- 禁止用 `PatchNodeProperties` 设置选择器类型，该操作会被拒绝。
- **`[SlotSelectors]` 是用户自定义类型的权威白名单**；框架枚举（`SlotChannel`、`SlotState`）无论是否在白名单中，始终合法。
- 仅用于查看状态时才调用 `ListSlotProperties`，它会返回 `slotEnumerator: true`、`currentSelectorType` 和 `allowedSelectorTypes`，不作为创建前提条件使用。
