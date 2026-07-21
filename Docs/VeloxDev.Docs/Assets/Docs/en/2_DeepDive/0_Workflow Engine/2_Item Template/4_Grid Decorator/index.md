# Grid Decorator Template

Implements `IWorkflowGridDecorator`. Renders a customizable grid with ruler markings.

## Usage

```shell
dotnet new wpf-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new ava-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new winui-v-decorator -n MyGridDecorator -ns MyApp.Views
dotnet new maui-v-decorator -n MyGridDecorator -ns MyApp.Views
```

## Parameters

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | `GridDecorator` | Class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |
| | `--gridBackground` | `#1E1E1E` | Grid background |
| | `--minorGridColor` | `#2A2D2E` | Minor grid line color |
| | `--majorGridColor` | `#3A3D40` | Major grid line color |
| | `--axisColor` | `#4D4D4D` | Axis color |
| | `--gridSpacing` | `40d` | Minor grid spacing |
| | `--majorLineEvery` | `5` | Major line interval |
| | `--rulerBackground` | `#252526` | Ruler background |
| | `--rulerTickColor` | `#555555` | Tick color |
| | `--rulerLabelColor` | `#888888` | Label text color |
| | `--rulerDividerColor` | `#3A3D40` | Divider color |
