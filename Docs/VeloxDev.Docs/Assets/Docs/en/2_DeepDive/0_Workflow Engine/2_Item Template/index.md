# Item Template

Each platform provides 7 `dotnet new` item templates for generating workflow component views, customizable via template parameters.

---

## Install Template Packages

```shell
dotnet new install VeloxDev.WPF.Templates       # WPF
dotnet new install VeloxDev.Avalonia.Templates   # Avalonia
dotnet new install VeloxDev.WinUI.Templates      # WinUI
dotnet new install VeloxDev.MAUI.Templates       # MAUI
```

## Short Names by Platform

| Type | WPF | Avalonia | WinUI | MAUI |
|------|-----|----------|-------|------|
| Tree View | `wpf-v-tree` | `ava-v-tree` | `winui-v-tree` | `maui-v-tree` |
| Node View | `wpf-v-node` | `ava-v-node` | `winui-v-node` | `maui-v-node` |
| Slot View | `wpf-v-slot` | `ava-v-slot` | `winui-v-slot` | `maui-v-slot` |
| Link View | `wpf-v-link` | `ava-v-link` | `winui-v-link` | `maui-v-link` |
| Grid Decorator | `wpf-v-decorator` | `ava-v-decorator` | `winui-v-decorator` | `maui-v-decorator` |
| Template Selector | `wpf-v-selector` | `ava-v-selector` | `winui-v-selector` | `maui-v-selector` |
| Minimap | `wpf-v-minimap` | `ava-v-minimap` | `winui-v-minimap` | `maui-v-minimap` |

## Common Parameters

All templates share these parameters:

| Short | Parameter | Default | Description |
|-------|-----------|---------|-------------|
| `-n` | `--name` | Varies | Generated class/filename |
| `-o` | `--output` | Current dir | Output directory |
| | `--namespace` | `MyApp.Views` | Namespace |

Component-specific parameters are documented in each sub-page.
| `-n` / `--name` | `LinkView` | Class/filename |
| `-o` / `--output` | Current dir | Output directory |
| `--namespace` | `MyApp.Views` | Namespace |
| `--linkColor` | `#DDFFFFFF` | Line color |
| `--linkThickness` | `2` | Line thickness |

### `wpf-v-decorator` — Grid & Ruler

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-n` / `--name` | `GridDecorator` | Class/filename |
| `-o` / `--output` | Current dir | Output directory |
| `--namespace` | `MyApp.Views` | Namespace |
| `--gridBackground` | `#1E1E1E` | Grid background |
| `--minorGridColor` | `#2A2D2E` | Minor grid line color |
| `--majorGridColor` | `#3A3D40` | Major grid line color |
| `--axisColor` | `#4D4D4D` | Axis color |
| `--gridSpacing` | `40d` | Minor grid spacing |
| `--majorLineEvery` | `5` | Major line interval |
| `--rulerBackground` | `#252526` | Ruler background |
| `--rulerTickColor` | `#555555` | Tick color |
Component-specific parameters are documented in each sub-page.

## Usage Examples

```shell
dotnet new wpf-v-node -n MyNodeView -o Views --nodeBackground "#FF2D2D2D"
dotnet new wpf-v-decorator -n MyGridDecorator
dotnet new wpf-v-link -n MyLinkView --linkColor "#FF4FC3F7"
```
