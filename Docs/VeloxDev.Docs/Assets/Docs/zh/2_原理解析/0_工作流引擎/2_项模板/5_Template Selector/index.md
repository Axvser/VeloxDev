# Template Selector 模板

继承 `DataTemplateSelector` 的模板选择器，将 ViewModel 类型映射到对应的 DataTemplate。

## 使用

```shell
dotnet new wpf-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new ava-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new winui-v-selector -n MyTemplateSelector -ns MyApp.Views
dotnet new maui-v-selector -n MyTemplateSelector -ns MyApp.Views
```

## 参数

仅支持公共参数，无特有参数。

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | `TemplateSelector` | 类名/文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |
