# VeloxDev.WinForms.Xaml — 架构

> 包：`VeloxDev.WinForms`（适配器 + XAML 运行时）与 `VeloxDev.WinForms.Generator`（专属生成器），同为 **12.0.0**。
> 代码：`Src/Adapters/VeloxDev.WinForms/Xaml/`（运行时）、`Xaml/Primitives/`（共享原语，命名空间仍是 `...Xaml.Controls`）、`Xaml/Visuals/`（视觉基类、绑定、样式、资源、宿主）与 `Xaml/Visuals/Controls/`（视觉控件）；生成器在 `Src/Generators/VeloxDev.WinForms.Generator/`；工作流表面在 `WorkflowSystem/AttachedBehaviors/`。
> 定位：**一套视觉 XAML 后端**。控件是 `VisualElement`，`VisualHost` 是唯一 HWND；不是 WPF 兼容层。
> 面向消费者的运行时手册在 `skills/veloxdev-create-workflow/references/practices/gui/winforms-xaml.md`；动画在 `skills/veloxdev-create-animation/references/practices/winforms-xaml.md`。

## 一、运行时

| 文件 | 类型 | 职责 |
|---|---|---|
| `Primitives/Thickness.cs` / `LayoutPrimitives.cs` / `TextPrimitives.cs` | `Thickness` / `GridLength` / `RowDefinition` / `ColumnDefinition` / 布局枚举 | 布局值类型与枚举，全部在 `VeloxDev.WinForms.Xaml.Controls` |
| `Primitives/XamlDrawing.cs` | `internal XamlDrawing` | 自绘 chrome 辅助（环外轮廓 -0.5） |
| `Primitives/PathGeometry.cs` | `Shapes.SvgPathParser` | `<Path Data>` 的 SVG 子集解析 |
| `DependencyProperty.cs` + `DependencyProperty.Visual.cs` | `DependencyProperty` / `PropertyMetadata` | WPF 写法注册；视觉值经 `IWinformsObject` 的优先级表存取、类型校验 |
| `WinformsObject.cs` | `IWinformsObject` / `WinformsObject` | 视觉基类核心：优先级层（Animation→Local→StyleTrigger→Template→Style→Inherited）、`GetObservable`/`Bind`、`INotifyPropertyChanged` |
| `Visuals/Visual.cs` | `Visual` / `ContainerVisual` / `ShapeVisual` / `TextVisual` / `CurveBatchVisual` / `DropShadowEffect` | 视觉树节点：`Bounds`（父坐标）/ `Transform` / `Opacity` / `Clip` / `Effect`；`RenderContent` / `HitTestContent` |
| `Visuals/VisualElement.cs` | `VisualElement : ContainerVisual, IDisposable` | 布局/输入/渲染基类：`Margin`/`Padding`/`Width`/`Height`/对齐/`Background`/`CornerRadius` 等 DP，measure/arrange、指针/键盘事件、逻辑焦点 |
| `Visuals/VisualHost.cs` | `VisualHost : System.Windows.Forms.Control` | 唯一 HWND：`Root`、`Resources`、`DpiScaleOverride`、`RenderTo`、`ProcessPointer*` / `ProcessKey*` / 文本输入 |
| `Visuals/Controls/` | 视觉控件 | `UserControl`/`ContentControl`/`Border`/`StackPanel`/`Grid`/`DockPanel`/`Canvas`/`WrapPanel`/`UniformGrid`/`Viewbox`/`ScrollViewer`/`ScrollBar`/`ScrollContentPresenter`/`ItemsControl`/`ListBox`/`VirtualList`/`TreeView`/`ComboBox`/`TextBlock`/`TextBox`/`RichTextBox`/`Button` 系/`Slider`/`ProgressBar`/`Calendar`/`DatePicker`/`VisualMenu`/`VisualContextMenu`/`VisualPopup`/`VisualPresenter`/Shapes |
| `Visuals/VisualBindingOperations.cs` | `VisualBindingOperations` / `VisualBindingExpression` | OneWay/OneTime/TwoWay、`DataContext` 路径、Converter/StringFormat/FallbackValue/TargetNullValue；`RelativeSource.Self`/`TemplateBinding`；无 `ElementName`/校验规则 |
| `Visuals/Styling.cs` | `VisualStyle` / `VisualSetter` / `VisualTrigger` | 显式/隐式样式；setter 写 `Style` 层、trigger 写 `StyleTrigger` 层；`EnterAction`/`ExitAction` 在激活/失效时各跑一次；移除只清自己那两层 |
| `Visuals/VisualTriggerActions.cs` | `VisualTriggerAction` / `TriggerActionGroup` / `TransitionAction` / `KeyFrameAnimation` / `VisualTransition` | 视觉目标（`VisualElement`）的触发器动作；动画经 `Transition<VisualElement>` 写 `Animation` 层，同一目标的新动画覆盖旧动画，`Stop` 停住 |
| `Visuals/VisualControlTemplate.cs` | `VisualControlTemplate` | 模板应用：`TemplatedParent`、每实例 `VisualNameScope`、`GetTemplateChild<T>`；模板触发器写 `Template` 层，动作与 setter 用同一个 `TargetName` 部件 |
| `Visuals/Controls/ButtonBase.cs` | `ButtonBase` | `Command`/`CommandParameter` DP；激活时 `CanExecute` 通过才执行；拒绝时命令负责禁用/恢复 |
| `Visuals/VisualResources.cs` | `VisualResourceLookup` | 按「元素 → 祖先 → `VisualHost.Resources`」查找并做类型转换 |
| `Visuals/DataTemplate.cs` | `DataTemplate` | `Func<VisualElement>` 工厂，由生成器填充；keyed 资源存进 `Resources` |
| `Visuals/VisualTemplateSelector.cs` | `VisualTemplateSelector` | `object?` → `VisualElement?`；工作流的选择器基类 |
| `Visuals/VisualShapes.cs` | Shapes | `Rectangle`/`Ellipse`/`Line`/`Polygon`/`Polyline`/`Path` |
| `Visuals/VisualText.cs` | `VisualText.Draw` | 视觉子树的文字一律走 GDI+ `DrawString`；`TextRenderer` 不认 world transform |
| `Visuals/VisualInput.cs` | 输入重载 | `VisualHost` 把指针/滚轮/键/文本路由到最深命中元素 |
| `Theme.cs` | `Theme` / `ThemePalette` | 明/暗调色板；视觉控件渲染时直接读 `Theme.Current` |
| `WorkflowSystem/AttachedBehaviors/WorkflowSurfaceBehavior.cs` | 附着行为 | `VisualHost` 上解析 `PART_*` 视觉部件；相机矩阵（`PART_Canvas.Transform`）、Core 输入路由、网格/小地图/视口/虚拟化推送；相机 API `GetZoom`/`GetPan`/`SetPan`/`SurfaceToWorld`/`WorldToSurface`/`GetWorldViewport`/`SetWorldViewport`/`ZoomAt`/`ProcessWheel`/`Refresh` |
| `WorkflowSystem/AttachedBehaviors/ViewPool.cs` | 附着行为 | 通用视觉池（与工作流无关）：一个 item 一个视觉子元素，`ItemsSource`/`TemplateSelector`/`ItemTemplate`，设 `DataContext`，按 `Panel.ZIndex` + 源序排序 |
| `WorkflowSystem/AttachedBehaviors/SlotStateBrushConverter.cs` | `IValueConverter` | 模型 `SlotState` → 端口字形画笔，供模板绑 `PART_Glyph.Fill` |
| `WorkflowSystem/AttachedBehaviors/WorkflowSlotLayoutBehavior.cs` / `WorkflowNodeDragBehavior.cs` / `WorkflowSlotConnectionBehavior.cs` | `VisualElement` 附着行为 | 槽测量写 `slot.Anchor`、标题行拖动、按下-释放在接收口完成连接；由 `WorkflowSurfaceBehavior` 在每个视觉元素上 `Ensure` |
| `WorkflowSystem/AttachedBehaviors/WorkflowMinimapOverlay.cs` / `WorkflowInfoOverlay.cs` | `VisualElement` | 视觉小地图与 HUD；表面推偏移、拖小地图反推滚动 |
| `WorkflowSystem/Platform/NativeWindowStyleHelper.cs` / `WorkflowDpi.cs` | Win32/DPI 辅助 | `WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`、`BeginRedraw`/`EndRedraw`；DPI 换算 |

