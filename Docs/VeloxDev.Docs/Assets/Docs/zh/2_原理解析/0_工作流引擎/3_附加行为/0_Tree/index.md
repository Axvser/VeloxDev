# WorkflowSurfaceBehavior

宿主级行为，为 UserControl 提供**画布平移、缩放、网格吸附、小地图**等交互能力。

---

## 附加属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `IsEnabled` | `bool` | `false` | 启用/禁用所有表面行为 |
| `ScrollViewerName` | `string` | `null` | ScrollViewer 元素名称（必须） |
| `CanvasName` | `string` | `null` | Canvas 元素名称（必须） |
| `GridDecoratorName` | `string` | `null` | 实现 `IWorkflowGridDecorator` 的装饰器名称 |
| `MinimapOverlayName` | `string` | `null` | 实现 `IWorkflowMinimapOverlay` 的小地图名称 |
| `PointerPressSourceName` | `string` | `null` | 背景空白区域点击源（用于平移起始） |

## 功能

| 功能 | 触发方式 | 说明 |
|------|----------|------|
| 平移（Pan） | 在空白区拖拽 | 更新 ScrollViewer 偏移 |
| 缩放（Zoom） | Ctrl + 滚轮 | 缩放 Canvas 的 ScaleTransform |
| 网格渲染 | 配置 `GridDecoratorName` | 委托给 `IWorkflowGridDecorator` |
| 小地图 | 配置 `MinimapOverlayName` | 委托给 `IWorkflowMinimapOverlay` |

## XAML 用法

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay">
    <ScrollViewer x:Name="PART_ScrollViewer">
        <Canvas x:Name="PART_Canvas" />
    </ScrollViewer>
</UserControl>
```

## 编程调用

```csharp
// 刷新布局（如窗口大小变化后）
WorkflowSurfaceBehavior.Refresh(hostUserControl);
```
