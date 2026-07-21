# WorkflowSlotConnectionBehavior

Slot 视图的行为——点击 Slot 发起或接受连接。

---

## 附加属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `IsEnabled` | `bool` | `false` | 启用连接交互 |

## 行为

| 事件 | 操作 |
|------|------|
| `PreviewMouseLeftButtonDown` | 调用 `SendConnectionCommand.Execute(null)` |
| `PreviewMouseLeftButtonUp` | 调用 `ReceiveConnectionCommand.Execute(null)` |

发送方按下时，Tree 的 `WorkflowSurfaceBehavior` 处理拖拽过程、绘制**虚拟连线**（幽灵指引线）；释放到目标 Slot 时自动创建 `Link`。

## XAML 用法

```xml
<ContentControl behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True" />
```

## 完整连接流程

1. 在输出 Slot 上按下鼠标 → `SendConnectionCommand` 设置 VirtualLink.Sender
2. 拖拽 → `SetPointerCommand(Anchor)` 实时更新虚拟连线端点（由 SurfaceBehavior 处理）
3. 指针进入输入 Slot → `ReceiveConnectionCommand` 设置 VirtualLink.Receiver
4. 释放 → `SubmitCommand(WorkflowActionPair)` 创建并添加 Link 到 Tree
5. ESC 取消 → `ResetVirtualLinkCommand` 清除幽灵线

## SlotChannel 方向约束（Flags 枚举）

| 枚举值 | 值 | 含义 |
|--------|:--:|------|
| `None` | `0` | 无权限（不允许任何连接） |
| `OneTarget` | `1` | 最多 1 条出向连接 |
| `OneSource` | `2` | 最多 1 条入向连接 |
| `OneBoth` | `3` | 最多 1 出 + 1 入（OneTarget \| OneSource） |
| `MultipleTargets` | `4` | 多条出向连接 |
| `MultipleSources` | `8` | 多条入向连接 |
| `MultipleBoth` | `12` | 多条出 + 多条入（默认值） |
