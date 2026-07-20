# Template Selector 模板

生成继承 `DataTemplateSelector` 的模板选择器。

```shell
# WPF
dotnet new wpf-v-selector -n MyTemplateSelector -ns MyApp.Views

# Avalonia
dotnet new ava-v-selector -n MyTemplateSelector -ns MyApp.Views
```

生成的模板选择器将 ViewModel 类型（`IWorkflowNodeViewModel`、`IWorkflowSlotViewModel` 等）映射到对应的 DataTemplate。
