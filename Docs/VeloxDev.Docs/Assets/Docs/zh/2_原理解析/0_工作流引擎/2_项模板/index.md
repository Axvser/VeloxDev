# 项模板

每个平台提供 7 个 `dotnet new` 项模板，一键生成工作流组件视图。通过自定义参数修改外观和命名。

---

## 安装

```shell
dotnet new install VeloxDev.WPF.Templates       # WPF
dotnet new install VeloxDev.Avalonia.Templates   # Avalonia
dotnet new install VeloxDev.WinUI.Templates      # WinUI
dotnet new install VeloxDev.MAUI.Templates       # MAUI
```

## 平台短名对照

| 类型 | WPF | Avalonia | WinUI | MAUI |
|------|-----|----------|-------|------|
| Tree | `wpf-v-tree` | `ava-v-tree` | `winui-v-tree` | `maui-v-tree` |
| Node | `wpf-v-node` | `ava-v-node` | `winui-v-node` | `maui-v-node` |
| Slot | `wpf-v-slot` | `ava-v-slot` | `winui-v-slot` | `maui-v-slot` |
| Link | `wpf-v-link` | `ava-v-link` | `winui-v-link` | `maui-v-link` |
| Grid Decorator | `wpf-v-decorator` | `ava-v-decorator` | `winui-v-decorator` | `maui-v-decorator` |
| Template Selector | `wpf-v-selector` | `ava-v-selector` | `winui-v-selector` | `maui-v-selector` |
| Minimap | `wpf-v-minimap` | `ava-v-minimap` | `winui-v-minimap` | `maui-v-minimap` |

## 公共参数

所有模板共享以下参数：

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | 各模板不同 | 生成的类名和文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |

各组件特有参数见以下子页面。

## 使用示例

```shell
# 节点视图，自定义颜色
dotnet new wpf-v-node -n MyNodeView -o Views `
    --nodeBackground "#FF2D2D2D" --nodeForeground "#FFFFFFFF"

# 网格装饰器
dotnet new wpf-v-decorator -n MyGridDecorator

# 连接视图
dotnet new wpf-v-link -n MyLinkView --linkColor "#FF4FC3F7"
```
