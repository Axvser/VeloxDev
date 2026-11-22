# Templates — 模板包

> **路径约定**：本文的路径都相对 `Src/Templates/`。`workflow-<角色>/` 指某个条目目录，完整形式是
> `VeloxDev.<平台>.Templates/working/content/workflow-<角色>/`。引用 Core、适配器、demo、skill 时写全路径。
> 契约（七个视图角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复。

---

## 一、这是什么，不解决什么

`Src/Templates/` 下是**七个 NuGet 包项目**，每个打包一套 `dotnet new` 的 **item template**（条目模板）。四件事一起看：

| 事实 | 依据 |
|---|---|
| 是模板包，不是类库 | 七份 csproj 都有 `<PackageType>Template</PackageType>`（WPF `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj`） |
| 内部一行 C# 都不编译 | 同文件的 `EnableDefaultCompileItems`、`IncludeBuildOutput` 两个属性都是 `false` |
| 与被模板化的代码**没有编译期联系** | 七份 csproj 的 `ItemGroup` 里**零** `PackageReference` / `ProjectReference`，只有 `<None Include=… Pack="true">` |
| 但仍然进解决方案 | `VeloxDev.slnx` 的 `/Templates/` 文件夹列了七个项目（Jalium 那行的路径没有 `working/`，见 §二） |

⇒ **模板产物编译不过，本仓库的任何构建都不会发现**。七家共用 `Src/Test/Harness/verify-workflow-item-templates-all.ps1`
（装包 → 生成七个条目 → 一起编译 → 与镜像比对，并对残留的 `replaces` 占位符报错）；
`verify-workflow-item-templates.ps1` / `verify-jalium-item-templates.ps1` 分别转发到它的 `-Platform WinForms` / `-Platform Jalium`。见 `extension.md` §一。

生成的 `workflow-tree-view/TemplateClass.xaml` 的 `xmlns:behaviors` 声明写着
`xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"` ——
这个程序集模板项目**自己从没引用过**，是**用户**必须自己装的那一个。所以模板里所有的
`behaviors:` 前缀、`VeloxDev.WorkflowSystem` 命名空间引用，都只在**用户的项目里**才可能解析。

**不解决什么**：

- **不是 demo**。`Examples/Workflow/<GUI>*/` 才是能跑的；模板里没有树 ViewModel、没有窗口、没有入口。
- **不是适配器**。七个角色的实现全在 `Src/Adapters/VeloxDev.<GUI>/WorkflowSystem/AttachedBehaviors/`。
- **不是"一套模板七家共享"**。同一角色在七家**结构不同**（§六），不是同一份文件换个后缀。
- **不是新设计**。每个条目文件都是该平台 `Examples/Workflow/<GUI> Trimmed/` 里同名文件的一份改名副本
  （改名后逐行差异很小 —— MAUI tree-view 那处是刻意的 demo/模板分叉，见 §五），
  源头与五类机械改动见 `extension.md` §1.1。

---

## 二、七份包共有的形状与唯一的离群

| 事实 | 值 | 覆盖 |
|---|---|---|
| `TargetFramework` | `netstandard2.0` | 七家全部 |
| `Version` | `11.0.0` | 七家全部 |
| `PackageId` | `VeloxDev.<平台>.Templates` | 七家全部 |
| `sourceName` | `TemplateClass` | 全部条目 |
| `preferNameDirectory` | `false` | 全部条目 |
| `tags.type` | `item` | 全部条目 |
| `.template.config/` 内容 | **恰好两个文件**：`template.json` + `dotnetcli.host.json` | 每个条目各两个 |
| 条目目录名 | 七个平台**同名同集** | 全部条目 |

**Jalium 是唯一的结构离群**，四处都不一样：

| 项 | 其余六家 | Jalium |
|---|---|---|
| csproj 位置 | `working/VeloxDev.<平台>.Templates.csproj` | `VeloxDev.Jalium.Templates.csproj`（项目根，**没有 `working/`**） |
| `Pack` 的 content glob | `content\**\*` | `working\content\**\*`（`VeloxDev.Jalium.Templates.csproj` 的 `<None Include>`） |
| logo 相对路径 | `..\..\..\..\Assets\veloxdev-logo.png` | `..\..\..\Assets\veloxdev-logo.png`（同文件另一条 `None`，少一层） |
| 元数据 | 有 `PackageTags` / `PackageLicenseExpression` / `RepositoryUrl` | 三项全无；多一条 `IncludeSymbols=false` |

⇒ Jalium 是"条目在 `working/content/` 下、csproj 在上一级"；另外六家是"csproj 与 `content/` 同在 `working/` 下"。
两种都自洽，但对不上就是 path 全错。

---

## 三、条目：七个平台 × 七个角色

| 条目目录 | `defaultName` | `shortName` 后缀 |
|---|---|---|
| `workflow-grid-decorator` | `GridDecorator` | `-decorator` |
| `workflow-link-view` | `LinkView` | `-link` |
| `workflow-minimap-overlay` | `MinimapOverlay` | `-minimap` |
| `workflow-node-view` | `NodeView` | `-node` |
| `workflow-slot-view` | `SlotView` | `-slot` |
| `workflow-template-selector` | `TemplateSelector` | `-selector` |
| `workflow-tree-view` | `TreeView` | `-tree` |

