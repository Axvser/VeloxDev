# Workflow Engine

The workflow engine is a graph-topology-based compilation and execution system.

## Architecture Overview

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐
│  Workflow   │────▶│  Compiler    │────▶│  Executor    │
│  Tree       │     │  (BFS/DFS)   │     │  (Plan)      │
└─────────────┘     └──────────────┘     └──────────────┘
```

The compiler traverses the node graph starting from the Controller, following Slot connections, and produces an ordered execution plan (`CompilationResult`). The executor runs the plan sequentially, passing the result chain automatically.
