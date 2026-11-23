# VeloxDev.WinForms.Xaml — 扩展路径

> 回答「加一个视觉控件 / 属性 / 样式能力，动哪几处，按什么顺序」。架构见 [architecture.md](architecture.md)。

## 一、新增一个视觉控件

1. 在 `Src/Adapters/VeloxDev.WinForms/Xaml/Visuals/Controls/` 新建类，继承 `VisualElement` 或合适的派生基类（单内容继承 `ContentControl`，列表继承 `ItemsControl`）。
2. 布局/绘制属性用 `DependencyProperty.Register`，按需标记 `AffectsMeasure`/`AffectsArrange`/`AffectsRender`；纯配置属性把 `AffectsRender` 设 `false`。
3. 覆写 `MeasureOverride`/`ArrangeOverride`/`OnRender`；绘制走 GDI+（`VisualText.Draw`），不要用 `TextRenderer`。
4. 子元素用 `Add`/`Children`，不要创建 HWND；原生岛只能经 `NativeControlHost`。
5. 生成器无需登记：不带前缀的类型按 `Visuals.Controls` → `Visuals.Controls.Shapes` → `Visuals` → 共享原语反射查找。
6. 测试放 `Src/Test/Unit/WinFormsXaml/VeloxDev.WinForms.Xaml.Test/`；在 `Examples/XamlGallery` 的卡片条目里出现。

联动：`XamlRuntime.ConvertValue`（新字面量类型）、`Theme` 默认值、文档、Gallery。

## 二、新增一个依赖属性

- 生成器路径：`[XamlProperty(DefaultValue = ...)] public partial T X { get; set; }`。非可空引用类型必须写 `DefaultValue` 或改成可空，否则 `VELOX_XAMLP005`。
- 手写路径：`DependencyProperty.Register(nameof(X), typeof(T), typeof(Owner), new PropertyMetadata(default) { AffectsMeasure/AffectsArrange/AffectsRender = ... })` + CLR 包装，包装里 `GetValue`/`SetValue`。
- 附加属性：`DependencyProperty.RegisterAttached` + 静态 `GetX`/`SetX`；适配器 owner 在 XAML 里不写前缀（`Grid.Row`），自己的 owner 写 `local:Owner.X`。
- 视觉属性值存 `IWinformsObject` 的优先级层；写 `SetValue(..., BindingPriority.Animation)` 可让过渡帧不被本地值覆盖。

## 三、新增一个可插拔类型

- 值转换器：实现 `IValueConverter`（或 `IParameterizedValueConverter`），在绑定上引用。
- 模板选择器：继承 `VisualTemplateSelector`，`SelectTemplate(object?)` 返回 `VisualElement?`；XAML 里用空元素对象资源 `<local:Selector x:Key="..." />`。
- 触发器动作：内置 `TransitionAction`（单段）、`KeyFrameAnimation`（关键帧链）、`TriggerActionGroup`（按顺序）；自定义动作继承 `VisualTriggerAction` 并覆写 `Invoke(VisualElement)`，在 `<Trigger.EnterAction>`/`<Trigger.ExitAction>` 里以元素名引用。模板动作与 setter 用同一个 `TargetName` 部件。
- 命令：需要 `ICommand` 的交互控件继承 `ButtonBase`（`Button` 系已如此），在激活路径调用 `ExecuteCommand()`；`CanExecute` 拒绝时 `UpdateCommandEnabled` 负责禁用/恢复。

## 四、视觉树与效果

- 静态装饰、大量图形/曲线用 `VisualHost` + `Visual` 树，不要给每个元素建控件。
- 新视觉类型继承 `Visual`，实现 `RenderContent`，按需覆写 `HitTestContent`；子项用 `ContainerVisual.Add`。`Bounds` 是父坐标，内容画在 `0..Width/Height`。
- 效果实现 `VisualEffect`：`Padding` 声明额外空间，`Render` 在源位图上合成；组透明度与效果由 `Visual` 统一走临时位图。
- 改树后调 `InvalidateVisual()`；命中用 `HitTestVisual(Point)`。
- **不要**把 HWND 控件塞进视觉树：两者没有共同的输入、焦点、无障碍管线。原生内容只经 `NativeControlHost` 作矩形岛。

## 五、样式、模板、资源与弹层