- `shortName` 完整形式是 `<平台小写>-v-<后缀>`（`wpf-v-tree`、`jalium-v-decorator`、`winforms-v-minimap`…）。
- `identity` 是 `VeloxDev.<平台>.<角色名>`（如 `VeloxDev.Jalium.WorkflowNodeView`）。
- **`primaryOutputs` 列出的路径恰好等于条目目录里除 `.template.config/` 外的全部文件**：有标记语言的一家两个
  （`TemplateClass.xaml` + `TemplateClass.xaml.cs`；Razor 是 `.razor` + `.razor.cs`），纯代码的一家一个
  （`TemplateClass.cs`）。⇒ **"这个平台有没有标记语言"在包元数据里就能读出来**。

---

## 四、用户拿到之后怎么用

**人面向的用户手册在 `skills/veloxdev-create-workflow/references/templates.md`**（安装、七条命令的完整命令行、
每个条目的全部 CLI 别名与默认值、生成之后还要自己写什么）。
本节只写从 `template.json` 与模板源码能核到、而那一份文档没写（或写得比代码乐观）的部分。

| 从代码能核到的事 | 依据 |
|---|---|
| 用户**必须自己装适配器包**（模板项目零 `PackageReference`） | 见 §一；`workflow-tree-view/TemplateClass.xaml` 里 `xmlns:behaviors` 的 `assembly=VeloxDev.WPF` |
| 七个条目**必须落进同一个命名空间**，机制是 tree-view 用 `xmlns:local` 与 `xmlns:workflowViews` **两个前缀指向同一个** `clr-namespace:TemplateNamespace` | `workflow-tree-view/TemplateClass.xaml` 的这两个 `xmlns`，随后 `NodeTemplate` / `LinkTemplate` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` 都用 `workflowViews:` 取兄弟 |
| `sourceName: "TemplateClass"` 是**全局文本替换的锚**：`-n` 同时改文件名、类名、`x:Class` 和别处的引用 | 所有条目的 `template.json` 的 `sourceName` 都是这个值 |
| `preferNameDirectory: false` ⇒ 产物不建子目录 | 所有条目 |
| 替换是**纯文本**的，所以颜色占位符也会被替换进生成文件的**注释**里 | 见 `skills/veloxdev-create-workflow/references/templates.md` 的同一条 |

---

## 五、跨条目契约：tree-view 按 `defaultName` 引用兄弟

七个条目是一个宿主加六个部件；tree-view 的产物里**写死了另外六条的 `defaultName`**，七家各自用自己语言的写法引用同一批名字：

| 平台 | 引用写法 | 依据 |
|---|---|---|
| WPF | `workflowViews:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`；前缀指向本命名空间 | `workflow-tree-view/TemplateClass.xaml` 的 `NodeTemplate` / `LinkTemplate` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` |
| WinUI | `local:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` | `workflow-tree-view/TemplateClass.xaml` 的 `NodeTemplate` / `LinkTemplate` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` |
| Avalonia | `local:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`（视图模板是 keyed 资源 + 声明选择器，不是隐式 `DataTemplate`） | `workflow-tree-view/TemplateClass.axaml` 的 `NodeTemplate` / `LinkTemplate` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` |
| MAUI | `local:NodeView` / `TemplateSelector` / `GridDecorator` / `LinkView` | `workflow-tree-view/TemplateClass.xaml` 的 `NodeTemplate` / `TemplateSelector` / `GridDecorator` / `LinkView` |
| Razor | 组件标签 `<GridDecorator>` / `<LinkView>` / `<MinimapOverlay>` / `<TemplateSelector>` / `<NodeView>` | `workflow-tree-view/TemplateClass.razor` 的 `GridDecorator` / `Minimap` / `TemplateSelector` / `LinkView` / `NodeView` 组件标签 |
| WinForms | tree 只引用适配器的 `hosting:VisualWorkflowSurface`，**不引用任何兄弟条目**；宿主在 code-behind 把 `CustomTemplateSelector` 装到 `VisualWorkflowSurface.TemplateSelector`，`LinkView` 默认不池化（表面的单个 `VisualWorkflowLinkLayer` 一趟画完） | `workflow-tree-view/TemplateClass.xaml` 与 `.xaml.cs`；`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/VisualWorkflowSurface.cs` 的 `TemplateSelector` |
| Jalium | 标记里按 `defaultName` 引用兄弟：`workflowViews:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`，`xmlns:workflowViews` 指向本命名空间 | `workflow-tree-view/TemplateClass.jalxaml` |

⇒ 除 WinForms 外，用户给某一条传了非默认 `-n`，必须回 tree-view 手写照改；没有工具、测试或编译器提示漏改。WinForms 的 tree 不引用兄弟，改名只需宿主自己改接线。

这一家在"引用兄弟"上还有三种别家没有的形态：

- **WinForms 的 tree-view 不引用兄弟条目**：`workflow-tree-view` 的 XAML 只实例化适配器的
  `VisualWorkflowSurface`；`workflow-template-selector` 由宿主在 code-behind 赋给
  `VisualWorkflowSurface.TemplateSelector`。只生成 tree 也能编译，但需要宿主自己接选择器。
- **WinForms 的 grid / minimap 条目是独立扩展点**：表面的 grid / minimap / link layer 是
  `VisualWorkflowSurface` 自己组合的内部实例；`workflow-grid-decorator` 与 `workflow-minimap-overlay`
  是调色板参考或替换用的派生类，不会被 tree 自动实例化。
- 对照：**WinForms 的可见集由表面写进 helper**（`VisualWorkflowSurface.UpdateViewport` 写
  `tree.GetHelper().Viewport`，宿主必须用虚拟化 helper），模板不自己发散虚拟化。WPF/WinUI/Avalonia/Razor 在标记里绑
  `Helper.VisibleItems`，MAUI 由适配器的 `ViewLinkLayer`（`IsActive=true`、`Handles` 判连线）在入队前挡在主池外，交给共享 overlay 画（见 `adapters/maui.md` §2.2 与 `WorkflowSystem/adapters/maui.md` §二·1）；
  Jalium 现在也在标记里绑 `behaviors:ViewPool.ItemsSource`（`workflow-tree-view/TemplateClass.jalxaml`）—— 绑的是可见集，**不要**改成全量 `Nodes`。
  Razor 的 `Items="Tree.GetHelper().VisibleItems"` 在 `workflow-tree-view/TemplateClass.razor` 的 `TemplateSelector` 上。
  ⚠ **`workflow-tree-view` 条目带「连线的右键菜单」**：**条目**声明在模板里（WPF/Avalonia/WinUI/MAUI/Jalium
  是资源里的 `ContextMenu`，Razor 是 `.razor` 里的按钮，WinForms 是宿主赋
  `VisualWorkflowSurface.LinkMenuBuilder`），而**接线在适配层**。WPF/Avalonia/WinUI/MAUI/Jalium 五家只多一行附着属性
  `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="<资源键>"`，适配器按这个键取菜单、包办订阅 / 定位 / 弹出 /
  开合上报，**模板 code-behind 因此只剩 `InitializeComponent()`**（WPF/Avalonia/WinUI/MAUI 四家 tree-view code-behind 都只剩薄薄一层；Jalium 的 code-behind 另有那个 `CanvasTransform` 镜像属性，见 `WorkflowSystem/adapters/jalium.md` §2.4）。
  Razor 没有附着属性这一层：菜单是 `.razor` 里带内联 `@onclick` 的 `<LinkMenu>` 按钮（见下）。
  传键不传菜单本身：`{StaticResource}` 会在定义它的资源字典之前求值（见 [item-template-specifications.md](../../specifications/item-template-specifications.md) §五）。
  菜单落在 tree-view 而不是 link-view（连线视图不吃指针，弹出需要屏幕坐标）。
  ⚠ **条目是「声明 + 绑定」，不是「声明 + 处理器」**：弹出前把**菜单自己的上下文设成那条连线**
  （WPF/Avalonia 是 `DataContext`；MAUI 是 `BindingContext`；WinUI 是逐条给 `MenuFlyoutItem.DataContext`，
  因为 `MenuFlyout : FlyoutBase : DependencyObject` **没有 `DataContext`**），条目写 `Command="{Binding DeleteCommand}"`
  即成一个新动作。Avalonia 因为资源里没有 `x:DataType`、而 demo 开了编译绑定，必须写 `{ReflectionBinding …}`，不能退回 `Click`。
  Razor 没有绑定那一套，保留内联 `@onclick`；WinForms 的条目由宿主在 code-behind 给 `VisualWorkflowSurface.LinkMenuBuilder`，Jalium 的条目在资源里的 `ContextMenu` 上增删。
  ⚠ **MAUI 的一份菜单有两副面孔**：Windows 上把声明的 `MenuFlyout` 翻成原生 flyout；非 Windows **没有任意点弹出**，
  翻成一层**由适配器搭的浮层**（`WorkflowSurfaceBehavior.EnsureLinkMenuLayer`；不翻转、不出窗口、
  不是 OS 菜单、嵌套项会拍平）。非 Windows 的「请求菜单」手势是**长按**（`LongPressDelay = 500` ms，
  位移超过 `LongPressMoveSlop = 8` 设备无关单位即取消），由链接层翻译成一次合成右键，交给输入路由。
  ⚠ **菜单不会活得比它指着的那条线久**：判据是树既有的事件 —— 「菜单开着、那条线却离开树」时
  树发 `LinkRemoved`，适配器据此收自己那份菜单；**接线在适配层，模板不参与**（`WorkflowSystem/architecture.md`）。
  ⚠ **模板里的注释一律英文**（见 [code-comment-specifications.md](../../specifications/code-comment-specifications.md) §五）：
  只标扩展点、一行说清，函数体注释也算在内。
  ⚠ **MAUI 的 Trimmed demo 与模板故意分叉**：demo 是**每线一视图**
  （`Demo/Controls/Workflow/LinkView.xaml(.cs)`，节点与连线共用一个池、选择器给 `LinkTemplate`），
  而 `workflow-link-view` / `workflow-tree-view` 两个模板**还是旧的 overlay 形态**。
  做模板镜像时，同步改这条注、§六 轴 1 的绑定说明、§八 里「MAUI 模板没有本地连线视图」那句。
