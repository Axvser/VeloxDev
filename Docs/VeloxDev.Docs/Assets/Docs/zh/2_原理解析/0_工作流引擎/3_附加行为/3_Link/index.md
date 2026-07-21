# WorkflowCanvasTransformBehavior 与 WorkflowMinimapOverlay

---

## WorkflowCanvasTransformBehavior

画布坐标变换的通知载体——将 SurfaceBehavior 计算出的 `Transform` 通过附加属性广播，供节点和连接视图在 XAML 中绑定 `RenderTransform`。

### 附加属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Transform` | `Transform` | 当前画布变换矩阵 |

### XAML 绑定

```xml
<Canvas x:Name="PART_Canvas"
        RenderTransform="{Binding (behaviors:WorkflowCanvasTransformBehavior.Transform), RelativeSource={RelativeSource Self}}" />
```

该行为**不应用于宿主自身**，仅用于内部 Canvas 面板。`OnTransformChanged` 事件处理器为空——该属性仅作为通知载体。

## WorkflowMinimapOverlay

小地图概览控件，渲染所有节点的缩略图以及当前可见视口矩形。

### 依赖属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `WorkflowTree` | `IWorkflowTreeViewModel` | `null` | 绑定的工作流树 |
| `ScrollOffsetX/Y` | `double` | `0` | 滚动偏移（绑定到 ScrollViewer） |
| `ContentOffsetX/Y` | `double` | `0` | 内容偏移 |
| `ViewportWidth/Height` | `double` | `1` | 视口尺寸 |
| `MinimapWidth/Height` | `double` | `200/140` | 小地图尺寸 |
| `IsMinimapVisible` | `bool` | `true` | 可见性 |
| `RulerThickness` | `double` | `28` | 标尺厚度 |
| `LinkStrokeThickness` | `double` | `2` | 连接线粗细 |
| `MinimapBackground` | `Brush` | 深色 | 小地图背景 |
| `MinimapBorderBrush` | `Brush` | 蓝色 | 小地图边框 |
| `NodeBackground` | `Brush` | — | 节点缩略背景 |
| `ViewportBrush` | `Brush` | — | 视口指示器颜色 |

### 交互

仅视口指示器矩形支持拖拽导航。

### XAML 用法

```xml
<local:WorkflowMinimapOverlay x:Name="PART_MinimapOverlay"
    WorkflowTree="{Binding Tree}"
    ScrollOffsetX="{Binding ScrollX, Mode=OneWay}"
    ScrollOffsetY="{Binding ScrollY, Mode=OneWay}"
    ViewportWidth="{Binding ViewportWidth, Mode=OneWay}"
    ViewportHeight="{Binding ViewportHeight, Mode=OneWay}" />
```
