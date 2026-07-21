# Spatial Virtualization

For large workflows with 100+ nodes, `SpatialGridHashMap` provides grid-based spatial indexing to avoid full traversals on every render.

---

## How It Works

The canvas is divided into fixed-size grid cells. Nodes are mapped to cells based on their Anchor coordinates. Rendering and hit-testing **only access cells visible in the current viewport**, drastically reducing computation.

## Enabling

```csharp
using VeloxDev.WorkflowSystem.StandardEx;

var spatial = tree.EnableSpatialVirtualization(viewport, cellSize: 200);
```

Creates a `SpatialGridHashMap` + `WorkflowSpatialManager` bound to the tree via `ConditionalWeakTable`.

## API

```csharp
public static class WorkflowSpatialEx
{
	public static WorkflowSpatialManager EnableSpatialVirtualization(
		this IWorkflowTreeViewModel tree,
		IViewport viewport,
		int cellSize = 200);

	public static WorkflowSpatialManager? GetSpatialManager(this IWorkflowTreeViewModel tree);
}
```

## Parameters

| Parameter | Description | Recommended |
|-----------|-------------|-------------|
| `cellSize` | Grid cell size in pixels | 200–500 |
| `viewport` | Current visible area | Update on scroll |

Spatial virtualization provides diminishing returns below 50 nodes. Above 200 nodes, the performance improvement is significant.