- **Jalium 的 tree-view 在标记里引用五个兄弟条目**：`NodeView` / `LinkView` / `TemplateSelector` /
  `GridDecorator` / `MinimapOverlay`（`workflow-tree-view/TemplateClass.jalxaml` 的资源与正文），所以
  "少生成一条兄弟就编译不过"在这家覆盖这五条。见 `adapters/jalium.md`。

---

## 六、平台族：同一条目在七家的结构差异

三条轴各自分族 —— **跨平台抄模板前先确认两边同族**。

### 轴 1 · 标尺默认厚度：28 与 36

| 值 | 平台 | 依据 |
| --- | --- | --- |
| **28** | WPF、Avalonia、WinUI、MAUI、Razor | WPF / Avalonia / WinUI / MAUI 各自 `workflow-grid-decorator/TemplateClass.cs` 的 `RulerThicknessProperty`；Razor 是 `workflow-grid-decorator/TemplateClass.razor.cs` 的 `RulerThickness`，tree-view 里又写死一份 `workflow-tree-view/TemplateClass.razor` 的 `RulerThickness="28"` |
| **36** | Jalium | 定义在模板自己的 grid-decorator 里；没有说明（模板只写「默认标尺带厚度」） |

⇒ **WinForms 没有标尺**：视觉后端的 `VisualWorkflowGridDecorator` 只画网格与轴，`workflow-grid-decorator` 条目也没有 `RulerThickness`。

⇒ **这个数字在别处被引用，且引用方式分两派**：

- **绑定式**（改厚度自动跟随）：WPF / Avalonia / WinUI / MAUI 在 tree-view 里把 `TranslateTransform` 的 X/Y
  绑到 `PART_GridDecorator` 的 `RulerThickness`（WPF `workflow-tree-view/TemplateClass.xaml`、Avalonia `workflow-tree-view/TemplateClass.axaml`、WinUI `workflow-tree-view/TemplateClass.xaml`、MAUI `workflow-tree-view/TemplateClass.xaml` 的 `Canvas.RenderTransform`）。
- **复制式**（改厚度必须手改）：只剩 **Razor** —— tree-view 里写死 `RulerThickness="28"`
  （`workflow-tree-view/TemplateClass.razor`）。
- **单一来源**：**Jalium** 的 36 只定义在模板的 grid-decorator 一处（`RulerBand => RulerThickness`），模板其余文件零副本。

### 轴 2 · 连线用什么方式画：四族

七家的 `workflow-link-view` 多数 XAML 是空壳，几何在代码里。

| 族 | 平台 | 依据 |
|---|---|---|
| **立即模式**：继承一个能覆写绘制入口的元素，XAML 只是空壳 | WPF、Avalonia、Jalium、WinForms | WPF `workflow-link-view/TemplateClass.xaml` 全文是一个空 `<UserControl>`，几何全在 `.xaml.cs` 的 `OnRender`；Avalonia 同理（`TemplateClass.axaml` 是空 `<Control>`，`Render` 在 code-behind）；Jalium 的模板也是空 `<UserControl>`，几何在 `.jalxaml.cs` 里算、自盒化（`WorkflowLinkBounds.Apply`）并 `PublishCurve`；WinForms 的**默认路径也是单层**：`VisualWorkflowLinkLayer` 在画布的 `OnRender` 里画所有可见线并发布曲线；`workflow-link-view` 是 per-link 可选路径，模板派生 `VisualElement`、在 `OnRender` 里用 `LinkCurve` 画自己的曲线并 `PublishCurve`，XAML 只是空自根 |
| **保留式几何在代码里构造**：XAML 是空壳，`Path` + `PathGeometry` 在构造函数里 new 出来 | WinUI | `workflow-link-view/TemplateClass.xaml` 几乎为空（`Clip="{x:Null}"` 是全部内容）；`TemplateClass.xaml.cs` 的 `_path` / `_pathGeometry` / `_pathFigure` 是 `Path` / `PathGeometry` / `PathFigure` 字段，构造函数里 new 并 `Children.Add`。原因是这一家**没有公共 `OnRender`**，理由见 `memory/modules/WorkflowSystem/adapters/winui.md` §二·L2，此处不抄 |
| **标记语言里的元素 + 代码给几何字符串** | Razor | `workflow-link-view/TemplateClass.razor` 的 `<path d=…>`（虚线的 `stroke-dasharray="@dash"`，`dash` 在 code-behind 里按 `IsVirtual` 取 `"6 4"`）；`data-veloxdev-link-curve="1"` 是给适配器缩放 JS 认的标记，不是样式 —— 见 `adapters/razor.md` §2 |
| **复用适配器的视口级图层** | MAUI | `workflow-link-view/TemplateClass.xaml` 直接放 `behaviors:WorkflowLinkOverlay`；文件头的注释写明取舍：**每条线一个 `GraphicsView` 会在深缩放下超出 Win2D 纹理上限并静默消失**，所以一个表面只放**一个** link 层 |

