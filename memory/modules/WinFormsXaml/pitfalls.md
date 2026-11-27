# WinForms XAML — 坑

## Window 的 DIP 尺寸在句柄创建时才落地

`Easel/Src/Easel/Visuals/Window.cs` 的 `OnHandleCreated` 用 `_dipsApplied` 把 `WidthDips`/`HeightDips` 换算成物理尺寸，只套用一次。窗口显示前写 `Form.Bounds`/`ClientSize` 的启动代码会在句柄创建时被这次套用覆盖；要按目标屏裁剪物理矩形，先读一次 `Handle`，再写 `Bounds`。

`Easel/Examples/XamlGallery/Demo/GalleryCapture.cs` 的 `PlaceOnCaptureScreen` 即此顺序：`_ = form.Handle;` 之后才设 `Bounds`。
