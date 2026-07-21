# Link View Template

Binds to `IWorkflowLinkViewModel`. Renders connections as Bezier curves or polylines.

## Usage

```shell
dotnet new wpf-v-link -n MyLinkView -ns MyApp.Views
dotnet new ava-v-link -n MyLinkView -ns MyApp.Views
dotnet new winui-v-link -n MyLinkView -ns MyApp.Views
dotnet new maui-v-link -n MyLinkView -ns MyApp.Views
```

## Parameters

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | `LinkView` | Class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |
| | `--linkColor` | `#DDFFFFFF` | Line color |
| | `--linkThickness` | `2` | Line thickness |
