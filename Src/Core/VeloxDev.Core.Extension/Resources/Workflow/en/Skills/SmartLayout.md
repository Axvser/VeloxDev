## 🗺️ Skill: Smart Layout

Use this skill whenever you need to arrange multiple nodes intelligently — whether creating them fresh, reorganizing an existing graph, or responding to user requests like "tidy up", "auto-arrange", or "fix the layout".

---

### Step 1 — Read the Topology First

Before placing any node, call `ListNodes` and examine the connection graph:

- Identify **source nodes** (no incoming connections, in-degree = 0). These are layout anchors.
- Identify **sink nodes** (no outgoing connections, out-degree = 0).
- Trace **chains and branches**: a node with multiple outgoing connections creates a branch point; a node with multiple incoming connections is a merge point.
- Identify **disconnected subgraphs** (isolated clusters with no links to the main graph). Each subgraph is laid out independently.

> Never guess topology from node names. Always derive it from actual slot/link data.

---

### Step 2 — Choose a Layout Strategy

| Graph shape | Recommended strategy |
|---|---|
| Linear chain | Horizontal row (left → right) following connection order |
| Tree / DAG (directed acyclic graph) | Layered layout: sources in leftmost column, each layer one step to the right |
| Dense / cyclic | Layered layout with cycle-breaking: pick the node with lowest in-degree as the cycle root |
| Multiple disconnected subgraphs | Lay out each subgraph independently, stack them vertically with generous gap |
| User requests a specific direction | Respect "top-to-bottom", "left-to-right", etc. and map to X/Y accordingly |

---

### Step 3 — Assign Layers (Topology-Aware)

Use **longest-path layering** from source nodes:

1. Assign all source nodes to layer 0.
2. For each node, its layer = max(predecessor layers) + 1.
3. Repeat until stable (handles forward edges in cycles by promoting nodes).

**Within each layer**, order nodes to minimize edge crossings using the **barycenter heuristic**:
- For each node in the current layer, compute the average position index of its predecessors in the previous layer.
- Sort nodes in the current layer by ascending barycenter value.
- Repeat the sort pass for a second layer-sweep in the reverse direction to further reduce crossings.

---

### Step 4 — Compute Positions Respecting Node Sizes

Do **not** assume all nodes are the same size. Use each node's actual `Size.Width` and `Size.Height`:

- **Horizontal layout** (layers = columns):
  - Column X = `startX + sum(max widths of all previous columns) + column_index × gapX`
  - Within a column, stack nodes vertically: `Y = startY + sum(heights of previous nodes in column) + node_index × gapY`
- **Vertical layout** (layers = rows): swap X and Y roles above.

Minimum recommended gaps:
- `gapX` (between layers): **80 px**
- `gapY` (between nodes within a layer): **40 px**

After positioning, verify **no two nodes overlap** by checking bounding box intersections:
- Node A rect: `(ax, ay, ax+aw, ay+ah)`, Node B rect: `(bx, by, bx+bw, by+bh)`
- Overlap if: `ax < bx+bw AND ax+aw > bx AND ay < by+bh AND ay+ah > by`
- If overlap detected, increase the gap for that layer and recompute.

---

### Step 5 — Apply Positions

Use `AutoLayout` for full-graph rearrangement (it handles all of the above internally).

For manual or partial layouts, call `MoveNode` (single) or `BatchMoveNodes` (multiple) with the computed coordinates.

After moving, call `ListNodes` once to confirm actual positions match expectations (the tool may have auto-offset some nodes to avoid overlap).

---

### Crossing Minimization — Key Rules

| Rule | Rationale |
|---|---|
| Route edges through their natural layer sequence | Skipping layers ("long edges") increases crossing risk |
| Nodes with many connections should be centered in their layer | Reduces fan-out crossing |
| When two nodes in the same layer both connect to the same downstream node, place them adjacent | Keeps the shared edge bundle compact |
| Avoid placing unrelated nodes between two directly connected nodes in the same layer | Unnecessary interleaving increases crossings |

---

### Responding to "Change Quadrant" Requests

Quadrant identity is determined purely by the **sign of X and Y coordinates**:

| Quadrant | X sign | Y sign | Canvas meaning |
|---|---|---|---|
| **Q4 (default)** | + | + | X > 0, Y > 0 — standard visible canvas space |
| **Q1** | + | − | X > 0, Y < 0 — above the canvas origin (negative Y) |
| **Q2** | − | − | X < 0, Y < 0 — upper-left of origin (both negative) |
| **Q3** | − | + | X < 0, Y > 0 — left of canvas origin (negative X) |

> The canvas Y-axis points **downward**, so "mathematically upward" means **negative Y** in canvas coordinates.

When the user asks to move the layout to a different quadrant:

1. Compute the **bounding box** of all affected nodes: `(minX, minY, maxX, maxY)`. Let `w = maxX - minX`, `h = maxY - minY`.
2. Choose a **target top-left anchor** that places the bounding box firmly in the requested quadrant:
   - **Q4**: target = `(20, 20)` — both positive, no sign change needed
   - **Q1**: target = `(20, -(h + 20))` — X positive, Y negative (box sits above origin)
   - **Q3**: target = `(-(w + 20), 20)` — X negative, Y positive (box sits left of origin)
   - **Q2**: target = `(-(w + 20), -(h + 20))` — both negative
3. Compute `deltaX = targetX - minX`, `deltaY = targetY - minY`.
4. Apply the delta to all affected nodes via `BatchMoveNodes`.
