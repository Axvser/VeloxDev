# Automatic layout

The layout solution belongs to the **tree**, not to the Agent and not to a demo. A tree helper that implements `IWorkflowLayoutHost` exposes `LayoutSolver`; register one solution there and every entry point uses it — a host button, the node's context menu, and the Agent's `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout` tools.

```csharp
// The tree helper is the owner.
var helper = (IWorkflowLayoutHost)tree.GetHelper();

// Replace the planner and/or the cost; the default solver still snapshots, solves and applies.
helper.LayoutSolver = new DefaultWorkflowLayoutSolver(
    planner: new MyPlanner(),
    cost: new DefaultWorkflowLayoutCost());

// Or replace the whole composition.
helper.LayoutSolver = new MyLayoutSolver();
```

## The entry point

```csharp
using VeloxDev.WorkflowSystem;

// Scope the run to a node's reachable subgraph. node.Arrange sets StartNode for you.
var result = node.Arrange(new WorkflowLayoutOptions
{
    Traversal = WorkflowLayoutTraversal.Downstream, // Upstream / Both
    PinStartNode = true,
    ColumnGap = 64,
    RowGap = 32,
    PositionTolerance = 8,
});

// Dry run: solve and report, write no anchors.
var preview = node.Arrange(new WorkflowLayoutOptions { DryRun = true });
```

`node.Arrange(options)` asks the node's tree helper for its registered solver. `tree.Arrange(options)` asks the tree's helper. Both return a `WorkflowLayoutResult` describing placements, cost, bounds and diagnostics.

## The pipeline

The default solver is a fixed composition:

1. **Snapshot.** `WorkflowLayoutProblemBuilder.Build(tree, options)` reads the live tree once and produces an immutable `WorkflowLayoutProblem`: node rectangles (world coordinates), directed edges, pinned flags, skipped nodes and the original node references.
2. **Plan.** An `IWorkflowLayoutPlanner` chooses the stages for this problem.
3. **Solve.** `WorkflowLayoutPipeline` runs the stages against a mutable `WorkflowLayoutState` (`X` / `Y` arrays plus the reserved `Rank` / `Order` / `RouteX` / `RouteY` arrays), then copies pinned positions back.
4. **Measure.** An `IWorkflowLayoutCost` scores the solved arrays.
5. **Apply.** `WorkflowLayoutApplier` writes the changed anchors through `StandardSetAnchor` as one `WorkflowActionPair`. One Undo restores every original position.

`DryRun` stops after measurement: no anchor changes. `PreviewLayout` sets it; `ArrangeLayout` leaves it off.

## The problem and the state

`WorkflowLayoutProblem` is the only object a stage may read:

- `Nodes` — `WorkflowLayoutNode`: index, world rectangle, layer and `IsPinned`.
- `Edges` — `WorkflowLayoutEdge`: sender index → receiver index.
- `Sources` — the original `IWorkflowNodeViewModel` for each participating index.
- `SkippedIndices` — original indices omitted because their geometry was `NaN` or infinite.

The options are carried by `WorkflowLayoutState.Options`, not by the problem.

A stage reads the immutable problem and options and writes only its own arrays in `WorkflowLayoutState`. A stage never reads the model, the view, or `Problem.Sources`, and never writes a pinned node; the pipeline copies pinned positions back after the stages run.

The problem stores **directed edges without slot identity**. Every link is a parent→child edge. The default planner assumes the graph has one edge meaning (a workflow's output→input). For a graph that mixes semantics — a sequence chain beside an ownership fan-out — scope the run with `StartNode` / `Traversal`, or supply a planner that classifies the edges before the default stages. Reserve a whole-tree run for a pure hierarchy.

## The options

| Option | Meaning |
|---|---|
| `StartNode` | Optional start node; the run is scoped to its reachable subgraph. Nodes outside the reachable set stay in the problem as pinned obstacles. |
| `Traversal` | `Downstream`, `Upstream` or `Both` — the direction used to build the reachable set. |
| `MaxDepth` | Maximum traversal depth; `0` means unlimited. |
| `PinStartNode` | Pins the start node at its current position (default `true`). |
| `Pinned` | A predicate evaluated once per node; `true` pins that node. |
| `ColumnGap` / `RowGap` | Spacing between columns and rows. |
| `PositionTolerance` | A node within this distance of its solved position is treated as unmoved. `0` asks for strict canonical placement. |
| `MaxSweeps` | Maximum separation sweeps. |
| `TileSize` | Tile size for the separation index. |
| `ParallelThreshold` | Node count above which the pipeline may use the parallel separation path. |
| `MeasureCost` | Whether to compute the cost. |
| `DryRun` | Solve and report without applying anchors. |

## Extension points

### A planner

A planner picks the stages for one problem:

```csharp
sealed class MyPlanner : IWorkflowLayoutPlanner
{
    public IReadOnlyList<IWorkflowLayoutStage> CreateStages(
        WorkflowLayoutProblem problem, WorkflowLayoutOptions options)
        => [new MyLayeringStage(), new HierarchyAlignmentStage(), new TileSeparationStage()];
}
```

### A stage

```csharp
sealed class MyLayeringStage : IWorkflowLayoutStage
{
    public string Name => "my-layering";

    public void Execute(WorkflowLayoutState state, WorkflowLayoutContext context)
    {
        // Read state.Problem and state.Options. Write state.X / state.Y
        // (and the reserved arrays if you need them).
    }
}
```

The shipped stages are `HierarchyAlignmentStage` (ranks nodes by hierarchy and aligns them) and `TileSeparationStage` (separates overlapping tiles).

### A cost

An `IWorkflowLayoutCost` measures the immutable problem and the solved arrays. It does not read the live model, the view, or a curve the view published before the solve.

### A complete solver

A full `IWorkflowLayoutSolver` owns the whole composition: snapshot → solve → apply. Honour `WorkflowLayoutOptions.DryRun` and keep the one-undo contract — write all moves as one `WorkflowActionPair`.

## Host and Agent

The host-facing call is `node.Arrange(...)` / `tree.Arrange(...)`. The Agent-facing tools are `GetLayoutInfo` (the registered solver and whether it is custom), `PreviewLayout` (a dry run) and `ArrangeLayout` (one undoable application). The Agent tools accept an optional `nodeIndex`; a scoped run is the normal shape for a mixed-semantics graph. See [the AI skill's layout notes](../../../veloxdev-drive-workflow-with-ai/references/practices/layout-agent.md).