⇒ **WPF / WinUI 那一对容易看错**：WPF 的 tree-view 给 `LinkView` 绑了 `Width/Height` 到
`PART_Canvas` 的 `ActualWidth/ActualHeight`（`workflow-tree-view/TemplateClass.xaml` 的 `LinkTemplate`），
WinUI 的 tree-view **故意不绑**并在注释里写明理由（`workflow-tree-view/TemplateClass.xaml` 的 `LinkTemplate` 注释：
`Do not bind Width/Height here; LinkView sizes its own box.`）。
两者最终都在代码里画，但"盒子谁给"是相反的。

### 轴 3 · minimap 是薄壳还是自带实现

七家全是薄壳：继承/配置框架或适配器给的实现，只设颜色，模板只给调色板（各家 `workflow-minimap-overlay/TemplateClass.*`）。
WinForms 的小地图实现在视觉后端（`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/VisualWorkflowMinimap.cs`），模板派生它并设四色；
Jalium 的薄壳设四个颜色符号（`workflow-minimap-overlay/TemplateClass.cs`）。

**WinForms 的七项都走「视觉基类 + 薄派生」**：tree 是 `UserControl` + `VisualWorkflowSurface`，
node / slot / link / grid / minimap 分别派生 `VisualWorkflowNodeView` / `VisualWorkflowSlotView` /
`VisualElement` / `VisualWorkflowGridDecorator` / `VisualWorkflowMinimap`，selector 派生 `VisualTemplateSelector`；
调度与平台机制在适配器，调色板、卡片样子与选择器策略在模板。

`node-view` 收掉的只有机制（绑定/定位/折叠/反射读名字），**卡片长什么样是用户的设计** —— Full demo 的卡片视觉仍留在模板/宿主。

