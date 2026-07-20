# 项模板

通过 `dotnet new` 项模板快速生成各平台的组件视图。

## 安装模板包

```shell
dotnet new install VeloxDev.WPF.Templates       # WPF 模板
dotnet new install VeloxDev.Avalonia.Templates   # Avalonia 模板
dotnet new install VeloxDev.WinUI.Templates      # WinUI 模板
dotnet new install VeloxDev.MAUI.Templates       # MAUI 模板
```

## 生成视图

```shell
dotnet new {shortName} -n MyView -ns MyApp.Views
```

## 各平台模板短名

| 类型 | Avalonia | WPF | WinUI | MAUI |
|------|----------|-----|-------|------|
| **Tree 视图** | `ava-v-tree` | `wpf-v-tree` | `winui-v-tree` | `maui-v-tree` |
| **Node 视图** | `ava-v-node` | `wpf-v-node` | `winui-v-node` | `maui-v-node` |
| **Slot 视图** | `ava-v-slot` | `wpf-v-slot` | `winui-v-slot` | `maui-v-slot` |
| **Link 视图** | `ava-v-link` | `wpf-v-link` | `winui-v-link` | `maui-v-link` |
| **网格装饰器** | `ava-v-decorator` | `wpf-v-decorator` | `winui-v-decorator` | `maui-v-decorator` |
| **模板选择器** | `ava-v-selector` | `wpf-v-selector` | `winui-v-selector` | `maui-v-selector` |
| **小地图覆盖** | `ava-v-minimap` | `wpf-v-minimap` | `winui-v-minimap` | `maui-v-minimap` |
