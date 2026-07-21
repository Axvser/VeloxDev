# Slot 视图模板

绑定到 `IWorkflowSlotViewModel` 的 Slot 视图，包含连接点及拖拽目标区域。

## 使用

```shell
dotnet new wpf-v-slot -n MySlotView -ns MyApp.Views
dotnet new ava-v-slot -n MySlotView -ns MyApp.Views
dotnet new winui-v-slot -n MySlotView -ns MyApp.Views
dotnet new maui-v-slot -n MySlotView -ns MyApp.Views
```

## 参数

| 短名 | 参数 | 默认值 | 说明 |
|------|------|--------|------|
| `-n` | `--name` | `SlotView` | 类名/文件名 |
| `-o` | `--output` | 当前目录 | 输出目录 |
| | `--namespace` | `MyApp.Views` | 命名空间 |
| | `--slotBackground` | `#01000000` | 命中测试背景色 |
| | `--slotColor` | `#DD1E1E1E` | 待机颜色 |
| | `--slotBorderColor` | `#FFFFFFFF` | 边框色 |
| | `--slotPath` | SVG 地球图标 | 图标路径数据 |
