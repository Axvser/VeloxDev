# VeloxDev.WinForms.Xaml — 扩展路径

> 这里回答「要加一个新控件 / 新属性 / 新样式能力，动哪几处，按什么顺序」。架构见 [architecture.md](architecture.md)。

## 一、新增一个 XAML 控件

顺序：

1. 在 `Src/Adapters/VeloxDev.WinForms/Xaml/Controls/` 新建类，继承 `Control` 或合适的派生基类（布局面板继承 `Control`，单内容继承 `ContentControl`，选择器继承 `Selector`）。
2. 视觉属性用 `DependencyProperty.Register`；可动画的像素/颜色/`Thickness` 用默认 `AffectsRender=true`，纯配置属性写 `new PropertyMetadata(...) { AffectsRender = false }`。
3. 在 `OnPaintBackground`/`OnPaint` 里自绘；需要子控件的在 `OnLayout` 里按 `XamlLayout.ContentBounds` 摆放，别依赖原生 `Dock`，除非模板根（见 §五）。
4. 交互控件把类型名加进 `ThemeStyles.SurfaceTypes`；容器**不要**加，容器保持透明。
5. 生成器无需登记：不带前缀的类型按 `Controls` → `Controls.Shapes` → `.Xaml` 顺序反射查找。
6. 测试放 `Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/`；视觉能力要在 `Examples/XamlGallery` 的卡片条目里出现（`GalleryCatalog.cs` + 对应 Section 的 `x:Name`）。
7. 更新 `skills/veloxdev-create-workflow/references/practices/gui/winforms-xaml.md` 的控件表与本模块记忆。

联动清单（漏一处通常不报错，只是悄悄少一块）：`XamlRuntime.ConvertValue`（新字面量类型）、`ThemeStyles`（是否交互控件）、`XamlCompiler` 的类型解析顺序（只在新增命名空间时）、文档、Gallery。

## 二、新增一个依赖属性

- 生成器路径：`[XamlProperty(DefaultValue = ...)] public partial T X { get; set; }`。非可空引用类型必须写 `DefaultValue` 或改成可空，否则 `VELOX_XAMLP005`。
- 手写路径：`DependencyProperty.Register(nameof(X), typeof(T), typeof(Owner), new PropertyMetadata(...))` + CLR 包装。
- 附加属性：`DependencyProperty.RegisterAttached` + 静态 `GetX`/`SetX`；适配器 owner 在 XAML 里不写前缀（`Grid.Row`），自己的 owner 写 `local:Owner.X`，原生 owner 写 `native:Owner.X`。
- 原生控件上的附加值只支持 OneWay/OneTime；TwoWay 不成立。

## 三、新增一个可插拔类型

- 值转换器：实现 `IValueConverter`；需要 `ConverterParameter` 再实现 `IParameterizedValueConverter`。
- 模板选择器：继承 `VeloxDev.WinForms.Xaml.TemplateSelector`，暴露 `DataTemplate` 属性；XAML 里用空元素对象资源 `<local:Selector x:Key="..." Template="{StaticResource ...}" />`。
- 自定义动作：继承 `TriggerAction`，公共无参构造 + 可写属性；`Invoke(Control)` 里用 `XamlTransition.Animate`。嵌套内容与非 `{StaticResource}` 标记不支持。
- 校验规则：继承 `ValidationRule`，`Validate(object?, CultureInfo)` 返回 `ValidationResult`。`Binding.ValidationRules` 没有 XAML 语法，用 code-behind `SetBinding`。

## 四、轻量视觉树

- 需要 Transform / Opacity / Clip / Effect，或大量不交互的图形/连线时，用 `VisualHost` + `Visual` 树，而不是给每个元素建一个控件。
- 新视觉类型继承 `Visual`，实现 `RenderContent`，按需覆写 `HitTestContent`；子项用 `ContainerVisual.Add`。`Bounds` 是父坐标，内容画在 `0..Width/Height`。
- 效果实现 `VisualEffect`：`Padding` 声明额外空间，`Render` 在源位图上合成；组透明度与效果由 `Visual` 统一走临时位图。
- `VisualHost.Root` 铺满宿主客户区；改树后调 `InvalidateVisual()`；命中用 `HitTestVisual(Point)`。
- **不要**把原生/HWND 控件塞进视觉树，也不要把视觉节点当成可交互控件：两者没有共同的输入、焦点、无障碍管线。

## 五、样式与模板

- 显式样式 `Style="{StaticResource Key}"` 由资源替换/移除驱动重应用；无 `x:Key` 的 `TargetType` 样式是隐式兜底。
- `Style.Template` 接受 `ControlTemplate`；模板根的 `Dock=Fill` 由运行时设置，**不要**在模板里自己写 `Dock`。
- 模板触发器里的 Setter 指命名部件时写 `TargetName`；部件属性不属于模板父时再写 `TargetType`。
- 样式 Setter 里的 `{DynamicResource}` 在**活动控件**上求值；跨视图的键必须在控制树祖先里，而不是兄弟视图的资源里。

## 六、官方做法 vs 看起来能编译的捷径

