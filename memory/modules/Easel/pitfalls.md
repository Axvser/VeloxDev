# Easel 示例 — 实测坑

- `ItemsControl` 同时设 `ItemsSource` 和 `ItemsPanelTemplate`（WrapPanel）时，项被 realize 成 0×0（视觉树里是 6 个 0 尺寸的容器）。锚点：`Easel/Examples/XamlGallery/Demo/Views/Industrial/ProductionLineView.xaml` 的 Stations 列表。改回默认竖向 `ItemsControl` 后正常；需要横向排布时用代码构建面板或每行一条。
- `Window.WidthDips` 只在句柄创建且物理尺寸仍是记录值时应用；先设 `MinimumSize` 会改物理尺寸，之后的 `WidthDips` 被跳过。锚点：`Easel/Examples/IndustrialWorkbench/Demo/MainForm.xaml.cs` 的构造函数。顺序：`MinimumSize` 先，`WidthDips` 后。
- 150% DPI 下 Gallery 预览的默认三栏宽度装不下 540 DIP 卡片，截图会裁掉右侧；验证整页用 `--preview-only`，不要据普通截图判断控件尺寸。
