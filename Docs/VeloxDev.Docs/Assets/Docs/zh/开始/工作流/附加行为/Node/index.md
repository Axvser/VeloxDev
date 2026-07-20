# Node

## [ Slot Layout Behavior ]

节点挂载连接器，这组附加属性负责这些连接器的行为

> **属性一览**

| 附加属性 | 类型 | 作用 |
| --- | --- | --- |
| IsEnabled | bool | 开关 |
| SlotNames | string[] | 单个Slot、Slot枚举器 |
| CoordinateHostName | string | Slot需要在一个宿主容器中布局 |

## [ Node Drag Behavior ]

处理节点拖拽行为

> **属性一览**

| 附加属性 | 类型 | 作用 |
| --- | --- | --- |
| IsEnabled | bool | 开关 |
| CoordinateHostName | string | 拖拽将基于一个宿主容器进行偏移坐标检测 |