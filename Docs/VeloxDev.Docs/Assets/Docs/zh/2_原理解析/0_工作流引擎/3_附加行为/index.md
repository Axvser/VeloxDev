# 附加行为

`VeloxDev.WorkflowSystem.AttachedBehaviors` 命名空间下的附加行为使工作流画布具备交互能力。

| 行为 | 功能 |
|------|------|
| `WorkflowSurfaceBehavior` | 平移、缩放、网格装饰器、小地图 |
| 标准命令 | 鼠标/键盘事件绑定（拖拽连接、选择、移动） |
| 虚拟连线 | 拖拽连接时的幽灵指引线 |

## 使用 WorkflowSurfaceBehavior（WPF 示例）

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas">
    <!-- 画布内容 -->
</UserControl>
```
