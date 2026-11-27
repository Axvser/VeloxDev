# WinForms XAML — 坑

## Window 的 DIP 尺寸在句柄创建时才落地

`Easel/Src/Easel/Visuals/Window.cs` 的 `OnHandleCreated` 用 `_dipsApplied` 把 `WidthDips`/`HeightDips` 换算成物理尺寸，只套用一次。窗口显示前写 `Form.Bounds`/`ClientSize` 的启动代码会在句柄创建时被这次套用覆盖；要按目标屏裁剪物理矩形，先读一次 `Handle`，再写 `Bounds`。

`Easel/Examples/XamlGallery/Demo/GalleryCapture.cs` 的 `PlaceOnCaptureScreen` 即此顺序：`_ = form.Handle;` 之后才设 `Bounds`。

## 原生岛的底色与像素边界

- 透明 `BackColor` 的控件托管期间会被换成实色（最近祖先/宿主/主题）；`WS_CLIPCHILDREN`/`WS_EX_COMPOSITED` 下父窗口补画不可靠，探针实测 Label 整块变成系统控件色。要它透出某个容器，就把岛放进那个容器、别放在旁边。
- 快照位图的底色取同一个解析色；把岛放在绿色 `Border` 外、指望它透出绿色，只会得到画布色。
- 逻辑矩形的物理摆放是 `floor(起点)`/`ceil(远边)`；先 `Rectangle.Round` 再缩放会在 1.5 倍屏上缩进 1px。
- 快照输入代理也要挖弹层的洞（`VisualHost.RefreshNativeRegions` 会刷新注册过的代理）；缺它时快照岛上的弹层点不到。

依据：`NativeChildrenTests` / `NativeEmbeddingTests`。
