# Item Template

The template system generates platform-specific Views for each component type via `dotnet new` item templates.

## Install Template Packages

```shell
dotnet new install VeloxDev.WPF.Templates       # WPF templates
dotnet new install VeloxDev.Avalonia.Templates   # Avalonia templates
dotnet new install VeloxDev.WinUI.Templates      # WinUI templates
dotnet new install VeloxDev.MAUI.Templates       # MAUI templates
```

## Generate a View

```shell
dotnet new {shortName} -n MyView -ns MyApp.Views
```

## Short Names by Platform

| Type | Avalonia | WPF | WinUI | MAUI |
|------|----------|-----|-------|------|
| **Tree View** | `ava-v-tree` | `wpf-v-tree` | `winui-v-tree` | `maui-v-tree` |
| **Node View** | `ava-v-node` | `wpf-v-node` | `winui-v-node` | `maui-v-node` |
| **Slot View** | `ava-v-slot` | `wpf-v-slot` | `winui-v-slot` | `maui-v-slot` |
| **Link View** | `ava-v-link` | `wpf-v-link` | `winui-v-link` | `maui-v-link` |
| **Grid Decorator** | `ava-v-decorator` | `wpf-v-decorator` | `winui-v-decorator` | `maui-v-decorator` |
| **Template Selector** | `ava-v-selector` | `wpf-v-selector` | `winui-v-selector` | `maui-v-selector` |
| **Minimap Overlay** | `ava-v-minimap` | `wpf-v-minimap` | `winui-v-minimap` | `maui-v-minimap` |
