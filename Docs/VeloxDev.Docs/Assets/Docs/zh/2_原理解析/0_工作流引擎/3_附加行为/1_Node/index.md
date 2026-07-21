# WorkflowNodeDragBehavior 与 WorkflowSlotLayoutBehavior

Node 卡片通过两个行为协作实现拖拽移动和 Slot 定位。

---

## WorkflowNodeDragBehavior

### 附加属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `IsEnabled` | `bool` | `false` | 启用拖拽 |
| `CoordinateHostName` | `string` | `null` | 坐标参考元素名称（通常为 Canvas） |
| `CoordinateHostType` | `Type` | `null` | 坐标参考元素的类型 |

### 行为

- `PreviewMouseLeftButtonDown`：记录拖拽起始位置
- `PreviewMouseMove`：计算偏移并调用 `MoveCommand.Execute(offset)`
- `PreviewMouseLeftButtonUp`：结束拖拽

### XAML 用法

```xml
<UserControl x:Name="PART_NodeCard"
             behaviors:WorkflowNodeDragBehavior.IsEnabled="True"
             behaviors:WorkflowNodeDragBehavior.CoordinateHostName="PART_Canvas">
    <!-- 节点内容 -->
</UserControl>
```

## WorkflowSlotLayoutBehavior

响应 Node 上 Slots 集合的变化，自动同步 Slot 控件的**锚点位置**到 Canvas 坐标。

### 附加属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `IsEnabled` | `bool` | `false` | 启用布局同步 |
| `SlotNames` | `string` | `null` | 分号分隔的 Slot 属性名列表（如 `"InputSlot;OutputSlot"`） |
| `SlotEnumeratorNames` | `string` | `null` | 分号分隔的 SlotEnumerator 属性名列表 |
| `CoordinateHostName` | `string` | `null` | 坐标参考元素名称 |
| `CoordinateHostType` | `Type` | `null` | 坐标参考元素的类型 |

### 行为

- 监听 Slot 属性的 `PropertyChanged`
- 监听 `SlotEnumerator` 的集合变更
- 在变化时重新计算 Slot 的 Anchor 坐标并同步到 Canvas

### XAML 用法

```xml
<UserControl behaviors:WorkflowSlotLayoutBehavior.IsEnabled="True"
             behaviors:WorkflowSlotLayoutBehavior.SlotNames="InputSlot;OutputSlot"
             behaviors:WorkflowSlotLayoutBehavior.SlotEnumeratorNames="Selector"
             behaviors:WorkflowSlotLayoutBehavior.CoordinateHostName="PART_Canvas">
</UserControl>
```
