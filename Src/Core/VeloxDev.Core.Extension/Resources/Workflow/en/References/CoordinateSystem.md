## 📐 Coordinate System

The workflow canvas uses **standard computer graphics coordinates** (screen coordinates):

- **Origin (0, 0)** = top-left corner of the canvas.
- **Anchor.Horizontal (X)** increases **rightward** (left → right).
- **Anchor.Vertical (Y)** increases **downward** (top → bottom).
- **Anchor.Layer (Z)** = z-order (higher values render on top).

```
  (0,0) ────── X+ (Horizontal) ──────►
    │
    │
   Y+ (Vertical)
    │
    ▼
```

### Natural Language Translation

| User says | Meaning |
|---|---|
| "top-left", "upper-left" | Small X, Small Y (near origin) |
| "top-right", "upper-right" | Large X, Small Y |
| "bottom-left", "lower-left" | Small X, Large Y |
| "bottom-right", "lower-right" | Large X, Large Y |
| "center" | Midpoint of existing nodes' bounding box |
| "above node X" | Same X as node X, **smaller** Y |
| "below node X" | Same X as node X, **larger** Y |
| "left of node X" | **Smaller** X, same Y as node X |
| "right of node X" | **Larger** X, same Y as node X |
| "move up" | **Decrease** Y (negative offsetY) |
| "move down" | **Increase** Y (positive offsetY) |
| "move left" | **Decrease** X (negative offsetX) |
| "move right" | **Increase** X (positive offsetX) |

> ⚠️ **Common pitfall**: "up" means **smaller Y** (toward 0), not larger. "Down" means **larger Y** (away from 0).

### Quadrant Frame of Reference

Quadrant identity is determined by the **sign of X and Y** — the same rule as standard mathematics:

| Quadrant | X | Y | Position on canvas |
|---|---|---|---|
| **Q4 (default working region)** | + | + | Right of and below origin — the normal visible area |
| **Q1** | + | − | Right of origin, **above** it (negative Y, off standard canvas top) |
| **Q2** | − | − | Left of and above origin (both negative) |
| **Q3** | − | + | **Left** of origin, below it (negative X) |

> Because the canvas Y-axis points **downward**, "above the origin" means **Y < 0**.

When the user says "move to Q1 / Q3 / …", translate that into a coordinate shift so the layout bounding box lands in the correct sign region. See the **SmartLayout** skill for the exact delta calculation.
