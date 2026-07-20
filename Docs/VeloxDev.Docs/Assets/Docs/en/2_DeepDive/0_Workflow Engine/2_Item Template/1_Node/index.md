# Node View Template

Generate a Node View with the `-v-node` template.

```shell
# Avalonia
dotnet new ava-v-node -n MyNodeView -ns MyApp.Views

# WPF
dotnet new wpf-v-node -n MyNodeView -ns MyApp.Views
```

The generated view binds to `IWorkflowNodeViewModel` and renders the node header, slots, and body.
