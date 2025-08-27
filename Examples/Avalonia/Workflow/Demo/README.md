# Workflow

这份文档描述了工作流系统中的核心参数与命令

---

### 工作流核心接口表格

| **接口**               | **关键属性** | **关键命令** | **核心功能**                      |
|----------------------|--------------|--------------|-------------------------------|
| **IWorkflowContext** | `IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `UndoCommand` - 撤销操作 | 【共有基接口】 Undo命令具备真实行为 / 属性用途由用户自行决定 |
| **IWorkflowLink**    | `Sender` - 发送端槽位<br>`Processor` - 接收端槽位 | `DeleteCommand` - 删除连接 | 连接关系管理                        |
| **IWorkflowNode**    | `Parent` - 所属工作流树<br>`Anchor` - 节点坐标<br>`Size` - 节点尺寸<br>`Slots` - 槽位集合 | `CreateSlotCommand` - 创建槽位<br>`DeleteCommand` - 删除节点<br>`BroadcastCommand` - 广播消息<br>`ExecuteCommand` - 执行命令 | 节点核心功能                        |
| **IWorkflowSlot**    | `Targets` - 目标节点集合<br>`Sources` - 源节点集合<br>`Capacity` - 槽位能力类型<br>`State` - 当前状态<br>`Offset` - 槽位偏移坐标 | `ConnectingCommand` - 开始连接<br>`ConnectedCommand` - 完成连接 | 槽位连接管理器 （ 输入 / 输出口 ）          |
| **IWorkflowTree**    | `VirtualLink` - 虚拟连接线<br>`Nodes` - 节点集合<br>`Links` - 连接线集合 | `CreateNodeCommand` - 创建节点<br>`SetMouseCommand` - 设置鼠标状态<br>`SetSenderCommand` - 设置发送端<br>`SetProcessorCommand` - 设置接收端 | 工作流树控制                        |

---

### 核心实现类表格

| **类名** | **关键特性** | **运算符/方法** | **核心能力**     |
|----------|--------------|-----------------|--------------|
| **Anchor** | 二维坐标管理 (Left/Top/Layer) | `+`/`-` 运算符<br>`Equals`/`GetHashCode` | 二维空间定位       |
| **Size** | 尺寸管理 (Width/Height) | `+`/`-` 运算符<br>NaN值特殊处理 | 二维尺寸      |
| **LinkContext** | 连接状态自动检测<br>双向关系维护 | `DeleteCommand` - 连接删除<br>`OnSenderChanged` - 状态同步 | 【默认】连接线数据上下文 |
| **SlotContext** | 动态坐标偏移<br>连接状态跟踪 | `ConnectingCommand` - 启动连接<br>`ConnectedCommand` - 完成连接 | 【默认】连接调度上下文  |

---

## 源代码生成特性

> Context 生成是通用的，我们在此处用表格列出生成支持，但是需要注意，若你启用这些生成，最好不要与其它MVVM工具混用，否则可能出现生成冲突，该库自身提供的 MVVM 功能是推荐的

> View 因其在各个框架间的实现差异大，我们会延后在各个框架的适配包中实现其专属的生成器以辅助您实现用户交互

| 特性 | 应用对象 | 参数 | 描述 |
|------|----------|------|------|
| `Workflow.Context.Tree` | 工作流树类 | `slotType`, `linkType` | 自定义槽位和连接线类型 |
| `Workflow.Context.Node` | 工作流节点类 | 无 | 标记自定义节点类型 |
| `Workflow.Context.Slot` | 工作流槽位类 | 无 | 标记自定义槽位类型 |
| `Workflow.Context.Link` | 工作流连接线类 | 无 | 标记自定义连接线类型 |