- **布局**：`VisualElement.Measure/Arrange` 管 Margin、显式尺寸、Min/Max、对齐与 Visibility；面板在 `MeasureOverride/ArrangeOverride` 只算内容。`Hidden` 占位不绘制，`Collapsed` 不测量不占位。`PropertyMetadata.AffectsMeasure/AffectsArrange/AffectsRender` 驱动失效。**`InvalidateArrange` 只失效摆放，保留测量缓存**；`Arrange` 在最终尺寸不变且子树未失效时跳过 `ArrangeOverride`，`Canvas.Left/Top` 只让父级重排，不把整张卡的内容再排一遍。可用尺寸真变了，`Measure` 自己会按 available 比较重测。
- **输入**：`VisualHost` 命中测试最深元素，事件沿 `ParentElement` 冒泡；焦点是逻辑焦点，`FocusElement` 把原生焦点留在 Host 上，Tab 在焦点链里走。指针捕获、Enter/Leave、滚轮、文本输入都集中在 Host。
- **`ContentControl`** 的 `ContentTemplate` 只作用于非 `VisualElement` 内容：模板创建视觉并设 `DataContext = Content`；`VisualElement` 内容直通。解析顺序：presenter 自己的 selector → 自己的 `ContentTemplate` → `TemplatedParent` 的 selector → `ContentTemplate` → 文本投影。`ContentTemplateSelector`（`DataTemplateSelector`）拿到原始 `Content`；`ContentStringFormat` 只在文本投影路径按当前文化 `string.Format`。`ContentControl.Template` 走 `VisualControlTemplate`，与内容模板是两条路。
- **`ScrollViewer` 是 `ContentControl`**：默认模板（`DefaultTemplate`）是 `Grid`：星号主格放 `ScrollContentPresenter(PART_ScrollContentPresenter)`，右列/下行各是 `Auto` 轨道放 `ScrollBar(PART_HorizontalScrollBar`/`PART_VerticalScrollBar)`，两条占用视口而不是覆盖内容；调用方 `Template` 或样式模板优先于默认 chrome。`ScrollContentPresenter` 按视口宽、纵向不设限量内容，裁切并偏移，并实现 `IScrollInfo`；`ScrollBar` 负责拇指/轨道/键盘、可选模板部件（`PART_Track`/`PART_Thumb`/`PART_*RepeatButton`）与按住自动翻页。`CanContentScroll=true` 且内容是 `IScrollInfo` 时，extent/offset/滚轮/分页都交给内容自己的单位。外部相机模式（`IsExternalScroll` + `SetExternalOffset`）只让 presenter 报范围、不偏移内容，条的手势转成 `ScrollOffsetRequested`。
- **`TextBox`** 自绘插入符/选区/剪贴板，聚焦时默认用 `NativeControlHost` + 原生 `System.Windows.Forms.TextBox` 岛处理 IME/无障碍；`UseNativeEditor=false` 时聚焦也走自绘（DP、模板、绑定、撤销全保留，只失去 IME 组合与原生无障碍 peer）。其余控件（`RichTextBox`、`ComboBox`、`Calendar` 等）都是纯视觉控件；唯一的另一条原生路径是显式 `NativeControlHost`。
- **`DataContext`** 是 inherited 属性；没有本地层时从 `ParentElement` 解析，祖先切换向无本地值的后代补发 `Inherited` 变更；子树换父级时 `VisualElement.OnParentChanged` 也给自身与后代补这一跳。绑定只监听本元素的 DataContext 变化，少了这一跳，预构造的嵌套子树挂到有上下文的父级后会一直停在空源上（如 XAML 视图里的按钮 `Command` 绑定）。
- **覆盖层**：`VisualHost` 在根树之后绘制、之前命中铺满客户区的覆盖层；`VisualPopup` 挂进去，不受祖先 `ScrollViewer.Clip` 裁切。弹层脱离根树，打开时把目标 `DataContext` 以 `Inherited` 层推给内容。
- **`RichTextBox`** 用扁平字符缓冲，每字符带 `RichTextStyle`；绘制必须走 `VisualText.Draw`。
- **渲染缓存**：`Visual.CacheAsBitmap` 缓存本地子树；`VisualElement.InvalidateVisual` 标脏最近的缓存并上报矩形，`VisualHost` 只清/画脏矩形；有变换或 `Effect` 时整面重画。
- **默认暗色对齐 Scribalix / VS Code workbench**：画布 `#141414`、面板 `#252526`、raised `#2D2D2D`、输入 `#2A2A2A`、边框 `#333333`、文本 `#E7E7E7`/`#A0A0A0`、accent/focus `#0078D4`、选中 `#094771`；light 为 Light+（`#FFFFFF`/`#F3F3F3`、选中 `#ADD6FF`）。4px 圆角、28px 控件高。
- **`Theme` 内置三套调色板**：`ThemeMode.Dark` / `Light` / `HighContrast`。高对比是纯黑底、白字、琥珀 accent（`#FFB000`）、聚焦 `#FFD700`、`CornerRadius=2`、`ControlHeight=30`；`ThemeMode` 切换整套 `ThemePalette`，控件在下次构造/根重建时读取。
- **DPI**：`VisualHost` 按 `DeviceDpi/96` 物理渲染；布局、相机、模型锚点与装饰偏移都是逻辑 DIP。
- **属性没有别名**（用户定）：一个概念只有一个 DP 名，`Background`/`Foreground` 而不是 `BackColor`/`ForeColor`。

