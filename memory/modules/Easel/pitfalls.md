# Easel 示例 — 实测坑

- `ItemsControl` 同时设 `ItemsSource` 和 `ItemsPanelTemplate`（WrapPanel）时，项被 realize 成 0×0（视觉树里是 6 个 0 尺寸的容器）。锚点：`Easel/Examples/XamlGallery/Demo/Views/Industrial/ProductionLineView.xaml` 的 Stations 列表。改回默认竖向 `ItemsControl` 后正常；需要横向排布时用代码构建面板或每行一条。
- `Window.WidthDips` 只在句柄创建且物理尺寸仍是记录值时应用；先设 `MinimumSize` 会改物理尺寸，之后的 `WidthDips` 被跳过。锚点：`Easel/Examples/IndustrialWorkbench/Demo/MainForm.xaml.cs` 的构造函数。顺序：`MinimumSize` 先，`WidthDips` 后。
- `Window.Content = null` 只清 surface root，不会把面板从 `VisualBootRoot.Wrap` 的 `DockPanel` 里摘掉；回坞前必须 `panel.ParentElement?.RemoveChild(panel)`，直接再次 `Content = panel` 会因 `ParentElement` 非空抛出。锚点：`Easel/Examples/XamlGallery/Demo/Views/Industrial/TearOffView.xaml.cs` 的 `DockBack`。
- 浮动窗口首帧白闪：`Show()` 才创建句柄时 DWM 暗色标题栏来不及在首帧前生效。`TearOffView.DetachAt` 在 Show 前取 `window.Handle`；`NativeWindowTheme` 在 Windows 11 补 `DWMWA_BORDER_COLOR(34)` / `CAPTION_COLOR(35)` / `TEXT_COLOR(36)`；`DockBack` 先 `Hide()` 再摘 content。锚点：`TearOffView.xaml.cs` 的 `DetachAt`/`DockBack`、`Easel/Src/Easel/Visuals/NativeWindowTheme.cs`。
- 原生控件卡片拆卸时，`DataGridView` 移除会触发 `ClearSelection` → `SelectionChanged`，此时 `Native` 包装可能已 null/Disposed；用 `_detached` + `Native.IsDisposed` 守卫，`CurrentRow` 优先读 `SelectedRows[0]`，`Dispose` 里先解绑原生事件。锚点：`Easel/Examples/XamlGallery/Demo/Views/Cards/NativeWorkspaceCard.xaml.cs` 的 `CurrentRow`/`SyncFieldsFromSelection`。
- `native:GroupBox` 的 Content 首次落位早于子 HWND 移动，内容表面会停在默认 `DisplayRectangle`；原生 Layout 后用 `BeginInvoke` 再 `PerformLayout` 一次。锚点：`Easel/Examples/XamlGallery/Demo/Views/Cards/NativeEmbedCard.xaml.cs` 的 Layout 处理。
- 活体 HWND 岛不随预览 `ScrollViewer` 滚动重新落位；原生卡片控制在 393 DIP 内避开滚动，窗口更小时岛与视觉内容会脱节。锚点：`Easel/Examples/XamlGallery/Demo/Catalog/Cards/NativeCards.cs` 的 5 张原生卡；属框架限制。
- `Border.Child` 有独立的 `_child` 字段；用继承的 `RemoveChild` 摘除后 `Border.Child` 仍指向旧面板，改用 `Child = null`。锚点：`Easel/Src/Easel/Visuals/Controls/CoreControls.cs` 的 `Border.Child`。
- `Border` 构造时 `IsHitTestVisible = false`；拿 `Border` 当标题栏/拖动把手时要显式 `IsHitTestVisible="True"` 且设置 `Background`；空白区没有命中面时只剩子按钮能点。锚点：`Easel/Src/Easel/Visuals/Controls/CoreControls.cs` 的 `Border` 构造函数、`Easel/Examples/XamlGallery/Demo/Views/Industrial/TearOffView.xaml` 的 `DragBar`。
- `ProcessPointerDown/Move/Up` 接收物理客户区像素并在内部 `ToLogical`；合成拖拽坐标要乘 `DpiScale`，直接传 DIP 会点偏。锚点：`Easel/Src/Easel/Visuals/VisualHost.cs` 的 `ProcessPointerDown`、`TearOffView.DragOutForCapture`。
- `VisualHost.AdoptPointerGesture` 会在屏幕点命中新宿主并补一次 `PointerPressed`；调用前先把目标元素摆到指针下，直接调用会在别的元素上补按下。锚点：`Easel/Src/Easel/Visuals/VisualHost.cs` 的 `AdoptPointerGesture`、`TearOffView.DetachAt`。
- 不要在宿主自己的 `PointerPressed/Moved/Released` 里 `Dispose` 宿主：回坞发生在浮动窗口的 `PointerReleased` 内，先 `Hide()`，用 `BeginInvoke` 推迟 `Close/Dispose`。锚点：`Easel/Examples/XamlGallery/Demo/Views/Industrial/TearOffView.xaml.cs` 的 `DockBack`。
- `VirtualizingPanel.InvalidateRealizedWindow` 必须先把当前窗口按 `_firstRealizedIndex` 退池，再置 `-1`；只置 `-1` 会让 `RecycleItem` 收到的索引与元素实际显示项不一致。锚点：`Easel/Src/Easel/Visuals/Controls/VirtualizingPanel.cs` 的 `InvalidateRealizedWindow`。
- `VirtualizingPanel` 的 `CanContentScroll` 分支要把 `_ownVerticalOffset` / `_ownHorizontalOffset` 夹到 `TotalExtent - viewport`；数据源缩短后偏移越界会得到 `first == last == count`，窗口全空。锚点：同文件的 `ResolveViewport`。
- `VisualElement.Resources` 的索引器只查自己的字典，不查合并字典；主题键（如 `Raised`）要从 `Theme.Current` 或元素属性取，自定义键（如 `SignalWash`）才放本地。锚点：`Easel/Src/Easel/ResourceDictionary.cs` 的索引器、`TearOffView.UpdateDockHint`。
- 150% DPI 下 Gallery 预览的默认三栏宽度装不下 540 DIP 卡片，截图会裁掉右侧；验证整页用 `--preview-only`，不要据普通截图判断控件尺寸。
