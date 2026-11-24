---
name: slot-enumerator
description: 配置 SlotEnumerator（IConditionalSlotProvider）属性——先 CreateNode 再用 SetEnumSlotCollection 设置枚举/bool 选择器，ISlotProvider 类型走 GetTypeSchema 加 JSON，用 GetEnumSlotByValue、SetEnumSlotChannel、ConnectEnumSlot 按条件值访问分支，且不得用 PatchNodeProperties 设置选择器
---

## Skill: SlotEnumerator（枚举插槽集合）

节点类型可声明 **SlotEnumerator 属性**——任何实现了 `IConditionalSlotProvider<TSlot>` 接口的属性（如 `SlotEnumerator<SlotViewModel> OutputSlots`）。若属性类型直接使用 `IConditionalSlotProvider<TSlot>` 接口本身，生成器以 `SlotEnumerator<TSlot>` 作为默认实现。这类属性根据配置的选择器类型（枚举或 bool）自动为每个值生成一个输出插槽。

### 创建节点

先调用 **`CreateNode`** 创建节点，然后调用 **`SetEnumSlotCollection`** 为其配置选择器类型：

```
CreateNode(fullTypeName, left, top, width, height)
SetEnumSlotCollection(nodeIndex, "OutputSlots", "Demo.ViewModels.MyEnum")
```

选择器驱动的节点创建时**没有任何端口**：插槽只在设置选择器之后才存在。`ListSlotProperties` 对 SlotEnumerator 属性报告 `count: 0` 就是这个意思，而不是属性缺失。

- 选择选择器前先读 `allowedSelectorTypes`：`ListSlotProperties` 一定会报告它；`GetComponentContext` 在属性没有自带描述时也会显示它。一句话类型摘要不含成员级细节。

### 修改已有节点的选择器

调用 `SetEnumSlotCollection`，传入属性名与选择器。**不要删除并重新创建节点**。

**枚举 / bool 选择器**（最常见）：传入完全限定类型名，`nonEnumTypeName` 留空。

**ISlotProvider（实例驱动）选择器**——当 `[SlotSelectors]` 白名单中存在非枚举类型（实现了 `ISlotProvider`）时：

1. **先查结构** — 调用 `GetTypeSchema(nonEnumTypeName)` 读取该类型的属性结构、嵌套类型及开发者说明。不得假设 JSON 结构。
2. **构造并应用** — 根据 schema 构建 JSON，再调用 `SetEnumSlotCollection`，JSON 填入 `selectorTypeOrJson`，完全限定类型名填入 `nonEnumTypeName`。

```
// 第一步 —— 调用前必须先查结构
GetTypeSchema("Demo.ViewModels.CustomRouteSelector")

// 第二步 —— 依据 schema 构造 JSON，然后应用
SetEnumSlotCollection(nodeIndex, "OutputSlots",
    "{\"Routes\":[{\"Key\":\"A\",\"Label\":\"路径A\"},{\"Key\":\"B\",\"Label\":\"路径B\"}]}",
    "Demo.ViewModels.CustomRouteSelector")
```

> ⚠️ 切换选择器类型会重建分支。框架自动将新分支按位置重连到旧分支的下游；切回已用过的类型会恢复其保存的连接——**无需手动重新连线**。若新类型成员更少，被移除位置的下游连接会丢失。

### 通过条件值访问内部插槽

条件值是枚举成员名、bool 选择器的 `True`/`False`，或 ISlotProvider 驱动枚举器的端口名。

- **`GetEnumSlotByValue`** — 返回插槽的运行时 ID（`slotId`）与通道。
- **`SetEnumSlotChannel`** — 修改某个枚举器插槽的通道。
- **`ConnectEnumSlot`** — 通过条件值把枚举器插槽连到另一个插槽，并验证结果。接收端可以是普通插槽、数字插槽索引，或另一个枚举器插槽（后者还需要它自己的条件值）。

### 强制连接协议（严格执行）

在连接 `SlotEnumerator` 内任意插槽之前，**必须**完整执行阶段一。跳过它会把同一源插槽连到多个意料外的分支。

**阶段一 — 补齐条件值**

- `SetEnumSlotCollection` 的回复会列出每个插槽的 `label` 与 `value`，那些 `value` 就是条件值。
- 若该回复已不在上下文中，调用 `ListSlotProperties(nodeIndex)` 读 `currentSelectorType`，再调 `GetTypeSchema(currentSelectorType)` 读其 `values`。
- ISlotProvider 驱动的枚举器，端口名就是条件值。

在拿全条件值列表之前，不得进入阶段二。

**阶段二 — 逐一连接每个插槽**

对每个条件值恰好调用一次 `ConnectEnumSlot`，`senderCondition` 与条件值精确匹配。不得使用阶段一未返回的条件值。

**示例（正确）**
```
// 阶段一
ListSlotProperties(2)  → currentSelectorType: "Demo.NetworkRequestMethod", count: 3
GetTypeSchema("Demo.NetworkRequestMethod") → values: GET, POST, DELETE

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

- 禁止手动增删 `SlotEnumerator` 属性上的插槽。
- 禁止用 `PatchNodeProperties` 设置选择器类型——会被拒绝。
- **`[SlotSelectors]` 是用户自定义类型的权威白名单。** 框架枚举（`SlotChannel`、`SlotState`）无论是否在白名单中都会被接受。
- `ListSlotProperties` 返回 `slotEnumerator: true`、`currentSelectorType`、`allowedSelectorTypes` 与当前插槽 ID——用于发现状态。