## 二、源生成器

- `[XamlProperty]`：partial 属性 → 静态 `DependencyProperty` + CLR 包装；`[XamlAttachedProperty]`：静态 partial `GetX`/`SetX` → `RegisterAttached`。
- 没有 `DefaultValue` 时写 `default(T)`；非可空引用类型必须给 `DefaultValue` 或声明可空，否则报 `VELOX_XAMLP005`。
- `*.xaml`（`AdditionalFiles`）→ `private void InitializeComponent()`（普通方法，不要求/不生成 partial 声明）。
- **前缀解析**：不带前缀按视觉后端找 —— `Xaml.Visuals.Controls` → `.Visuals.Controls.Shapes` → `.Visuals` → 共享原语；`local:` 是调用方类型；历史 `v:` 是别名；`native:`/`System.Windows.Forms` 被拒。找不到报 `VELOX_XAML019`（Error）。
- 视觉根必须派生自 `VisualElement`；单内容容器（`UserControl`/`ContentControl`/`ScrollViewer`）的子元素生成 `Content =`，其余 `Add`；有 `Items` 的父级写 `Items.Add`。
- `x:Name` 生成字段并写 `VisualElement.Name`；模板里注册进 `VisualNameScope`。显式 `Name="..."` 后写覆盖。
- 资源：标量、空元素对象（`<local:Selector x:Key="..." Template="{StaticResource ...}" />`）、`<Style>`、`<ControlTemplate>`、`<DataTemplate>`。keyed `DataTemplate` 编成工厂存进 `Resources`；`<DataTemplate>` 也可直接作 `ItemsControl.ItemTemplate`。`{DynamicResource}` 报诊断。
- 子元素上的 `{StaticResource}` 以视图根为锚，因为生成代码先赋属性、后挂父级。
- 内联 `Trigger`：`<Trigger Property="IsHot" Value="True"><Trigger.Setters>…</Trigger.Setters></Trigger>`；样式触发器经 `<Style.Triggers>`。触发器可带 `<Trigger.EnterAction>` / `<Trigger.ExitAction>`，内容为 `TransitionAction`、`KeyFrameAnimation`、`TriggerActionGroup` 或派生 `VisualTriggerAction` 的自定义动作。
- 事件：CLR 事件（`Click`/`PointerPressed`/`KeyInputDown` 等）由生成代码原位订阅。
- 诊断 `VELOX_XAML001–019` / `VELOX_XAMLP001–005`；`VELOX_XAML002`（未知成员）是 Error。

