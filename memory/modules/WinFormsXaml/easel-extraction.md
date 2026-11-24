# VeloxDev.WinForms.Xaml ↔ Easel 抽取

> Easel 独立仓：`E:\VisualStudio\Projects\Easel`。抽取前备份为 `Easel-backup-20261124`。

## 现状

- **运行时**：`Src/Easel` 是 VeloxDev `Src/Adapters/VeloxDev.WinForms/Xaml` 的完整副本；命名空间映射 `VeloxDev.WinForms.Xaml` → `Easel`、`...Xaml.Controls` → `Easel.Controls`、`...Xaml.Visuals` → `Easel.Visuals`、`...Visuals.Controls[.Shapes]` → `Easel.Visuals.Controls[.Shapes]`。Easel 运行时**不引用 VeloxDev.Core**；只有 net461 额外引 `System.ValueTuple`。
- **生成器**：`Src/Generators/Easel.Generator` 是 `VeloxDev.WinForms.Generator` 的副本，诊断 ID 为 `EASEL_XAML*` / `EASEL_XAMLP*`，unprefixed 类型按 Easel 命名空间解析。
- **测试/示例**：`Src/Test/Unit/Easel.Test`、`Examples/XamlGallery` 已复制并改名。Easel 示例去掉了 Workflow graph 与 VeloxDev MVVM 卡片及对应依赖；Easel 测试去掉了依赖过渡引擎的动画行为测试（那些留在 VeloxDev 适配器侧）。
- **动画契约**：`Easel/Animation/` 定义 `IEase` / `Eases` / `AnimationSpec` / `IVisualAnimator` / `AnimationRuntime.Animator`。`Easel.Visuals.VisualTransition` 只是门面，不实现引擎；没有注入适配器时 `AnimationRuntime.Animator` 是 `NullVisualAnimator`：`CanAnimate` 返回 false，启动动画（`VisualTransition` / 触发器动作）抛出明确的 `InvalidOperationException`。`TransitionAction` / `KeyFrameAnimation` 仍留在 `Easel.Visuals`，调用门面。
- **VeloxDev 适配器**：`Src/Adapters/VeloxDev.WinForms/XamlAnimation/EaselVisualAnimator.cs` 实现 `IVisualAnimator`，把 Easel DP 包成 `ITransitionProperty`，用 `Easel.BindingPriority.Animation` 写帧、收尾落 `LocalValue`，`EaseBridge` 把 `IEase` 接到 `IEaseCalculator`；模块初始化器在程序集加载时赋值 `AnimationRuntime.Animator`。
- **条件引用**：`VeloxDev.WinForms.csproj` 按配置分支 —— `Debug` 用子模块 `ProjectReference`（`..\..\..\Easel\Src\Easel\Easel.csproj`），其余配置用 `PackageReference Include="WinForms.Easel"`，好让 nuspec 带上真实的 Easel 依赖（`Easel` 这个 ID 在 nuget.org 上是保留的，推不上去）。
- **验证**：Easel 解决方案（运行时/生成器/测试/示例）构建与测试通过；示例 `--smoke`/`--capture` 正常；独立探针加载 VeloxDev.WinForms 后 `AnimationRuntime.Animator` 为 `EaselVisualAnimator`，Easel `Border.Width` 10→100 动画完成。

## Easel 新增能力（VeloxDev 的 Xaml 副本尚未同步）

