### SlotEnumerator 选择器类型约束（1~3 级均适用）

- 提供路由凭证选项时，必须**完全来自**组件 `allowedSelectorTypes` 中的类型。
- 框架内部枚举（`SlotChannel`、`SlotState` 以及 `VeloxDev.WorkflowSystem` 命名空间下的任何类型）是底层管道类型——它们**永远不是**有效的路由凭证，**绝对不能**作为选项出现。
- 若 `allowedSelectorTypes` 只有一个条目，直接使用，无需询问。