## 三、边界与不变量

- 支持：视觉控件树、DP/附加属性、`DataContext` 继承、OneWay/TwoWay/OneTime 绑定、Converter/StringFormat/Fallback/TargetNull、资源、显式/隐式 `VisualStyle`、样式/模板触发器与 enter/exit 动作、`ControlTemplate` + `ContentPresenter`、`ContentTemplate`、`ItemsControl.ItemTemplate`、keyed `DataTemplate`、`ScrollViewer` 默认/自定义模板与 `ScrollBar` 模板部件、`VisualPopup`/`VisualMenu`、`NativeControlHost`、DPI、`Theme`。
- 不支持：设计器/热重载、`ElementName`/校验规则、`DynamicResource`、可变行高虚拟化、每条关键帧独立 `Ease`、`CommandBinding`/`InputBindings`、`NativeControlHost` 被变换/裁剪/混色。
- **一个视觉父级只接受 `VisualElement` 子元素**；`native:` 与 `System.Windows.Forms` 控件不进视觉树，只能经 `NativeControlHost` 作矩形轴对齐岛。
- `VisualTemplateSelector` 是 item → 元素的唯一接缝；`VisualStyle` 是描述不是活对象，改已应用样式的集合不会自动重应用。
- `VisualElement.Resources` 与 `VisualHost.Resources` 是普通 `ResourceDictionary`；静态资源在构造期解析，键必须先声明。

