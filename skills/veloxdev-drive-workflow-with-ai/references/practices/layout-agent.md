# Auto-layout for the Agent

The layout solution belongs to the **tree**, not to the Agent and not to a demo. A tree helper that implements `IWorkflowLayoutHost` exposes `LayoutSolver`; register one solution there and every entry point uses it — a host button, the node's context menu, and the Agent's `GetLayoutInfo` / `PreviewLayout` / `ArrangeLayout`.

The full contract (pipeline, options, planner/stage/cost, DryRun and the one-undo rule) is in [the workflow skill's layout reference](../../../veloxdev-create-workflow/references/practices/layout.md).

## The three tools

`GetLayoutInfo`, `PreviewLayout` and `ArrangeLayout` are catalogued in [tools.md](tools.md), with their shared parameters and result shape. What matters here is where they get the solution they run: the tree.

⚙ **The default pipeline treats every link as a hierarchy edge.** `WorkflowLayoutProblem` stores node rectangles and directed edges; it has no record of which slot a link came from, so every link is a parent→child edge to `HierarchyAlignmentStage`. A graph that mixes semantics — a sequence/timeline chain beside ownership fan-out — interleaves the sequence successor into its predecessor's fan-out group.

⚙ **Omit `nodeIndex` for a pure hierarchy.** It is optional because the solver accepts the whole tree; the demos pass a start node for every menu entry. A mixed-semantics graph uses a scoped run, or a planner that classifies the edges before the default stages.

⚙ **`GetLayoutInfo` reports `custom: true`** when the registered solver differs from `DefaultWorkflowLayoutSolver`, so the Agent can tell a host's solution from the fallback.

⚙ **`PreviewLayout` writes no anchors.** It is the safe way to answer "what would happen"; `ArrangeLayout` is the one undoable application.
