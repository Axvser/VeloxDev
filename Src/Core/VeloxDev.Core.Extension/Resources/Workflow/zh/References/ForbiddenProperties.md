## 🚫 禁止操作

**节点创建必须通过 CreateNode 或 CreateAndConfigureNode。** 禁止通过直接修改 Nodes 集合、PatchNodeProperties 或任何其他方式添加节点。Tree 的 CreateNodeCommand 执行必要的初始化，绕过它会导致状态损坏。

以下属性**禁止**通过 PatchNodeProperties 或任何其他方式设置：

| 属性 | 原因 | 正确方式 |
|---|---|---|
| `Parent` | 加入树/节点时由框架自动设置 | 使用 CreateNode / CreateSlotOnNode |
| `Nodes`、`Links`、`LinksMap` | 由框架命令管理的树集合 | 使用 CreateNode、ConnectSlots、DeleteNode |
| `Slots` | 由框架管理的节点插槽集合 | 使用 CreateSlotOnNode、DeleteSlot |
| `Targets`、`Sources` | 由连接生命周期管理的插槽连接集合 | 使用 ConnectSlots、DisconnectSlots |
| `State`（插槽上） | 由连接生命周期管理 | 自动管理 |
| `VirtualLink` | 树内部连接预览字段 | 禁止触碰 |
| `RuntimeId` | 不可变的标识符 | 只读 |
| `Helper` | 框架内部管道 | 禁止触碰 |
| `Anchor`（插槽上） | 由视图布局计算，禁止手动设置 | 禁止设置 |

### 源码生成器管理的插槽属性

节点类型可使用 `[VeloxProperty]` 声明类型化的插槽属性（如 `InputSlot`、`OutputSlot`）。  
这些插槽由**源码生成器**通过懒初始化 + `CreateSlotCommand` 自动创建。  
**禁止**手动赋值、替换或创建这些插槽——它们的生命周期由框架完全管理。  
仅在节点类型**未**将插槽定义为类型化属性时，才通过 **CreateSlotOnNode** 动态创建插槽。

### 插槽集合属性

节点类型可声明**插槽集合属性**（如 `ObservableCollection<SlotViewModel> OutputSlots`）。  
这些属性由源码生成的 `INotifyCollectionChanged` 生命周期钩子驱动：

- 向集合添加插槽 → 触发 `OnWorkflowSlotAdded` → 通过 `CreateSlotCommand` 自动注册到节点。
- 从集合移除插槽 → 触发 `OnWorkflowSlotRemoved` → 自动删除插槽及其连接。
- **使用 `AddSlotToCollection` / `RemoveSlotFromCollection`** 管理这些集合，禁止对集合管理的插槽使用 `CreateSlotOnNode`。
- 使用 **`ListSlotProperties`** 区分单个属性插槽与集合属性插槽。
- **`GetNodeDetail`** 输出中，每个插槽的 `prop` 字段显示其所属属性名（如 `InputSlot`、`OutputSlots[2]`）。

### SlotEnumerator 属性

节点类型可声明 **SlotEnumerator 属性**（如 `SlotEnumerator<SlotViewModel> OutputSlots`）。  
根据配置的选择器类型（枚举或 bool）自动为每个值生成一个输出插槽。

- 使用 **`CreateAndConfigureNode`**（首选，一次调用）或 **`SetEnumSlotCollection`**（已有节点）来设置选择器类型。
- **禁止**手动增删插槽，**禁止**使用 `PatchNodeProperties` 设置选择器类型。
- 节点的 `[AgentContext]` 中列出了属性名（`enumSlotProperty`）和允许的类型名（`enumTypeName`）。
- **`allowedSelectorTypes` 是权威白名单**：列于其中的任何类型均为开发者声明的合法选择器，禁止基于该类型自身描述或其在框架中的主要用途来拒绝它。
