# Slot View Template

Binds to `IWorkflowSlotViewModel`. Includes connection point and drag target area.

## Usage

```shell
dotnet new wpf-v-slot -n MySlotView -ns MyApp.Views
dotnet new ava-v-slot -n MySlotView -ns MyApp.Views
dotnet new winui-v-slot -n MySlotView -ns MyApp.Views
dotnet new maui-v-slot -n MySlotView -ns MyApp.Views
```

## Parameters

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | `SlotView` | Class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |
| | `--slotBackground` | `#01000000` | Hit-test background |
| | `--slotColor` | `#DD1E1E1E` | Standby color |
| | `--slotBorderColor` | `#FFFFFFFF` | Border color |
| | `--slotPath` | SVG globe icon | Icon path data |
