# Tree

## [ Surface Behavior ]

在 UserControl 级别提供基于 Name 捕获的行为注册，涵盖 Canvas 、ScrollViewer 、网格装饰线等元素所需的交互行为

> **属性一览**

| 附加属性 | 类型 | 作用 |
|---|---|---|
| `IsEnabled` | `bool` | 启用整套工作流画布交互行为 |
| `ScrollViewerName` | `string?` | 指定用于滚动与视口计算的 `ScrollViewer` |
| `CanvasName` | `string?` | 指定工作流内容画布 |
| `GridDecoratorName` | `string?` | 指定网格/标尺装饰器，用于同步滚动偏移与内容偏移 |
| `PointerPressSourceName` | `string?` | 指定接收按下事件、用于启动画布平移的元素 |

## [ ViewPool Behavior ]

为 Canvas 提供对象池机制，重用视图元素对于降低GC压力与提升渲染性能起到关键作用

> **属性一览**

| 附加属性 | 类型 | 作用 |
| --- | --- | --- |
| ItemsSource | INotifyCollectionChanged | 数据源 |
| TemplateSelector | 取决于GUI框架对应的模板选择器接口 | 模板选择器 |