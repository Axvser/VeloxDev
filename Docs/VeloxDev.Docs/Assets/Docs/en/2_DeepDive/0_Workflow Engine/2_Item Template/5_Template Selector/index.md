# Template Selector View Template

Generate a Template Selector with the `-v-selector` template.

```shell
# WPF
dotnet new wpf-v-selector -n MyTemplateSelector -ns MyApp.Views

# Avalonia
dotnet new ava-v-selector -n MyTemplateSelector -ns MyApp.Views
```

The generated selector extends `DataTemplateSelector` and maps ViewModel types (`IWorkflowNodeViewModel`, `IWorkflowSlotViewModel`, etc.) to their corresponding DataTemplates.