## 四、坑（带依据）

- **`Path` 的 `Stretch` 用显式 `Matrix(m11, m12, m21, m22, dx, dy)` 构造**：GDI+ 行向量下 `Scale`/`Translate` 叠加不会把 `-source` 一起缩放，整条几何会平移出控件。回归：`Path_UniformStretch_FitsTheGeometryIntoTheContentBounds`。
- **`Grid` 先按轨道量、再按格子收紧**：`Grid.ResolveTracks` 第一遍 Auto 轨道沿该轴不设限（`int.MaxValue`，容器减掉 Padding 后按 `int.MaxValue / 2` 判定）、像素与星号轨道按整张网格量；轨道尺寸定下后按子项实际格子再量一次。少第二遍时 `ScrollViewer` 会把内容算成整格宽，安排进窄轨道后右对齐内容被推出裁剪区，槽位锚点也落在裁剪区外。回归：`Grid_MeasuresAChildInsideItsFixedTrack`、`Grid_AutoRowMeasuresANestedStarGridAtItsContentHeight`。
- **`ScrollContentPresenter` 宽按视口、纵向不设限量内容**：宽若也不设限，`VirtualList` 会把 `int.MaxValue` 当 desired width，行内 `TextBlock` 画到 `int.MaxValue` 宽的矩形上抛 GDI+ `OverflowException`；纵向不设限才能让列表报告完整 extent。回归：`VirtualList_RealizesOnlyVisibleRowsAndFollowsTheScrollOffset`、`ScrollViewer_MeasuresStarContentAtItsNaturalHeight`。
- **DataContext 绑定的源是父级上下文**：`VisualBindingExpression` 对 `DataContext` 目标的绑定取 `ParentElement.DataContext`，并同时订阅父级的 DataContext 变化与 `VisualElement.ParentChanged`；父上下文/父级变化后重新解析源，复用视图的本地 DataContext 绑定才不会停在旧值。回归场景：ViewPool 复用 EnumSelectorNodeView 后输入口重新绑到新 SlotViewModel。
- **重绘脏区按本地矩形变换**：`InvalidateVisual` 取 `GetInvalidationBounds()`（元素可覆写成实际绘制区）经 `PointToSurface` 变成表面包围盒，不再因为祖先有 `Transform` 就整面重画；位图缓存和 `Effect` 仍走整面/外扩。`VisualHost.LastPaintTimestamp`/`PaintCount`/`LastPaintWasClockDriven` 记录绘制来源，动画宿主据此让定时帧给拖动重绘让位。`MotionScale` 默认 1（关闭），手势期间可降分辨率；`Visual.RenderDamage` 只对局部脏区裁剪不相交子树，位图缓存/Effect 不裁。
- **局部重绘的裁剪区要外扩 1 个逻辑单位**：`OnPaint` 先用 `BackColor` 铺脏区，`RenderPhysical` 再把物理脏区转成逻辑裁剪；DPI/GDI+ 取整会让脏区最后一列/行没被视觉树覆盖，拖动时留下一条宿主底色的 1px 竖线。`RenderPhysical` 现在把逻辑裁剪 `Inflate(1,1)`。`Slider` 会被水平栈拉伸到整列高，整列失效会把这条缝拉满高度，所以它的 `GetInvalidationBounds` 只报中间轨道带。
- **子元素实际尺寸变化自动反映到 extent/条**：内容 `Width/Height` 或 desired 改变后，presenter 重测并更新 extent/viewport，条的最大值随之变化；工作流把世界范围写进 `PART_Canvas.Width/Height`，条仍由控件层测量驱动，相机只决定子元素该多大。回归：`ScrollViewer_FollowsChildSizeChanges`。
- **`ScrollViewer` 的 desired 要回到 presenter 的内容尺寸**：默认模板的 `Grid` 有星号轨道，会按可用尺寸铺满；`ScrollViewer.MeasureOverride` 必须返回 presenter 的 desired（+ Padding），否则放进 ComboBox 下拉弹层/StackPanel 的 Auto 尺寸时会把整条可用宽度占掉，弹层盖住旁边控件。回归：`ComboBox_OutsidePressClosesAndFocusesTheClickedElement`。
- **滚动条占 Auto 轨道，不覆盖内容**：`ScrollBar` 只在自身轨道条带（含命中内缩）应答；默认 chrome 的两条条各占一行/一列，presenter 视口随之缩小。显示/隐藏用 `Visibility`；切换后必须让 `TemplateRoot`/`ScrollViewer` 重新 `Measure`——父级的测量缓存不会因子元素折叠/展开自动失效。轨道按下先翻一页、同一笔继续拖动；按住约 400ms 后每 50ms 重复一页；悬停拇指用主题前景色高亮。回归：`ScrollViewer_TrackPressContinuesAsTheSameDrag`、`ScrollViewer_ScrollBarCornerIsNotShared`、`ScrollViewer_ThumbBrightensUnderThePointer`、`ScrollBar_TemplatePartsPositionTheThumb`、`ScrollBar_TrackPressPagesOnceThenRepeats`。
- **布局期间的新失效不能被本轮结尾吞掉**：`VisualHost.EnsureLayout` 在进入时置 `_layoutValid = true`，结束不再写 true；measure/arrange 里触发的 `InvalidateMeasure` 会把它留成 false，下一帧再排。滚动条第一次算出范围就是在 presenter 的 Arrange 里改 `Visibility`，少了这条会永远错过第二次测量。回归：`ScrollViewer_AutoBarsSettleAfterTheFirstLayout`。
- **WPF 语义补齐**：`Binding` 支持 `FindAncestor`/`AncestorLevel`、`PropertyChanged`/`LostFocus`/`Explicit`、`OneWayToSource`/`Default`、`ValidatesOnExceptions`/`DataErrors`/`NotifyDataErrors` 与 `ConverterCulture`；资源改为按目标元素链解析并支持 `DynamicResource`；`DataTemplate`/`ItemTemplate`/`ItemsPanelTemplate` 有独立名字作用域；支持附加属性 property element、直接 `Style` setter/trigger、`ItemsPanel`/`ItemsPresenter`/`IsItemsHost`，不支持的触发器/模板显式报 `VELOX_XAML004`。
- **渲染与输入属性是 DP**：`Opacity`/`Transform`/`Clip`/`ClipToBounds`/`Effect`/`CacheAsBitmap` 在 `VisualElement` 上有 DP 覆盖；`FontSize`/`FontFamily`/`FontWeight` 继承，`Foreground` 以 Brush 存储、保留 Color 访问器；`IsEnabled` 继承，`TabIndex`/`IsTabStop` 参与焦点顺序。
- **控件模板**：`TemplatedControl`/`ButtonBase`/`ItemsControl`/TextBox/RichTextBox/ComboBox/Menu/Calendar/DatePicker/TreeView/VirtualList 都应用 `Template` 与模板触发器；TextBox 的 `PART_ContentHost` 原生编辑器只能覆盖定位（HWND 不随视觉变换/裁剪），RichTextBox/ComboBox 等缺必需部件显式抛 `InvalidOperationException`。剩余边界：VirtualList 无逐行容器、TreeView 无虚拟化、templated Menu 的下拉仍自绘。
- **动画类型**：`Matrix`/`RectangleF?` 已注册采样器；`bool` 不是可动画类型，`ClipToBounds`/`CacheAsBitmap` 等 bool 过渡显式抛 `NotSupportedException`，无采样器的属性不会被接受后冻结。旧 native XAML profile 已删除。
- **`CanContentScroll` 的单位由内容决定**：内容是 `IScrollInfo` 时，`ScrollViewer` 用它报告的 extent/viewport/offset 驱动两条条与滚轮/分页，presenter 只负责测量/裁切并把自身偏移归零；`VirtualList` 仍是像素滚动，尚未实现按项的逻辑单位。回归：`ScrollViewer_CanContentScrollUsesTheContentsScrollInfo`。
- **原生 TextBox 不支持透明背景**：`TextBox.ApplyNativeProperties` 通过 `ResolveNativeBackground` 取自身或最近不透明祖先的颜色（兜底 `Theme.Current.ControlBackground`）。把视觉层的 `Transparent` 直接写进 `_editor.BackColor` 会抛 `ArgumentException: 控件不支持透明的背景色`。回归：`TextBox_TransparentBackgroundFallsBackToTheNearestOpaqueAncestor`。
- **`PathGeometry.Parse` 的每个 `M` 必须 `StartFigure`**（再补零长度段把起点挪到 M 坐标）；当成 `L` 会在子路径间画连接线，白色描边就成了横穿字形的割线。回归：`Path_MultipleMoveCommands_StartSeparateFigures`。
- **视觉子树的文字走 `VisualText.Draw`**：`TextRenderer` 直接写 HDC、不认 world transform，嵌套平移后文字会画到表面原点。
- **`DockPanel` 顶部子项要显式高度**：没有 `RowDefinitions` 的 `Grid` 默认一行星号，会吃掉全部可用高度。
- **`NativeControlHost` 是 airspace 岛**：子 HWND 永远浮在视觉树之上，不能变换/裁剪/混色，弹层也盖不住它。
- **原生岛不会每趟布局都重走 `ArrangeOverride`**：`NativeControlHost.ArrangeOverride` 是 `VisualHost.PlaceNative` 的唯一登记点，但 `VisualElement.Arrange` 在尺寸不变且已安排过时跳过 `ArrangeOverride`；`EnsureLayout` 末尾的扫描曾因此把岛当成“这趟没安排到”摘掉（岛仍 `IsUsingNativeEditor`，却已脱离宿主、拿不到焦点）。扫描现在对仍满足 `host.Surface == this && host.IsVisible` 的宿主按 `AbsoluteBounds` 重新 `PlaceNative`，其余才 `RemoveNative`。回归：`TextBox_NativeEditorSurvivesTheNextLayoutPass`。
- **原生岛拿走原生焦点不算逻辑失焦**：`VisualHost.OnLostFocus` 只在 `!ContainsFocus` 时清 `_focused`，`FocusElement` 末尾也只在 `!ContainsFocus` 时重新聚焦宿主。否则宿主焦点会把岛顶掉，`TextBox.OnLostFocus` 再藏岛，焦点在宿主与岛之间反复来回。回归：点击文本框后原生编辑器应保持 `NativeEditor.Focused`，且下一次布局不摘岛。
- **原生岛按祖先 Clip 裁剪**：`VisualHost.PlaceNative` 把子 HWND 的矩形裁到宿主客户区与祖先 `ClipToBounds`/`Clip` 的交集，交集小于 1px 就隐藏。HWND 不受视觉 `Clip` 约束，不裁的话滚动到视口边缘的 TextBox 编辑器或 `NativeControlHost` 会盖住滚动条和其它面板。回归：`TextBox_NativeEditorIsClippedToTheScrollViewport`。
- **`Calendar` 不在“今天”上留标记**：只画选中日的 accent 填充。焦点环矩形或小圆点都会在换选日期后留在旧日期上，看起来像没清掉的选中框。回归：`Calendar_SelectingAnotherDayLeavesNoMarkerOnToday`。

## 五、验证

```bash
dotnet test Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/VeloxDev.WinForms.Xaml.Test.csproj
dotnet test Src/Test/Headless/VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Strict
dotnet build "Examples/Workflow/WinForms Trimmed/Demo/Demo.csproj"
dotnet build "Examples/Workflow/WinForms/Demo/Demo.csproj"
```

视觉测试多数只经 `VisualHost.RenderTo` 与 `ProcessPointer*`，不必 `Show()`；`WorkflowCameraTests` 覆盖相机/命中/缩放，`WorkflowSurfaceBehaviorTests` 覆盖槽测量与标题拖动，`VisualViewPoolTests` 覆盖池的物化/回收/排序。模板 harness 证明七家生成、构建、drift 0，但不证明绘制；画面验证跑 Trimmed / Full demo 或 `XamlGallery`。
