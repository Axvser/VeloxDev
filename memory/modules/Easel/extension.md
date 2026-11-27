# Easel 示例 — 扩展路径

## 新增一张 Gallery 卡片

1. 选组：组名必须在 `Easel/Examples/XamlGallery/Demo/Catalog/GalleryTaxonomy.cs` 的 `Order` 里；新组或新分区先改这张表，再改 `CardCatalog.Create` 的工厂顺序。
2. 在 `Easel/Examples/XamlGallery/Demo/Catalog/Cards/` 的对应工厂里加 `Card(id, title, summary, usage, factory)`；视图放 `Easel/Examples/XamlGallery/Demo/Views/Cards/`（应用页）或 `Easel/Examples/XamlGallery/Demo/Views/Industrial/`（工业页）。
3. 视图根用 `ThemedDockPanel` / `ThemedGrid` / `ThemedBorder`，构造时先设 DataContext 再 `InitializeComponent`，绑定才能在首帧解析。
4. 实时页面由 view 持有 WinForms `Timer`，`Dispose` 里停表；图表几何用 `PointF[]` 或 SVG 字符串绑定，不要在 `OnRender` 里另存状态。

## 新增一个工业控件

1. 自绘：继承 `VisualElement`，DP 用 `DependencyProperty.Register(..., new PropertyMetadata(...) { AffectsRender = true })`；`OnRender(Graphics)` 只画自己，值变了只失效自身。
2. 复合：`XAML UserControl` + code-behind；DP 要能在 XAML 里赋值或被绑定。
3. 同步四处注册：`Easel/Examples/IndustrialWorkbench/Demo/Controls/<Name>.cs`、`Easel/Examples/IndustrialWorkbench/Demo/Views/Controls/<Name>View.xaml(.cs)`、`Easel/Examples/IndustrialWorkbench/Demo/ViewModels/ControlCatalog.cs` 的条目、`Easel/Examples/IndustrialWorkbench/Demo/Views/OverviewView.xaml` 的控件墙（若首页要展示）。
4. 可交互控件把值写回 ViewModel（`Mode=TwoWay` 或 `Toggled` / `Acknowledged` 事件）；写回后仿真才能看到用户输入。

## 新增一个可拆出/回坞的面板

1. 页面持有槽位（`Border`/`Grid`）与面板根；拖出时 `DockSlot.Child = null`，再设 `Window.Content = 面板`（`Window` 是 `VisualForm` 的 XAML 窗口基类）。
2. 拖动超过阈值时立即建窗：先按 `headerLocal` 把窗口放到指针下，调用 `source.CancelPointerGesture()` 取消旧宿主捕获，再 `window.Surface.AdoptPointerGesture(screenPoint, MouseButtons.Left)`；新宿主补一次按下并继续接收移动/抬起。
3. 回坞时先 `panel.ParentElement?.RemoveChild(panel)`（窗口的 `VisualBootRoot.Wrap` 包装），再 `window.Content = null`，最后 `DockSlot.Child = panel`。回坞发生在浮动窗口自己的 `PointerReleased` 里：先 `Hide()`，用 `BeginInvoke` 推迟 `Close/Dispose`，不在该宿主的事件里销毁宿主。
4. 新 `Window` 的 `DataContext` 在 `Content` 之前设成页面 VM；`Owner` 指向宿主窗口；卡片 `Dispose` 时关闭窗口。

## 原生控件场景

1. 老 WinForms 控件不改写：XAML 用 `<native:…>`（布局属性落在 `NativeElement` 包装上），代码创建用 `NativeControlHost.Child`。
2. 新 Easel 视觉内容放原生容器的 `Content`（如 `native:GroupBox` 里的 StackPanel/ProgressBar）；原生事件只写数据源/DataContext，视觉绑定照常刷新。
3. `IsSnapshot` 只用于确实需要 opacity/transform/clip 的原生岛，不作为 Gallery 演示；Gallery 的 Native 组保持 5 个真实场景。`ScrollViewer` 会自动重摆活体岛；自定义平移/相机宿主在移动内容后调用 `Surface.RefreshNativePlacements()`。

## 自定义 VirtualizingPanel

1. 继承 `VirtualizingPanel`，override `RealizeItem` / `RecycleItem` / `BindRecycledItem`；固定 `ItemExtent`，外层用像素滚动 `ScrollViewer` 或 `CanContentScroll` 逻辑滚动。
2. `InvalidateRealizedWindow` 在置 `FirstRealizedIndex = -1` 前先把当前窗口按真实索引退池；`CanContentScroll` 分支的 `_own*Offset` 夹到 `TotalExtent - viewport`。两处不变量见 `pitfalls.md`。
3. 自定义子类可把选中状态放在行模型上，回收重绑后立即恢复；指标读取放在 `ArrangeOverride` 后，避免 `ScrollChanged` 早于 Arrange 的滞后。

## 官方做法 vs 看着能编译的捷径

- 卡片分组与顺序只改 `GalleryTaxonomy` / `CardCatalog`，不在 shell 里排序。
- 新组的卡片没登记 Taxonomy 不会报错，只会静默落到 Easel native 末尾 —— 新增组时最容易漏的一处。
- Gallery 的工业页是整页场景示例；可复用控件放 `Easel/Examples/IndustrialWorkbench/Demo/Controls`，两边不要互相引用。
- 示例的 Debug 构建引用 `Easel/Src/Easel` 与生成器源码；构建放在差分副本里，主工作树只做合并。