- 资源：`<Root.Resources>`（无前缀）放标量、空元素对象、`<Style>`、`<ControlTemplate>`、keyed `<DataTemplate>`。`StaticResource` 构造期解析，**键必须先声明**；查找顺序是元素 → 祖先 → `VisualHost.Resources`。
- 工作流 tree 的选择器：tree 资源里声明 `<local:CustomTemplateSelector x:Key="WorkflowTemplateSelector" NodeTemplate="{StaticResource NodeTemplate}" ... />`；`WorkflowSurfaceBehavior.VisualTemplateSelectorKey` 默认按这个键从 tree 根资源解析。
- 样式：`<Style TargetType="Button">` 用 `<Style.Setters>`/`<Style.Triggers>`；有 `x:Key` 走 `Style="{StaticResource}"`，无键按 `TargetType` 隐式。
- 模板：`<ControlTemplate TargetType="ContentControl">` 必须恰好一个视觉根；`x:Name` 是部件名，`ContentPresenter` 显示 `Content`。`TemplateBinding` 绑 `TemplatedParent`；模板触发器写 `Template` 层，`TargetName` 指部件。
- 节点模板行为：根挂 `behaviors:WorkflowSlotLayoutBehavior`（`IsEnabled` / `SlotNames` / `SlotEnumeratorNames` / `CoordinateHostName`），标题行挂 `behaviors:WorkflowNodeDragBehavior`（`IsEnabled` / `CoordinateHostName`），插槽根挂 `behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"`。槽名是逗号分隔的 `x:Name`；没挂行为或没写 `IsEnabled` 就不测量、不拖动、不连接（没有部件名回退）。
- 视图物化：`PART_Canvas` 上挂 `behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"` 与 `behaviors:ViewPool.TemplateSelector`（或单个 `ItemTemplate`）；池给每个 item 建一个视觉元素并设 `DataContext`，定位与绘制全在模板里。`WorkflowSurfaceBehavior` 在 `PART_Canvas` 尚未被绑定时替你把 `VisibleItems` 与 tree 的选择器接上。
- 弹层：`ComboBox` 直接可用；自定义 `VisualPopup` 的 `PlacementTarget` 在 code-behind 赋值（视觉绑定没有 `ElementName`）。弹层内容脱离根树，只能看到自己的资源与 `VisualHost.Resources`。
- 自定义 `Visual` 改变绘制内容后调 `InvalidateCache()`；走依赖属性的元素由 `InvalidateVisual` 自动标脏最近的 `CacheAsBitmap` 祖先。
- `DockPanel` 顶部子项要显式高度：没有 `RowDefinitions` 的 `Grid` 默认一行星号，会吃掉全部可用高度。

## 六、官方做法 vs 看起来能编译的捷径

| 想做的事 | 官方做法 | 错的捷径 |
|---|---|---|
| 用适配器类型 | 直接写 `Foo`（不带前缀） | 写 `native:Foo` —— `native:` 被拒，视觉父级只收 `VisualElement` |
| 用原生 WinForms 控件 | 用 `NativeControlHost` 单岛，保持矩形轴对齐 | 把原生控件塞进需要 Transform/Opacity/Clip 的视觉子树 —— airspace |
| 新增会触发重排/重绘的属性 | `DependencyProperty.Register(..., PropertyMetadata { AffectsMeasure/AffectsArrange/AffectsRender = ... })` + CLR 包装 | 写成普通 auto-property —— 改了不失效，停在旧值 |
| 绑定视图模型属性 | `element.SetBinding(Element.PropProperty, new Binding("Name") { Source = vm })`；流式用 `Bind` | 直接 `PropertyChanged +=` 手写回调 —— 失去优先级/清除/多源语义 |
| 绑定 `DataContext` 路径 | `element.DataContext = vm;` + `new Binding("Name")` | 依赖 `ElementName` —— 视觉后端没有 `NameScope` 解析 |
| 给子树加隐式样式 | `root.Styles.Add(new VisualStyle { TargetType = typeof(TextBlock), ... })`；本地值仍优先 | 把颜色写进每个元素 —— 改主题只能重建整棵树 |
| 写视觉 XAML | 根与子元素不带前缀；根派生视觉类型 | 根派生 HWND 控件 —— 子元素不会进视觉树 |
| 视觉 XAML 里用资源/样式/模板 | `<Root.Resources>` 声明 `<Style>`/`<ControlTemplate>`/keyed `<DataTemplate>`，再 `{StaticResource}` | 写 `{DynamicResource}` —— 报诊断 |
| 视觉模板里取命名部件 | `x:Name` + `GetTemplateChild<T>`，`TemplateBinding` 取 `TemplatedParent` | 把模板部件当字段 —— 每实例作用域不同 |
| 下拉/菜单 | `ComboBox`，或 `VisualPopup` 挂进覆盖层 | 把弹层放进 `ScrollViewer`/`Border` 期待它浮起来 —— 会被祖先 `Clip` 裁掉 |
| 主题切换后的原生岛 | 进程启动时 `Application.SetColorMode(...)`；双色截图用 `--light` 重启 | 运行期切 `SetColorMode` 期待已建句柄的岛全部重绘 |
| 大量不交互图形/连线 | `VisualHost` + `Visual` 树，一趟绘制 | 每个元素一个控件；HWND 数量和布局成本都上去 |
