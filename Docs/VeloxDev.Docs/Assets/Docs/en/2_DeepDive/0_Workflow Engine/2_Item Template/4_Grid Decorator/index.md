# Grid Decorator View Template

Generate a Grid Decorator with the `-v-decorator` template.

```shell
# WPF
dotnet new wpf-v-decorator -n MyGridDecorator -ns MyApp.Views

# Avalonia
dotnet new ava-v-decorator -n MyGridDecorator -ns MyApp.Views
```

The generated decorator implements `IWorkflowGridDecorator` and renders a snap-to-grid background.
