# Tree View Template

Generate a Tree View with the `-v-tree` template.

```shell
# Avalonia
dotnet new ava-v-tree -n WorkflowTreeView -ns MyApp.Views

# WPF
dotnet new wpf-v-tree -n WorkflowTreeView -ns MyApp.Views
```

The generated view binds to `IWorkflowTreeViewModel` and includes a scrollable canvas surface.
