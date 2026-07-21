# 空间虚拟化

对超过 100 个节点的大型工作流，`SpatialGridHashMap` 提供基于网格的空间索引，避免全量遍历。

---

## 原理

画布被划分为固定大小的网格单元。节点根据其 Anchor 坐标映射到对应的单元格。渲染和命中测试时**仅访问当前视口覆盖的单元格**，大幅减少计算量。

## 启用方式

```csharp
using VeloxDev.WorkflowSystem.StandardEx;

// 为指定 tree 启用空间虚拟化
var spatial = tree.EnableSpatialVirtualization(viewport, cellSize: 200);
```

内部创建 `SpatialGridHashMap` + `WorkflowSpatialManager`，通过 `ConditionalWeakTable` 与 tree 绑定。

## WorkflowSpatialManager

```csharp
public static class WorkflowSpatialEx
{
	// 启用空间管理，返回 WorkflowSpatialManager
	public static WorkflowSpatialManager EnableSpatialVirtualization(
		this IWorkflowTreeViewModel tree,
		IViewport viewport,
		int cellSize = 200);

	// 获取已绑定的空间管理器
	public static WorkflowSpatialManager? GetSpatialManager(this IWorkflowTreeViewModel tree);
}
```

## 核心组件

| 组件 | 职责 |
|------|------|
| `SpatialGridHashMap` | 哈希网格空间映射 |
| `WorkflowSpatialManager` | 管理可见/隐藏节点集合 |
| `Viewport` | 视口定义（位置 + 尺寸） |
| `CanvasLayout` | 画布布局定义 |
| `ISpatialBoundsProvider` | 为节点/连接提供边界矩形 |

## 参数

| 参数 | 说明 | 建议值 |
|------|------|--------|
| `cellSize` | 网格单元格大小 | 200–500px |
| `viewport` | 当前可见区域 | 随滚动实时更新 |

节点数量在 50 以内时，空间虚拟化的收益不明显；超过 200 时性能提升显著。
