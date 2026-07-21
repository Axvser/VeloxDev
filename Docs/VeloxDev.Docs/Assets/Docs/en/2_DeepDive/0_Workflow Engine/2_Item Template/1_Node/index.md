# Node View Template

Binds to `IWorkflowNodeViewModel`. Includes node header, slot area, and body.

## Usage

```shell
dotnet new wpf-v-node -n MyNodeView -ns MyApp.Views
dotnet new ava-v-node -n MyNodeView -ns MyApp.Views
dotnet new winui-v-node -n MyNodeView -ns MyApp.Views
dotnet new maui-v-node -n MyNodeView -ns MyApp.Views
```

## Parameters

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | `NodeView` | Class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |
| | `--nodeBackground` | `#DDFFFFFF` | Background color |
| | `--nodeForeground` | `#DD1E1E1E` | Foreground color |
| | `--nodeBorderBrush` | `#331E1E1E` | Border color |
| | `--nodeBorderThickness` | `1` | Border thickness |
| | `--nodeCornerRadius` | `6` | Corner radius |
