# Link 视图模板

绑定到 `IWorkflowLinkViewModel` 的 Link 视图，将连接渲染为贝塞尔曲线或折线。

## 使用

```shell
dotnet new wpf-v-link -n MyLinkView -ns MyApp.Views
dotnet new ava-v-link -n MyLinkView -ns MyApp.Views
dotnet new winui-v-link -n MyLinkView -ns MyApp.Views
dotnet new maui-v-link -n MyLinkView -ns MyApp.Views
```

## 参数

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | `LinkView` | 类名/文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |
| | `--linkColor` | `#DDFFFFFF` | 连接线颜色 |
| | `--linkThickness` | `2` | 连接线粗细 |
