# Node 视图模板

绑定到 `IWorkflowNodeViewModel` 的 Node 视图，包含节点头部、插槽区和主体区域。

## 使用

```shell
dotnet new wpf-v-node -n MyNodeView -ns MyApp.Views
dotnet new ava-v-node -n MyNodeView -ns MyApp.Views
dotnet new winui-v-node -n MyNodeView -ns MyApp.Views
dotnet new maui-v-node -n MyNodeView -ns MyApp.Views
```

## 参数

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | `NodeView` | 类名/文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |
| | `--nodeBackground` | `#DDFFFFFF` | 节点背景色 |
| | `--nodeForeground` | `#DD1E1E1E` | 节点前景色 |
| | `--nodeBorderBrush` | `#331E1E1E` | 节点边框色 |
| | `--nodeBorderThickness` | `1` | 边框粗细 |
| | `--nodeCornerRadius` | `6` | 圆角半径 |
