# Workflow [ V2 ]

这份文档描述了工作流系统中的核心参数、命令和回调

---

### ViewModel 生成支持

> 我们的工作流是 MVVM 的，建议您使用下述特性快速生成多个包含可选项的模板

| 特性 | 应用对象 | 参数 | 描述 |
|------|----------|------|------|
| `Workflow.Context.Tree` | 工作流树类 | `slotType` - 自定义槽位<br>`linkType` -连接线类型 | 标记自定义工作树类型 |
| `Workflow.Context.Node` | 工作流节点类 | `CanConcurrent` - 是否启用并发 | 标记自定义节点类型 |
| `Workflow.Context.Slot` | 工作流槽位类 | 无 | 标记自定义槽位类型 |
| `Workflow.Context.Link` | 工作流连接线类 | 无 | 标记自定义连接线类型 |

### ViewModel 生成结果

> 关键的属性、命令、扩展点，在您使用源生成特性后，即可参考这些信息来扩展功能

> IWorkflowContext 是所有工作流视图模型的共有接口，所以它的一切对其它工作流视图模型均适用

| **接口** | **关键属性** | **关键命令** | **核心功能**      | **回调方法** | **重要方法** |
|----------|--------------|--------------|---------------|--------------|--------------|
| **IWorkflowContext** | `IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `UndoCommand` - 撤销操作 | ( 共有 ) 基础行为控制 | `OnPropertyChanging`<br>`OnPropertyChanged`<br>`OnIsEnabledChanging/Changed`<br>`OnUIDChanging/Changed`<br>`OnNameChanging/Changed` | 无 |
| **IWorkflowLink** | `Sender` - 发送端槽位<br>`Processor` - 接收端槽位 | `DeleteCommand` - 删除连接<br>`UndoCommand` - 撤销操作 | 连接关系管理        | `OnSenderChanging/Changed`<br>`OnProcessorChanging/Changed` | 无 |
| **IWorkflowNode** | `Parent` - 所属工作流树<br>`Anchor` - 节点坐标<br>`Size` - 节点尺寸<br>`Slots` - 槽位集合 | `CreateSlotCommand` - 创建槽位<br>`DeleteCommand` - 删除节点<br>`BroadcastCommand` - 广播消息<br>`WorkCommand` - 执行节点工作任务<br>`UndoCommand` - 撤销操作 | 节点核心功能        | `OnParentChanging/Changed`<br>`OnAnchorChanging/Changed`<br>`OnSizeChanging/Changed`<br>`OnSlotAdded/Removed/Created`<br>`OnWorkExecuting/Canceled/Finished`<br>`OnFlowing/FlowCanceled/FlowFinished` | `FindLink()` - 查找连接 |
| **IWorkflowSlot** | `Parent` - 所属工作流节点<br>`Targets` - 目标节点集合<br>`Sources` - 源节点集合<br>`Capacity` - 槽位能力<br>`State` - 当前状态<br>`Anchor` - 绝对坐标<br>`Offset` - 偏移坐标<br>`Size` - 槽位尺寸 | `ConnectingCommand` - 开始连接<br>`ConnectedCommand` - 完成连接<br>`DeleteCommand` - 删除槽位<br>`UndoCommand` - 撤销操作 | 槽位连接管理        | <br>`OnParentChanging/Changed`<br>`OnCapacityChanging/Changed`<br>`OnStateChanging/Changed`<br>`OnAnchorChanging/Changed`<br>`OnOffsetChanging/Changed`<br>`OnSizeChanging/Changed` | 无 |
| **IWorkflowTree** | `VirtualLink` - 虚拟连接线<br>`Nodes` - 节点集合<br>`Links` - 连接线集合 | `CreateNodeCommand` - 创建节点<br>`SetPointerCommand` - 设置触点位置<br>`SetSenderCommand` - 设置发送端<br>`SetProcessorCommand` - 设置接收端<br>`ResetStateCommand` - 重置状态<br>`UndoCommand` - 撤销操作 | 工作流树控制        | `OnNodeAdded/Removed/Created`<br>`OnLinkAdded/Removed/Created`<br>`OnPointerChanging/Changed`<br>`OnNodeReseting/Reseted`<br>`OnLinkReseted` | `PushUndo()` - 压入撤销操作<br>`FindLink()` - 查找连接 |