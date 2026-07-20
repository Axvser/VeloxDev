# Slot View Template

Generate a Slot View with the `-v-slot` template.

```shell
# Avalonia
dotnet new ava-v-slot -n MySlotView -ns MyApp.Views

# WPF
dotnet new wpf-v-slot -n MySlotView -ns MyApp.Views
```

The generated view binds to `IWorkflowSlotViewModel` and renders the connection point with drag targets.
