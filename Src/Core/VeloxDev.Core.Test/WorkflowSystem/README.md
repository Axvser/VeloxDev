# WorkflowSystem 测试

针对 `VeloxDev.WorkflowSystem` 命名空间的单元测试 —— 空间原语、画布布局以及工作流引擎使用的空间哈希表。

| 测试文件 | 测试目标 | 关键场景 |
|---|---|---|
| `AnchorTests.cs` | `Anchor` | 构造、相等性、`+`/`-` 运算符、`Clone`、`ToString` |
| `SizeTests.cs` | `Size` | 构造、相等性、`+`/`-` 运算符、`Clone`、`ToString` |
| `OffsetTests.cs` | `Offset` | 构造、相等性、`+`/`-` 运算符、`Clone`、`ToString` |
| `CellKeyTests.cs` | `CellKey` | 构造、`Equals`（强类型 + 装箱）、`GetHashCode`、`ToString` |
| `ViewportTests.cs` | `Viewport` | `Right`/`Bottom`、`IsEmpty`、`IntersectsWith`、`Contains`（点/区域）、相等性 |
| `WorkflowActionPairTests.cs` | `WorkflowActionPair` | Redo/Undo 调用 |
| `CanvasLayoutTests.cs` | `CanvasLayout` | 默认值、ActualSize/ActualOffset 计算、Equals/GetHashCode、Clone 独立性、属性变更触发 Update |
| `SpatialGridHashMapTests.cs`
