# Grid Decorator 模板

实现 `IWorkflowGridDecorator` 的网格背景装饰器，包含可自定义颜色的网格标尺线。

## 使用

```shell
dotnet new wpf-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new ava-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new winui-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new maui-v-decorator -n MyGridDecorator -ns MyApp.Views
```

## 参数

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | `GridDecorator` | 类名/文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |
| | `--gridBackground` | `#1E1E1E` | 网格背景色 |
| | `--minorGridColor` | `#2A2D2E` | 次网格线颜色 |
| | `--majorGridColor` | `#3A3D40` | 主网格线颜色 |
| | `--axisColor` | `#4D4D4D` | 坐标轴颜色 |
| | `--gridSpacing` | `40d` | 次网格间距 |
| | `--majorLineEvery` | `5` | 主网格线间隔 |
| | `--rulerBackground` | `#252526` | 标尺背景色 |
| | `--rulerTickColor` | `#555555` | 标尺刻度色 |
| | `--rulerLabelColor` | `#888888` | 标尺标签色 |
| | `--rulerDividerColor` | `#3A3D40` | 分割线色 |