- **视觉附着属性回调**：`PropertyMetadata.VisualPropertyChangedCallback`（目标 `IWinformsObject`）由 `WinformsObject.RaisePropertyChanged` 调用；`[XamlProperty]` / `[XamlAttachedProperty]` 新增 `ChangedCallback = nameof(...)`，指向声明类型上的静态 `(IWinformsObject, DependencyPropertyChangedEventArgs)` 方法，生成器发 `VisualPropertyChangedCallback = ...`，签名不对报 `EASEL_XAMLP006`。此前只有原生 `Control` 目标的 `PropertyChangedCallback` 会触发，视觉元素（`VisualElement` 派生自 `WinformsObject`，不是 `Control`）写附着属性完全无通知——WinForms 适配器因此只能用每 pass 的 `Ensure` 遍历安装行为。语义与 Control 路一致：有效值变化才通知，首写与 `ClearValue` 回落都通知。回归在 `DependencyPropertyTests` / `SourceGeneratorTests`。
- **3D 变换**：`Transform3D` 家族（`Rotate3DTransform` / `Scale3DTransform` / `Translate3DTransform` / `MatrixTransform3D` / `Transform3DGroup`）挂在 `Visual.Transform3D` 与 `VisualElement.Transform3DProperty` 上；`Transform3D.cs` 自带 `Matrix4x4` 与单应性 `Projection3D`，不依赖 `System.Numerics`。渲染先合成子树，再用逆单应性逐像素双线性采样贴回（边缘 2×2 覆盖），不做 N×N 仿射近似；单轴 >4096px 或目标 >16M 像素时退回分格仿射。命中、`AbsoluteBounds` 与失效走同一逆映射。`Depth<=0` 视为正交。`CacheAsBitmap` 缓存投影前的层，只改角度/深度/透明度时不重绘子树；改 `Transform3DProperty` 时由父级重绘，旧投影区域才不会留残影。
- **合成遮罩**：`Visual.OpacityMask` / `VisualElement.OpacityMaskProperty` 用 `Visuals/OpacityMaskCompositor.cs` 乘合成位图；`OpacityMaskMode.Alpha` 取笔刷 alpha，`OpacityMaskMode.Luminance` 取预乘遮罩的亮度×alpha。承载 `ContainerVisual` 的 `VisualPresenter` 或舞台要 `ClipToBounds`，旋转/变形才不会在包围盒外留下旧帧。
- **渐变文字**：`Brush.CreateNativeBrush` 把 Solid/Linear/Radial 笔刷交给 `Graphics.DrawString`；`TextBlock` / `Button` / `TextBox` / `ComboBox` / `TextVisual` 都走这条路径，文本不再压成首色。多行 TextBox 每行单独铺渐变。
- **系统主题**：`ThemeMode.System` 是运行时模式；`Theme.EffectiveMode` 解析为 Dark/Light/HighContrast。读取 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`（P/Invoke `RegGetValueW`，避免 Registry 包依赖），`SystemInformation.HighContrast` 优先；`SystemEvents.UserPreferenceChanged` 事件投递到选模式时捕获的 `SynchronizationContext`，再 `RefreshSystemTheme()` 换调色板并触发 `Changed`。
- **资源引用**：`{StaticResource}` 只解析一次；`{DynamicResource}` 持续跟随字典增删改，并在资源消失时清回 DP 默认值。同一目标属性上的多个资源引用按“后写接管”处理：后到的静态引用会停掉先前的动态订阅，后到的动态引用重新接管。实现是 `VisualElement` 的 `RegisterResourceBinding(targetToken, binding)`；生成器把 DP 引用（或 CLR 属性字符串）作为 token 传给 `ApplyStatic/BindDynamic`。DynamicResource 仍只支持视觉/附加属性，Style/Trigger Setter 的值仍按声明期解析。
- **GridSplitter**：按目标在 sash 的哪一侧决定 delta 符号（左/上不反转，右/下反转）；起始尺寸取 `Target.Bounds` 的已安排宽高，不用 `Width`/`Height`（Auto 时它们是 NaN）。横向/纵向都有回归测试。
- **DataGrid / DropZone / 拖放**：`Visuals/Controls/DataGrid.cs` 是自绘二维表；源集合快照成 `_rows` + `_view` 索引，只画可见行；表头点击按升→降→源序循环，表头边缘拖拽改列宽；`DataGridColumn` 非视觉对象，`Kind` 为 Text/Badge/Check/Progress，`Width<=0` 分剩余宽。进程内拖拽用 `Easel.DataGrid.Rows` 载荷传同一对象引用，跨表移动同时从源 `IList` 删除；`DragStarting` 可补 `FileDrop`。`VisualHost.AllowDrop=true` 接原生 OLE；`VisualElement.AllowDrop` + `DragEnter/Over/Leave/Drop` 由最深命中目标向祖先冒泡；`ProcessDrag*` 可注入测试；`StartVisualDrag` 调 `Control.DoDragDrop`，`GiveFeedback` 移动浮标，Escape 由 `QueryContinueDrag` 取消。`DropZone` 收文件/文本/行并抛 `Dropped`。`DragEventArgs.X/Y` 是屏幕坐标，路由前必须 `PointToClient`。DataGrid/DropZone 的颜色、字体、圆角和选中/焦点态都取 `Theme.Current`；`GridChromeFollowsTheActiveFallbackPalette` 用自定义调色板断言表头与选中行像素。回归在 `Src/Test/Unit/Easel.Test/DataGridTests.cs`；有头探针用 `SetCursorPos` + `mouse_event` 跑通 grid→DropZone 的 `DragStarting`→`DragEnter`→`Dropped`，payload 保持同一行对象。
- **XAML**：`RectangleF` / `Rectangle` 字面量（`XamlRuntime.ParseRectangleF/ParseRectangle`，生成器 `IsRuntimeConvertedType`）；资源字典支持 `x:FontWeight`、`x:Thickness`、`x:PointF`；对 `Foreground` 这类 `Color` 访问器背后的 Brush DP，`StaticResource` 改用 `SetValue`，渐变笔刷能落到文本前景。
- **兜底主题覆盖**：`ThemePalette` 现在包含字体（Family/Size/Weight）、`ScrollBarThumb`/`Hover`/`Radius`、`ShapeStroke`/`ShapeStrokeThickness`、阴影 `ShadowColor/Offset/Blur/Opacity`、`TextVisual*`、`ControlBorderThickness`、`FocusThickness*` 等角色；所有原生控件的默认颜色、字体、边框/焦点宽度与滚动条拇指都从当前调色板读取，`VisualElement.DefaultFont` 也跟随 `Theme.Current.DefaultFont`。只剩布局 padding、箭头/刻度尺寸等算法常量保持字面量。Dark+/Light+ 的既有像素不变（对比截图无差异）。
- Easel 测试覆盖 3D 旋转/命中/失效、遮罩与渐变 XAML、主题角色往返与自定义调色板传播；VeloxDev 侧 `Src/Adapters/VeloxDev.WinForms/Xaml` 没有这些代码。

## Easel skill

- `Easel/skills/easel-build-winforms-xaml-ui/` 是 Easel 自己的完整 WinForms-XAML 指南：`SKILL.md` + `references/{runtime,xaml,controls,layout-input,visuals,themes,mvvm,animation,recipes,pitfalls}.md`。公开 API、推荐写法、诊断、主题、3D/合成和动画注入都以这里为准。
- VeloxDev 的 `veloxdev-create-workflow` / `veloxdev-create-animation` WinForms 页面只保留工作流专属内容（surface behavior、node/slot/link 模板、`ViewPool`、连接预览、VeloxDev 动画适配器），通用 UI 指南统一指向 Easel skill。
- 动画：Easel 只有 `IVisualAnimator` 契约；应用接受 VeloxDev 时安装 **`VeloxDev.WinForms` 12.0.0+**，模块初始化器自动接上 `EaselVisualAnimator`，无需额外动画包或手动注册。

## Gallery（`Easel/Examples/XamlGallery`）

- 外壳是 `Views/GalleryVisualShell.xaml` + `GalleryShellViewModel`；页面内大标题是 **Modern WinForms Gallery**，原生窗口标题仍是 `Easel Demos`。目录根节点写作 `Demos`，`Scenarios` 组是 8 个整页（dashboard / settings / player / video / chat / gradients / composition / 3d），其余为组件组。
- 主题选择是四选一 ComboBox：Dark+ / Light+ / High contrast / Follow system。`ThemeMode.System` 解析 Windows 的 `AppsUseLightTheme`（高对比时用 HighContrast），并用 `SystemEvents.UserPreferenceChanged` 在选模式的那个线程上重读；`Theme.EffectiveMode` 给出实际调色板。
- 兜底主题是库内的 `Src/Easel/Themes/{DarkTheme,LightTheme}.xaml`，根是 WPF 式 `<ResourceDictionary>`：调色板 Brush、`CornerRadius`/`ControlHeight` 与四个排版样式（Heading/Subheading/Body/Caption）。字体设置两者相同但每个文件都完整写一遍；`Theme.CreateDark/Light` 从这些 XAML 资源读回 `ThemePalette`，`Theme.CreateResourceDictionary(mode)` 给应用一份副本。High contrast 不是库内兜底：`Demo/Themes/HighContrastTheme.xaml` 是自定义主题示例，`ThemeService` 把它并入视图并用 `ThemePalette.FromResourceDictionary` 设为当前调色板；运行时仍有 `ThemeMode.HighContrast` 的 C# 调色板。生成器现在支持 `<ResourceDictionary>` 根、`x:Key` 资源、keyed Style/Template 与 `MergedDictionaries`（编译类型或内联；不支持文件 URI `Source`）。
- 每张卡是 `Views/Cards/*.xaml`；code-behind 只做 `DataContext`、树节点、弹层、日历/日期、位图、滑块/媒体接线。`Catalog/Cards/<Group>Cards.cs` 按组拼卡，`Catalog/CardCatalog.cs` 的顺序就是目录顺序。Styling 组有 `DynamicResource`/`StaticResource` 对比卡（`style.dynamic-resource`）。
- 媒体页是真实的：3 首 MP3 + 12s H.264/AAC 作为 `EmbeddedResource` 放在 `Demo/Assets/Media`，首次解压到 temp；音频用 WPF `MediaPlayer` 轮询，视频用 `MediaElement` + `ElementHost` 放进唯一的 `NativeControlHost` 岛。Demo 开 `UseWPF` 时必须关掉 WPF 默认 Page/ApplicationDefinition，否则 WPF 会和 Easel 争着编译视图 `.xaml`；`ElementHost` 不能设 `Dock=Fill`（NativeControlHost 用 Bounds 摆放，Dock 会铺满整个 VisualHost）。`UseWPF` 还会改变隐式 using（缺 `System.IO`）并引入 `System.ComponentModel.SortDescription` 的歧义。
- 场景页固定 540 宽；Data 组新卡 600–760 宽。`PreviewScroll` 横向 Auto（大卡可横向滚动），页内 `ScrollViewer` 横向禁用，窗口默认取工作区尺寸。3D/遮罩在 `graphics.*` 与 `scenario.*` 卡里演示。
- Data 组：`data.data-grid`（2,048 行 × 7 列，序号、徽章、进度、勾选、拖入拖出）、`data.drag-board`（两块 DataGrid 同一批 `SampleRow` 互移）、`data.virtual-list`（5,000 行并显示已实现区间）；ListBox/TreeView/Calendar/DatePicker/ComboBox 都放大并带状态标注。
- 生成器把 Style/Trigger Setter 的 `{StaticResource}` 装成 `Get<object>`，由 `SetValue` 转成 DP 类型；写成 `Get<Color>` 会让 Brush 主题资源在 `Foreground` 上转换失败。

## 发布

- Easel 独立仓的 `.github/workflows/publish.yml` 与 VeloxDev 同构：`version` 读 `Easel.csproj` / `Easel.Generator.csproj` 各自的 `<Version>` 并 HEAD flatcontainer，只发 NuGet 缺的版本；push 需仓库变量 `AUTO_PUBLISH=true`，`workflow_dispatch.force` 把两个包都当作缺失重跑；顺序是 `WinForms.Easel.Generator` 先推并轮询到可下载，再打包并推送 `WinForms.Easel`，最后按 `x3` unlist 同线旧 patch。
- `WinForms.Easel` 包不声明 `WinForms.Easel.Generator` 依赖（源项目对生成器是 Analyzer `ProjectReference`，消费者按 README 同时装两个包），所以工作流里的等待保证的是发布顺序，不是 restore 依赖。
- CI 的 publish job 是唯一校验 nupkg 里四个 TFM 资产齐全的地方（`scripts/pack-local.ps1` 只建 `net8.0-windows`）；推送前检查 `lib/net461*`、`lib/net5.0-windows*`、`lib/netcoreapp3.0*`、`lib/net8.0-windows*` 都存在。

## 未完成

- VeloxDev 自身的 XAML 运行时尚未切到 Easel；当前适配器只对同时引用 Easel 的宿主生效。
- WinForms 适配器仍用 `WorkflowSurfaceBehavior.EnsureAttachedBehaviors` 的每 pass 遍历安装四个行为；换成附着属性的 `ChangedCallback`（`WorkflowSlotLayoutBehavior.IsEnabled` 等）需要适配器消费含该能力的 Easel 版本（候选 1.2.0），并把 Release 的 `WinForms.Easel` 引用一起抬高。
- `VeloxDev.WinForms.csproj` 的 Release 从 NuGet 取 Easel 包（Debug 走子模块源码），所以发布顺序是 Easel 先于 VeloxDev.WinForms；父仓 `publish.yml` 在打包 WinForms 适配器前等待 `VeloxDev.WinForms.csproj` 里写的 Easel 版本在 NuGet 可下载。
