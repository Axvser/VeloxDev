# Slot 连接流程

Slot 连接是一个由 Tree 管理的多步流程：

1. **在输出 Slot 上按下** → `SendConnectionCommand`
2. **拖拽** → `SetPointerCommand(Anchor)` 更新虚拟连线端点
3. **指针进入输入 Slot** → `ReceiveConnectionCommand` 高亮
4. **在输入 Slot 上释放** → `SubmitCommand(WorkflowActionPair)` 创建连接
5. **取消**（ESC）→ `ResetVirtualLinkCommand` 清除幽灵线

`SlotChannel` 属性控制方向约束：`Input`、`Output`、`OneBoth`、`None`。
