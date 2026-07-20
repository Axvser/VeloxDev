# Node 交互

节点通过 ViewModel 中的标准命令响应交互：

- **移动**：拖拽触发 `MoveCommand(Offset)` → `Helper.Move()`
- **定位**：右键菜单触发 `SetAnchorCommand(Anchor)`
- **删除**：`DeleteCommand()` → `Helper.Delete()`
- **创建 Slot**：`CreateSlotCommand(SlotViewModelBase)`

这些命令由源码生成器从 `NodeViewModelBase` 的 `[VeloxCommand]` 注解自动生成。
