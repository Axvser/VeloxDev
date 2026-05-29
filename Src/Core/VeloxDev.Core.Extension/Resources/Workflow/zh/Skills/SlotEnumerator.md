## Skill: SlotEnumerator（枚举插槽集合）

节点类型可声明 **SlotEnumerator 属性**——任何实现了 `IConditionalSlotProvider<TSlot>` 接口的属性（如 `SlotEnumerator<SlotViewModel> OutputSlots`）均可被识别。若属性类型直接使用 `IConditionalSlotProvider<TSlot>` 接口本身，生成器将自动以 `SlotEnumerator<TSlot>` 作为默认实现。这类属性根据配置的选择器类型（枚举或 bool）自动为每个值生成一个输出插槽。

### 创建节点

使用 **`CreateAndConfigureNode`**，同时传入 `enumSlotProperty` 和 `enumTypeName`——这是唯一正确的创建+配置方式：

```
CreateAndConfigureNode(fullTypeName, ..., enumSlotProperty="OutputSlots", enumTypeName="Demo.ViewModels.MyEnum")
```

- 节点的 `[AgentContext]` 描述中列出了 `enumSlotProperty` 名称和 `allowedSelectorTypes`——**调用前必须先读取**。
- 不要在创建前调用 `ListSlotProperties`，允许的类型已在 `[AgentContext]` 中给出。

### 修改已有节点的枚举类型

调用 `SetEnumSlotCollection(nodeIndex, propertyName, selectorTypeOrJson, nonEnumTypeName?)`。**不要删除并重新创建节点**。

**枚举 / bool 选择器**（最常见）：

- 在 `selectorTypeOrJson` 中传入完全限定类型名（如 `"Demo.ViewModels.MyEnum"`）。
- `nonEnumTypeName` 留空。

**ISlotProvider（实例驱动）选择器**：

当 `[SlotSelectors]` 白名单中存在非枚举类型（即实现了 `ISlotProvider` 的类型）时，必须遵循以下两步协议：

1. **先查结构** — 调用 `GetTypeSchema(nonEnumTypeName)` 读取该类型的属性结构、嵌套类型及开发者说明。不得在未检查 schema 的情况下假设 JSON 结构。
2. **构造并应用** — 根据 schema 构建正确的 JSON，再调用 `SetEnumSlotCollection`，将 JSON 填入 `selectorTypeOrJson`，完全限定类型名填入 `nonEnumTypeName`。

```
// 第一步 —— 调用前必须先查结构
GetTypeSchema("Demo.ViewModels.CustomRouteSelector")

// 第二步 —— 依据 schema 构造 JSON，然后应用
SetEnumSlotCollection(nodeIndex, "OutputSlots",
    "{\"Routes\":[{\"Key\":\"A\",\"Label\":\"路径A\"},{\"Key\":\"B\",\"Label\":\"路径B\"}]}",
    "Demo.ViewModels.CustomRouteSelector")
```

> ⚠️ 切换选择器类型会销毁旧输出插槽上的所有连接，完成后必须重新连线。

### 通过条件值直接访问内部插槽

现在可以直接通过 **条件值**（枚举名称或 `True`/`False`）访问 SlotEnumerator 内部插槽：

- **`GetEnumSlotByValue(nodeIndex, propertyName, conditionValue)`** – 返回枚举器内指定插槽的运行时 ID 和详情。
- **`SetEnumSlotChannel(nodeIndex, propertyName, conditionValue, channel)`** – 修改指定插槽的 `SlotChannel`。
- **`ConnectEnumSlot(senderNodeIndex, senderProperty, senderCondition, receiverNodeIndex, receiverSlot)`** – 通过条件值连接枚举器插槽到另一个插槽。自动验证连接成功。

### 强制连接协议（严格执行，不得跳过）

在连接 `SlotEnumerator` 内任意插槽之前，**必须**完整执行阶段一。跳过阶段一会导致同一源插槽被连接到多个意料外的分支。

**阶段一 — 先普查所有内部插槽**

对拥有该 SlotEnumerator 的节点调用 `ListSlotProperties(nodeIndex)`，找到 `slotEnumerator` 为 `true` 的条目，读取其 `currentSelectorType` 及所有条件值列表。在完整获得条件值列表之前，**不得进入阶段二**。

**阶段二 — 逐一连接每个插槽**

对阶段一列出的每个条件值，恰好调用一次 `ConnectEnumSlot`，`senderCondition` 须与条件值精确匹配。不得使用阶段一未返回的条件值调用 `ConnectEnumSlot`。

**示例（正确）**
```
// 阶段一
ListSlotProperties(2)  →  OutputSlots: ["GET", "POST", "DELETE"]

// 阶段二 —— 每个条件值恰好调用一次
ConnectEnumSlot(2, "OutputSlots", "GET",    5, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "POST",   6, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "DELETE", 7, "InputSlot")
```

**示例（错误 — 禁止）**
```
// ✗ 未普查直接连接 —— 可能连接到错误的条件
ConnectEnumSlot(2, "OutputSlots", "GET", 5, "InputSlot")
ConnectEnumSlot(2, "OutputSlots", "GET", 6, "InputSlot")  // 重复 → 一个插槽连了两个目标
```

### 规则

- **`enumTypeName` / `fullEnumTypeName`** 始终接受**完全限定类型名字符串**。在 C# API 层面，`SetSelector(object? selector)` 同时支持传入 `Type` 对象或字符串两种形式。
- 禁止手动增删 SlotEnumerator 属性上的插槽。
- 禁止用 `PatchNodeProperties` 设置选择器类型，该操作会被拒绝。
- **`[SlotSelectors]` 是用户自定义类型的权威白名单**；框架枚举（`SlotChannel`、`SlotState`）无论是否在白名单中，始终合法。
- 仅用于查看状态时才调用 `ListSlotProperties`，它会返回 `slotEnumerator: true`、`currentSelectorType` 和 `allowedSelectorTypes`，不作为创建前提条件使用。
