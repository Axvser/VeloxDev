# Link 交互

连接线支持以下交互：

- **删除**：选中后按 Delete 键或调用 `DeleteCommand()`
- **自动路由**：连接的节点移动时，连接路径（贝塞尔/折线）自动调整
- **可见性**：由 `IWorkflowLinkViewModel.IsVisible` 属性控制

各平台视图层根据视图模板将连接渲染为贝塞尔曲线或折线。