| 想做的事 | 官方做法 | 错的捷径 |
|---|---|---|
| 在 XAML 里用适配器类型 | 直接写 `Foo`（不带前缀） | 写 `native:Foo` —— `native:` 只解析 `System.Windows.Forms` |
| 在 XAML 里用原生 WinForms 控件 | 写 `native:Foo` | 写 `Foo` —— 现在按适配器解析，找不到就报 `VELOX_XAML019` |
| 嵌套视图共用调色板/样式 | 祖先放隐式样式，或 `{DynamicResource}`；`StaticResource` 在视图构造期只能看到自己的字典 | 在子视图里 `{StaticResource}` 祖先字典的键 —— 构造时还没有祖先，拿不到 |
| 固定高度的控件放进横向堆叠 | 直接写 `Height`；显式尺寸压过 Stretch 并居中 | 依赖 Stretch 再自己算 Alignment |
| 单内容容器里让文字居中 | 子控件写 `VerticalAlignment="Center"`；容器必须走 `XamlLayout.Arrange` | 在 `OnLayout` 里直接 `child.Bounds = 内容区`，对齐被绕过，文字贴顶 |
| 卡片按内容定高 | 不写 `Height`；`Border` 会测「子控件 + Padding」，折行文本也算进去 | 写死一个 `Height`，换行/增删一行后底部被裁 |
| 面板自己算尺寸 | 重写 `MeasureCore` 后用 `ConstrainSize` 套回自身 `Width`/`Height` | 直接返回内容尺寸，XAML 上写的 `Width`/`Height` 静默失效 |
| 模板根撑满模板父 | 交给运行时（`Dock=Fill`） | 在模板根写死 `Width`/`Height` |
| 虚拟化列表 | `ScrollViewer` 里放 `ItemsControl Virtualizing=True ItemHeight=N`；未写 `ViewportHeight` 时用 viewer 客户区 | 把 `ItemsControl.Height` 设成全部条目高又期望它自己算视口 |
| 静态内容要滚动 | 用 `ScrollableControl`（原生 AutoScroll）并给内容 `Dock=Top`；内容高度由 `GalleryLayout` 这类宿主量 | 把静态面板塞进 `ScrollViewer` 期待它自动平移 —— `ScrollViewer` 只报告偏移，不移动内容 |
| 换内容/重建条目/换主题要一帧落地 | `using var render = XamlRenderScope.Begin(root)`；冻结整棵 HWND 树，结束后一次重画 | 只调 `Invalidate()` 或 `SuspendLayout()` —— 每个子窗口仍按自己的 `WM_PAINT` 露出中间态 |
| 自绘 1px 边框 | `XamlDrawing.PaintChrome`（`Color`）或 `PaintChromeWithBrushes`（`Brush`）；环的外轮廓已经压到 -0.5 | 自己用 `pen.DrawPath` 贴 0 / 0.5 画边框 —— 右、下各少半像素，或被不透明的子控件盖住 |
| 需要无 HWND 的控件树 / 大量节点 | `VisualSurface` + `VisualElement`（`Xaml/Visuals/`）；元素继承 `ContainerVisual`，不是 `Control` | 继续用 `Xaml/Controls/*` 每个控件一个 HWND —— 结构切换必然逐窗口上屏，节点数也被 HWND 成本吃掉 |
| 需要原生 WinForms 控件 | `NativeControlHost` 单子窗口岛；保持矩形、轴对齐，它永远浮在视觉树上方 | 把原生控件塞进需要 Transform/Opacity/Clip 的视觉子树 —— airspace，合成与裁剪都不成立 |
| 视觉元素要新增一个会触发重排/重绘的属性 | `WinformsProperty.Register<TOwner, T>(..., affectsMeasure/Arrange/Render)` + CLR 包装；附加属性用 `RegisterAttached` | 写成普通 auto-property —— 改了不会失效，布局/绘制停在旧值 |
| 视觉元素绑定一个视图模型属性 | `element.Bind(Element.PropProperty, vm.GetObservable(Vm.PropProperty))`；dispose 归还该优先级层 | 直接 `PropertyChanged +=` 手写回调 —— 失去优先级/清除/多源语义 |
| 覆盖在兄弟窗口上的装饰层 | `AdornerDecorator`/`AdornerLayer`；层为空时自动隐藏 | 自己给层铺不透明底 —— 会盖住被装饰控件 |
| 主题切换后的原生控件 | 进程启动时 `Application.SetColorMode(...)`；干净双色用 `--light` 重启 | 运行期切 `SetColorMode` 期待已建句柄的滚动条/下拉全部重绘 |
| 祖先在子元素上悬停 | 让子控件走 `Control` 的 OnMouseEnter/Leave；原生子控件由 `OnControlAdded` 自动挂回调 | 用子控件自己的 `IsPointerOver` 判断祖先，或依赖 WinForms 的窗口级 MouseLeave |
| 布局属性变化后重排 | 声明 `AffectsMeasure`（或 `AffectsArrange`）；失效从元数据扇出到根 | 在 DP 回调里手写 `Parent?.PerformLayout()`，仍只重排一层 |
| 大量不交互图形/连线 | `VisualHost` + `Visual` 树，一趟绘制 | 每个元素一个 `Control`；HWND 数量和布局成本都上去 |
| 新控件加进主题兜底 | 交互控件加 `ThemeStyles.SurfaceTypes`，容器不加 | 给所有控件塞不透明背景，盖住父级渐变/圆角 |
