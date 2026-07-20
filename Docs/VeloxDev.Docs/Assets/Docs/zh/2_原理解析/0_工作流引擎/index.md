# 工作流引擎

基于图拓扑的编译执行引擎。

```
Workflow Tree → Compiler (BFS/DFS) → Execution Plan → Executor
```

编译器沿着 Slot 连接遍历节点图，生成有序执行计划，执行器按顺序运行。