**Jalium 是标记驱动（与 WPF 同形）。** 适配器不提供可继承基类，角色落成附着行为
（见 [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一），模板是四个 `.jalxaml` 条目
（tree / node / slot / link）+ 三个纯 `.cs`（grid-decorator / minimap-overlay / template-selector）。
校验：`Src/Test/Harness/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict`。见 `extension.md` §4.1。

---

## 七、静默失败清单

### 7.1 一批"命令行收得下、什么也不替换"的 symbol

`template.json` 里的 symbol 若 `type: parameter` 而**没有 `replaces`**，`dotnet new --help` 会把它列出来、
命令行能传、**不报错、也不替换任何文本**。分布见下表；**Jalium 没有**。

| 平台 | 条目 | 空转的 symbol |
| --- | --- | --- |
| WPF | `workflow-slot-view` | `slotBorderColor` |
| WinUI | `workflow-slot-view` | `slotBorderColor` |
| Avalonia | `workflow-slot-view` | `slotColor`、`slotBorderColor` |
| MAUI | `workflow-slot-view` | `slotPath` |
| WinForms | `workflow-slot-view` | `slotPath` |
| WinForms | `workflow-link-view` | `surfaceBackground` |
| WinForms | `workflow-grid-decorator` | `rulerBackground`、`rulerTickColor`、`rulerLabelColor`、`rulerDividerColor` |
| Razor | `workflow-slot-view` | `slotBackground` |
| Razor | `workflow-grid-decorator` | `gridBackground`、`minorGridColor`、`majorGridColor` |
| Razor | `workflow-tree-view` | `surfaceBorderBrush`、`surfaceBorderThickness`、`surfaceCornerRadius` |

**这批参数都在自己的 `description` 里自陈**：`Accepted for cross-GUI CLI parity; <这家为什么没有消费者>`。
⚠ **这是刻意的跨 GUI 契约，不是漏接**（[skills/veloxdev-create-workflow/references/templates.md](../../../skills/veloxdev-create-workflow/references/templates.md)
「Style parameters」一节）：七家接受**同一套参数名、同一套默认值**，命令行因此能在 GUI 之间原样搬。
删参数会让 `dotnet new` 报未知参数；补的是**说明**，不是删参数，也不是硬造一个样式点。

⚠ **`slotPath` 特别容易骗人**：七个平台的 `defaultValue` 是**逐字节相同**的一段 SVG 路径
（viewBox 1024×1024 的圆环图标），但**只有六个真的替换它** ——
MAUI 声明了却没有 `replaces`。⇒ 在 MAUI 上传 `--slotPath` 什么也不会发生，
而图标看起来"是那段路径"，会让人以为换成功了。

⚠ **人面向文档只举了子集**（Razor 的树边框/圆角、MAUI 的 `slotPath`、WPF/WinUI/Avalonia 的 `slotBorderColor`、
Avalonia 的 `slotColor`）；上表是跨平台的完整枚举。

### 7.2 多份 csproj 里有一条匹配不到任何文件的 Pack glob

```xml
<None Include="..\..\skills\veloxdev-workflow-item-templates\references\**\*" Pack="true" PackagePath="references" Visible="false" />
```

WPF 在 `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj` 的那条 `None Include`，
Avalonia / WinUI / MAUI / Razor / WinForms 同位置同内容（`git grep` 多家都命中同一条 `None`）。

⇒ **这条路径本身是错的**：从 `working/` 上两级落在 `Src/Templates/`，所以它指的是
`Src/Templates/skills/veloxdev-workflow-item-templates/references/**\*` —— 而 `Src/Templates/skills`
**整个目录都不存在**。要够到仓库根的 `skills/` 得写**四级**（`..\..\..\..\skills\…`）。
⇒ **它匹配 0 个文件**：仓库根 `skills/` 下没有 `veloxdev-workflow-item-templates/` 这个子目录。
**包内不会有 `references/` 目录**，而 `dotnet pack` 对空 glob 不报错。**Jalium 没有这一行**（它的 csproj 只有 `working\content\**\*` 与 logo 两条 `None`）。

### 7.3 Avalonia 的 tree-view 生成出来就编译不过 —— 而它自己写明了

`workflow-tree-view/TemplateClass.axaml` 文件头的注释：
`compiled bindings need a concrete x:DataType as their starting type … until you do, the build fails HERE on vm:TreeViewModel`；
`xmlns:vm="using:TemplateNamespace"` 也在文件头，正文里是 `x:DataType="vm:TreeViewModel"`。
`vm:TreeViewModel` **不是**模板生成的任何一个类型，它是**用户自己的树 ViewModel**。

⇒ 七家里只有这一家把「生成即失败」写进产物。这是**设计**（逼用户填对类型），不是缺陷。

### 7.4 插槽状态着色：Avalonia 完全没有

`SlotState`（决定插槽在连线手势下变紫/番茄红/青柠）在七家的实现方式：

| 平台 | 方式 | 依据 |
|---|---|---|
| WPF | slot-view 有 `SlotStateProperty` DP，node-view 两侧都绑 | `workflow-slot-view/TemplateClass.xaml.cs` 的 `SlotStateProperty`；`workflow-node-view/TemplateClass.xaml` 的 `NodeTemplate` 输入/输出两行 |
| WinUI | 同上，DP 在 slot-view，node-view 两侧都绑 | `workflow-slot-view/TemplateClass.xaml.cs` 的 `SlotStateProperty` / `UpdateForeground`；`workflow-node-view/TemplateClass.xaml` 的输入/输出两行 |
| MAUI | `BindableProperty` + 绑 `Slot.State` / `State` | `workflow-slot-view/TemplateClass.xaml.cs` 的 `SlotStateProperty`；`workflow-node-view/TemplateClass.xaml` 的输入/输出两行 |
| Jalium | 模板 slot-view 自己在 code-behind 里按 `SlotState` 算 `Foreground`；node-view 绑 `SlotState="{Binding State}"` | `workflow-slot-view/TemplateClass.jalxaml.cs` 的 `UpdateForeground`；`workflow-node-view/TemplateClass.jalxaml` |
| Razor | 不绑，slot-view 在 C# 里算 | `workflow-slot-view/TemplateClass.razor.cs` 的 `FillColor` / `SlotStateColor` |
| WinForms | 模板 slot-view 在 `OnRender` 里按 `SlotState` 算填充（`FillFor`）；基类 `VisualWorkflowSlotView.OnRender` 是相同的默认画法 | `workflow-slot-view/TemplateClass.xaml.cs` 的 `FillFor`；`Src/Adapters/VeloxDev.WinForms/WorkflowSystem/VisualHosting/VisualWorkflowSlotView.cs` |
| **Avalonia** | **没有** —— slot-view 里搜 `SlotState` 零命中 | `workflow-slot-view/TemplateClass.axaml` 只把 `<Path Fill>` 绑到 `$parent[UserControl].Foreground`，而模板里没有任何地方写 `Foreground` |

⇒ 在 Avalonia 上，插槽**永远不会变色**，而且不会报错。要修就得给 slot-view 加一个状态属性再绑上去。

### 7.5 渲染就绪门：WPF / Jalium / Razor 三家写了

| 有门 | 写法 | 依据 |
| --- | --- | --- |
| WPF | `link.IsRenderReady()` | `workflow-link-view/TemplateClass.xaml.cs` 的 `OnRender` |
| Jalium | `!link.IsRenderReady()` | `Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-link-view/TemplateClass.jalxaml.cs`（`Refresh`） |
| Razor | `WorkflowSlotUpdateGate.IsLinkRenderReady(link)` | `workflow-link-view/TemplateClass.razor.cs` 的 `BuildCurve` |

| 无门 | 它们各自有的东西 |
| --- | --- |
| Avalonia | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.axaml.cs` 只有 `CanRender`） |
| WinUI | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.xaml.cs` 只有 `CanRender`） |
| MAUI | 无门 |
| WinForms | 无门；模板 link-view 自己用 `IsFinite` 挡 NaN/Infinity，然后才 `PublishCurve` |

⇒ `WorkflowSystem/extension.md` 那条"连线视图首行过渲染就绪门"的契约，**模板侧四家落地**。
另有两条 `double.IsNaN` 守卫：Razor（`workflow-link-view/TemplateClass.razor.cs` 的 `BuildCurve`）与 Jalium 的模板
（`workflow-link-view/TemplateClass.jalxaml.cs` 的 `Refresh`，不加会抛 `ArgumentException` 退出）。

### 7.6 WinForms 的 tree 不再依赖兄弟条目

`workflow-tree-view` 只引用适配器的 `VisualWorkflowSurface`；`NodeView` / `CustomTemplateSelector` 由宿主在
code-behind 接进 `TemplateSelector`。⇒ `winforms-v-tree` 可以单独编译，代价是宿主不接选择器时只有默认卡片。
选择器返回的元素必须是 `VisualWorkflowNodeView`，否则表面会落到 `NodeViewFactory` / 默认卡。

---

## 八、调色板与连线：模板的默认值 = Trimmed 那一套，非 Trimmed 的 demo 分四套

**模板默认配色是 `template.json` symbol 的 `defaultValue`**，七家同名 symbol 取值一致：

| 用途 | 值 |
|---|---|
| 画布底 / 边框 | `#1E1E1E` / `#33FFFFFF`（1px，圆角 3） |
| 次网格 / 主网格 / 轴 / 刻度 / 分隔 | `#2A2D2E` / `#3A3D40` / `#4D4D4D` / `#555555` / `#3A3D40` |
| 标尺底 / 标尺字 | `#C8252526` / `#888888` |
| 节点卡 底 / 字 / 边 | `#DDFFFFFF` / `#DD1E1E1E` / `#331E1E1E` |
| 槽位 待机 / 描边 / 三态 | `#DD1E1E1E` / `#FFFFFFFF` / `#FF6347`·`#32CD32`·`#EE82EE` |
| 连线 | `#DDFFFFFF`，粗 2 |
| 小地图四色 | `#D2141922` / `#DC94A3B8` / `#DC38BDF8` / `#F0FFFFFF` |

**七家 Trimmed demo 逐项等于上表**，只有一处刻意偏差：Jalium 标尺四色自成一档。

**七家非 Trimmed demo 却是四套**：Avalonia / WPF / WinUI / MAUI 共用一套「青蓝 v1」（画布 `#141922`、轴 `#38BDF8`、连线 `Colors.Cyan` 或 `#22D3EE`、节点卡逐类型配色），WinForms、Blazor、Jalium 各又一套。

对齐的只是画布层：那四家的画布底 / 网格 / 标尺 / 连线 / 边框已换成上表的值（各家 `WorkflowGridDecorator` 的八个静态字段 + 连线默认色 + `PART_SurfaceBorder`）。**节点卡的逐类型配色刻意保留** —— Controller 深灰、Timer/Python 蓝、Enum 紫是 feature demo 要展示的东西。

**改配色的落点**：网格集中在各家的 `WorkflowGridDecorator`（WinForms 在模板的 `workflow-grid-decorator/TemplateClass.cs` 与 `VisualWorkflowGridDecorator`；Razor 在适配器 `WorkflowGridDecorator.razor.cs` 加 surface）。连线的默认色一般在各家自己的连线视图里，**MAUI 与 WinForms 是例外：模板没有本地局部连线视图，默认是适配器的单层**（MAUI `WorkflowLinkOverlay.cs`、WinForms `VisualWorkflowLinkLayer.LineColor`）；WinForms 的 tree 在 `workflow-tree-view/TemplateClass.xaml.cs` 里把 `LinkLayer.LineColor` 设成 `#DDFFFFFF`，`workflow-link-view` 的调色板只对 per-link opt-in 生效。

**连线：七家都没有箭头。** 那套箭头（长 12、宽 8，`!IsVirtual` 时画）七家都不画。
模板与各家 Trimmed demo 现在都是**一条平线 + 悬停高亮**：高亮时绕线画一圈白色 halo
（如 Avalonia Trimmed `Demo/Demo/Views/Workflow/LinkView.axaml.cs` 里 `OnPointerEntered`/`OnPointerExited` 画的那圈 halo：`(color, 0.25)`、`thickness + 6`）。
**动的光带（旧称「流光」）只在部分非 Trimmed demo 里**，例如 Avalonia 的
`Examples/Workflow/Avalonia/Demo/Views/Workflow/PolylineCurveView.axaml.cs`（沿曲线裁剪几何做「彗星拖尾」，
中文注释在 `TailFraction` 声明处）—— 它**不在模板、也不在 Trimmed demo 里**。

**`BezierCurveView` 全仓只剩 Avalonia 非 Trimmed demo 的一个**（被 `WorkflowView.axaml` 引用，不是死代码）。

---

## 九、视图层的三个坑：端口被裁一半、删 axaml 会连带删掉交互、`Slot.State` 静默失败

这三个坑都不报错：编译通过、端口照样画出来，只是**半边不见了**、**再也拖不动**、或**状态永远不变**。

### 9.1 端口有一半骑在卡外，所以它上方不能有裁剪面

端口宽 32 设计单位（枚举列表里 24）、外溢一半骑在卡边上。任何一个 `ClipToBounds="True"` 的祖先都会把它切掉外侧那一半。

已经踩到的两处，**成因不同、修法也不同**：

| 卡 | 成因 | 修法 |
|---|---|---|
| `Examples/Workflow/Avalonia/Demo/Views/Workflow/ControllerView.axaml` | 端口**没有 `ZIndex`**，被卡面盖掉内侧一半。`TimerNodeView.axaml` 一直把端口包在 `ZIndex="6"` 的层里，Controller 漏了 | 补 `ZIndex="6"` |
| `…/EnumSelectorNodeView.axaml` | 输入端口若放在 `<Border Grid.Row="1" ClipToBounds="True">` **里面**就会被裁 —— 那个 Border 的裁剪是为「滚动内容不溢出圆角」而设的，**不能去掉** | 端口要挪成该 Border 的**兄弟**，直接挂根 Grid（与 Controller 同形） |

**Python 走的是第三条路**：它用「ScrollViewer 视口外扩 N / ItemsControl 内容内缩 N / 端口外溢 N」把端口留在滚动视口内。**三个 N 必须相等，且等于端口尺寸的一半** —— 端口从 16 换到 24 时 N 必须从 8 一起改到 12。这条约束在 `…/PythonNodeView.axaml` 的两条端口带里各有一份（左输入、右输出），改一处不改另一处就是对不齐。

**核查方法**：沿端口往上数祖先，只要有一个 `ClipToBounds="True"` 就是错的。`EnumSelectorNodeView.axaml` 的输出列表行仍在那层裁剪里，但行内容内缩 14、端口只外溢 12 → 还剩 2 单位在卡内，**够用**。

### 9.2 删掉 `SlotView.axaml` 会连带删掉交互，而且不报错

`Avalonia/Demo/Views/Workflow/` 下的 `SlotView` 原是一个 `UserControl` + 一个 `.axaml`，那个 axaml 的根上挂着**两样性命攸关的东西**：

- `behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"` —— 它给控件挂 `PointerPressed` / `PointerReleased`（`Src/Adapters/VeloxDev.Avalonia/WorkflowSystem/AttachedBehaviors/WorkflowSlotConnectionBehavior.cs` 的 `OnIsEnabledChanged`）。没有它，**端口拖不出连线**。
- `Background="#01000000"` —— 那是端口的**命中测试面**。全部改自绘、删掉 axaml 之后它一起没了，指针事件到不了控件。

两样都不报错：端口照样画得好好的，只是静默失去交互。两者现在都写在 `SlotView` 的**构造函数**里（外加一个近透明的子 `Border` 作命中面），卡片忘了也不会再丢。

⇒ **把标记语言控件改成自绘之前，先列出被删掉的那个根元素上都挂了什么。** 附着属性、`Background`、`x:Name`（会被别处按名字找）都算。

### 9.3 输出槽的 `SlotState` 写成 `Slot.State` 会静默失败

输出槽在 `DataTemplate` 里（`ItemsSource="{Binding OutputSlots.Items}"`），那个 `DataTemplate` 的 DataContext 是**包装项**（成员是 `Slot` 与 `Name`，模板里两个都用到过）。所以 `DataContext="{Binding Slot}"` / `BindingContext="{Binding Slot}"` 是对的 —— 槽视图的上下文就该是槽自己。

错的是**同一个元素上**再写 `SlotState="{Binding Slot.State}"`：`DataContext` 一被设成槽 VM，这一行就改在槽 VM 里求值，而 `ISlotComponent` 的成员只有 `Targets` / `Sources` / `Parent` / `Channel` / `State` / `Anchor`（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Components/ISlotComponent.cs`），**没有 `Slot`** ⇒ 绑定失败、槽永远拿不到自己的状态，而**不报错**：编译通过、槽照常画出来。

正确写法是 `SlotState="{Binding State}"`（与输入槽那行 `{Binding State}` 同形 —— 输入槽的上下文本来就是槽）。

三家模板的 `workflow-node-view/TemplateClass.xaml` 输出槽那一行都写成 `SlotState="{Binding State}"`（MAUI / WPF / WinUI），与三个 Trimmed demo 的 `NodeView` 一致；**改模板，不单独改 demo**，否则两边分叉。Avalonia 模板不设 `SlotState`（`TemplateClass.axaml` 的输入、输出两行只给 DataContext），没有这个问题。

---

## 十、我要改 X → 打开哪个文件

| 我要改 | 先打开 |
|---|---|
| 某平台某个部件生成后长什么样 | 那一条目目录下的 `TemplateClass.*` |
| 外壳结构（`PART_*` 嵌套、绑定关系） | 该平台的 `workflow-tree-view/TemplateClass.*` |
| 标尺厚度 | 该平台 `workflow-grid-decorator` 的 `RulerThickness`（Jalium 在模板里：`workflow-grid-decorator/TemplateClass.cs`）。**加** Razor `workflow-tree-view/TemplateClass.razor` 的 `RulerThickness="28"`；WinForms 的视觉后端没有标尺 |
| 条目对外的 CLI 参数 | 该条目 `.template.config/template.json` 的 `symbols`（**记得 `replaces`**，否则就是 §7.1）+ `dotnetcli.host.json` 的 `symbolInfo` |
| 条目名 / 命令名 / 默认类名 | `.template.config/template.json` 的 `identity` / `shortName` / `defaultName`，**并回头查 tree-view 里对它的引用**（§五） |
| 新增一个平台 | `extension.md` §三 |
