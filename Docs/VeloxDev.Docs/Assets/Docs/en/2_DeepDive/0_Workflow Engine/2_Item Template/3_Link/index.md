# Link View Template

Generate a Link View with the `-v-link` template.

```shell
# Avalonia
dotnet new ava-v-link -n MyLinkView -ns MyApp.Views

# WPF
dotnet new wpf-v-link -n MyLinkView -ns MyApp.Views
```

The generated view binds to `IWorkflowLinkViewModel` and renders the connection as a Bezier or polyline curve.
