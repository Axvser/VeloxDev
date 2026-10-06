# Templates — 模板包

> **路径约定**：本文的路径都相对 `Src/Templates/`。`workflow-<角色>/` 指某个条目目录，完整形式是
> `VeloxDev.<平台>.Templates/working/content/workflow-<角色>/`。引用 Core、适配器、demo、skill 时写全路径。
> 契约（七个视图角色、附着属性、注册位置）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复。

---

## 一、这是什么，不解决什么

`Src/Templates/` 下是**七个 NuGet 包项目**，每个打包一套 `dotnet new` 的 **item template**（条目模板）。
单独读一份 `TemplateClass.cs` 得不出这个结论 —— 它看起来就是普通控件源码。四件事要一起看：

| 事实 | 依据 |
|---|---|
| 是模板包，不是类库 | 七份 csproj 都有 `<PackageType>Template</PackageType>`（WPF `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj:11`） |
| 内部一行 C# 都不编译 | 同文件 `EnableDefaultCompileItems=false`（`:18`）、`IncludeBuildOutput=false`（`:16`） |
| 与被模板化的代码**没有编译期联系** | 七份 csproj 的 `ItemGroup` 里**零** `PackageReference` / `ProjectReference`，只有 `<None Include=… Pack="true">`（`:24-28`） |
| 但仍然进解决方案 | `VeloxDev.slnx:168-175` 的 `/Templates/` 文件夹列了七个项目（Jalium 那行的路径没有 `working/`，见 §二） |

⇒ 由此得出第一件必须记住的事：**模板产物编译不过，本仓库的任何构建都不会发现**。
（2026-10-04 起七家共用 `Src/Verification/verify-workflow-item-templates-all.ps1`；装包 → 生成七个条目 → 一起编译 →
与镜像比对，并对残留的 `replaces` 占位符当场报错。旧的两条 `verify-workflow-item-templates.ps1` /
`verify-jalium-item-templates.ps1` 现在只是转发到它的 `-Platform WinForms` / `-Platform Jalium`。见 `extension.md` §一。）

生成的 `workflow-tree-view/TemplateClass.xaml:7` 写着
`xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"` ——
这个程序集模板项目**自己从没引用过**，是**用户**必须自己装的那一个。所以模板里所有的
`behaviors:` 前缀、`VeloxDev.WorkflowSystem` 命名空间引用，都只在**用户的项目里**才可能解析。

**不解决什么**：

- **不是 demo**。`Examples/Workflow/<GUI>*/` 才是能跑的；模板里没有树 ViewModel、没有窗口、没有入口。
- **不是适配器**。七个角色的实现全在 `Src/Adapters/VeloxDev.<GUI>/Attached/Workflow/`。
- **不是"一套模板七家共享"**。同一角色在七家**结构不同**（§六），不是同一份文件换个后缀。
- **改动无法独立验证（已被统一脚本取代）**。仓库里没有测试项目、模板目录下没有 `.md`；2026-10-04 起七家统一走
  `Src/Verification/verify-workflow-item-templates-all.ps1`（装包 → 生成七个条目 → 一起编译 → 与镜像比对 +
  残留 `replaces` 断言）。旧的两条 `verify-workflow-item-templates.ps1` / `verify-jalium-item-templates.ps1`
  现在是薄转发。
- **不是新设计**。每个条目文件都是该平台 `Examples/Workflow/<GUI> Trimmed/` 里同名文件的一份改名副本
  （改名后逐行差异最小 0 行、最大 68 行 —— MAUI tree-view 那 68 行是刻意的 demo/模板分叉，见 §五），
  源头与五类机械改动见 `extension.md` §1.1。

---

## 二、七份包共有的形状与唯一的离群

| 事实 | 值 | 覆盖 |
|---|---|---|
| `TargetFramework` | `netstandard2.0` | 7/7 |
| `Version` | `10.0.0` | 7/7 |
| `PackageId` | `VeloxDev.<平台>.Templates` | 7/7 |
| `sourceName` | `TemplateClass` | 49/49 个条目 |
| `preferNameDirectory` | `false` | 49/49 |
| `tags.type` | `item` | 49/49 |
| `.template.config/` 内容 | **恰好两个文件**：`template.json` + `dotnetcli.host.json` | 49 + 49 |
| 条目目录名 | 七个平台**同名同集** | 7 × 7 = 49 |

**Jalium 是唯一的结构离群**，四处都不一样（改模板时要照抄，先看这张表别抄错）：

| 项 | 其余六家 | Jalium |
|---|---|---|
| csproj 位置 | `working/VeloxDev.<平台>.Templates.csproj` | `VeloxDev.Jalium.Templates.csproj`（项目根，**没有 `working/`**） |
| `Pack` 的 content glob | `content\**\*` | `working\content\**\*`（`VeloxDev.Jalium.Templates.csproj:22`） |
| logo 相对路径 | `..\..\..\..\Assets\veloxdev-logo.png` | `..\..\..\Assets\veloxdev-logo.png`（`:23`，少一层） |
| 元数据 | 有 `PackageTags` / `PackageLicenseExpression` / `RepositoryUrl` | 三项全无；多一条 `IncludeSymbols=false`（`:18`） |

⇒ Jalium 是"条目在 `working/content/` 下、csproj 在上一级"；另外六家是"csproj 与 `content/` 同在 `working/` 下"。
两种都自洽，但对不上就是 path 全错。

---

## 三、49 个条目 = 7 平台 × 7 角色

| 条目目录 | `defaultName` | `shortName` 后缀 | 产物文件数 |
|---|---|---|---|
| `workflow-grid-decorator` | `GridDecorator` | `-decorator` | 1（有标记语言的为 2） |
| `workflow-link-view` | `LinkView` | `-link` | 1 或 2 |
| `workflow-minimap-overlay` | `MinimapOverlay` | `-minimap` | 1 或 2 |
| `workflow-node-view` | `NodeView` | `-node` | 1 或 2 |
| `workflow-slot-view` | `SlotView` | `-slot` | 1 或 2 |
| `workflow-template-selector` | `TemplateSelector` | `-selector` | 1 或 2 |
| `workflow-tree-view` | `TreeView` | `-tree` | 1 或 2 |

- `shortName` 完整形式是 `<平台小写>-v-<后缀>`（`wpf-v-tree`、`jalium-v-decorator`、`winforms-v-minimap`…）。
- `identity` 是 `VeloxDev.<平台>.<角色名>`（如 `VeloxDev.Jalium.WorkflowNodeView`）。
- **`primaryOutputs` 的条数就是"这个条目生成几个文件"**，且它列出的路径恰好等于条目目录里除 `.template.config/`
  外的全部文件：有标记语言的一家写两个（`TemplateClass.xaml` + `TemplateClass.xaml.cs`；Razor 是
  `.razor` + `.razor.cs`），纯代码的一家写一个（`TemplateClass.cs`）。⇒ **"这个平台有没有标记语言"在包元数据里就能读出来**，
  不用打开源码。

---

## 四、用户拿到之后怎么用

**人面向的完整版在 `skills/veloxdev-create-workflow/references/templates.md`**（安装、七条命令的完整命令行、
每个条目的全部 CLI 别名与默认值、生成之后还要自己写什么）。那一份是**用户手册**，不在这里重抄。
本节只写从 `template.json` 与模板源码能核到、而那一份文档没写（或写得比代码乐观）的部分。

| 从代码能核到的事 | 依据 |
|---|---|
| 用户**必须自己装适配器包**（模板项目零 `PackageReference`） | 见 §一；`workflow-tree-view/TemplateClass.xaml:7` 的 `assembly=VeloxDev.WPF` |
| 七个条目**必须落进同一个命名空间**，机制是 tree-view 用 `xmlns:local` 与 `xmlns:workflowViews` **两个前缀指向同一个** `clr-namespace:TemplateNamespace` | `workflow-tree-view/TemplateClass.xaml:5-6`，随后 `:18,26,37,51,70` 都用 `workflowViews:` 取兄弟 |
| `sourceName: "TemplateClass"` 是**全局文本替换的锚**：`-n` 同时改文件名、类名、`x:Class` 和别处的引用 | 49/49 个 `template.json` 的 `sourceName` 都是这个值 |
| `preferNameDirectory: false` ⇒ 产物不建子目录 | 49/49 |
| **改动无法验证（已被校验脚本取代）**：模板没有构建产物；2026-10-04 起七家由 `Src/Verification/verify-workflow-item-templates-all.ps1` 一起生成+编译+比对镜像（旧两条是转发） | §一 |
| 替换是**纯文本**的，所以颜色占位符也会被替换进生成文件的**注释**里 | 见 `skills/veloxdev-create-workflow/references/templates.md` 的同一条 |

---

## 五、跨条目契约：tree-view 按 `defaultName` 引用兄弟

**这是这份记忆里最容易断的一处。** 七个条目不是七份独立产物，而是一个宿主加六个部件；
tree-view 的产物里**写死了另外六条的 `defaultName`**。七家各自用自己语言的写法，但引用的是同一批名字：

| 平台 | 引用写法 | 依据 |
|---|---|---|
| WPF | `workflowViews:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`；前缀指向本命名空间 | `workflow-tree-view/TemplateClass.xaml:18,26,37,51,70` |
| WinUI | `local:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` | `workflow-tree-view/TemplateClass.xaml:18,26,34,48,67` |
| Avalonia | `local:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`（2026-09-25 起把视图模板挪成 keyed 资源 + 声明选择器；此前是隐式 `DataTemplate`、不引用 selector 条目） | `workflow-tree-view/TemplateClass.axaml:28,35,44,61,76` |
| MAUI | `local:NodeView` / `TemplateSelector` / `GridDecorator` / `LinkView` | `workflow-tree-view/TemplateClass.xaml:19,24,38,42` |
| Razor | 组件标签 `<GridDecorator>` / `<LinkView>` / `<MinimapOverlay>` / `<TemplateSelector>` / `<NodeView>` | `workflow-tree-view/TemplateClass.razor:18,27,35,55,65` |
| WinForms | 产物是基类的两个工厂：`CreateNodeView` 里 `new NodeView()`、`CreateLinkView` 里 `new LinkView()`；**不引用 `GridDecorator`**（整个文件里连这个类型名都没有，网格由基类自带的内部 `SurfaceCanvas` 承担），小地图只经基类属性引用 | `workflow-tree-view/TemplateClass.cs:29,44` |
| Jalium | 标记里按 `defaultName` 引用兄弟：`workflowViews:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`，`xmlns:workflowViews` 指向本命名空间 | `workflow-tree-view/TemplateClass.jalxaml` |

⇒ **改名是这里唯一会断的地方**：用户在生成时给某一条传了非默认 `-n`，必须回 tree-view 的手写照改。
没有任何工具、测试或编译器会提示你漏改了哪一处。

这一家在"引用兄弟"上还有三种别家没有的形态：

- **WinForms 的 tree-view 与 `workflow-grid-decorator` 条目无引用关系**：整个 tree-view 模板文件里
  连 `GridDecorator` 这个类型名都没有出现（网格由基类 `WorkflowTreeView.cs` 自带的内部 `SurfaceCanvas`
  承担）。⇒ **生成不生成这一条，tree-view 行为不变**；其余六家的 tree-view 都真的引用它
  （WPF/WinUI/Avalonia/MAUI/Razor/Jalium 都在标记/组件里实例化，见本节上表）。
- **WinForms 的 tree-view 与 minimap 的类型级耦合已在 2026-10-03 消除**：
  `IWorkflowMinimapScrollSource` 曾经只声明在 minimap 条目的产物里、tree-view 在同命名空间下直接
  模式匹配（旧的 `workflow-tree-view/TemplateClass.cs`，那两行已不存在），只生成 `winforms-v-tree` 会 CS0246。
  现在接口搬进了适配器（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/IWorkflowMinimapScrollSource.cs:16`），
  由 `WorkflowTreeView`（`:111,:123`）与 `WorkflowMinimapOverlay`（`:23`）引用，**模板侧零命中**。
  ⇒ 现在 `winforms-v-tree` 的编译期兄弟只剩 `NodeView` 与 `LinkView`（见 §7.6）。
- 对照：**WinForms 的可见集现在也由适配器喂给池**（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowTreeView.cs:423`
  取 `_tree?.GetHelper().VisibleItems`），模板不再自己发散虚拟化。WPF/WinUI/Avalonia/Razor 在标记里绑
  `Helper.VisibleItems`，MAUI 由适配器 `ViewManager` 在入队前筛掉连线交给共享 overlay 画；
  Jalium 现在也在标记里绑 `behaviors:ViewPool.ItemsSource`（`workflow-tree-view/TemplateClass.jalxaml`）。⚠ 早期本家模板曾把全量 `Nodes` 喂给池，那条形状已随 2026-10-03 重构消失。
  Razor 的 `Items="Tree.GetHelper().VisibleItems"` 在 `workflow-tree-view/TemplateClass.razor:35`；MAUI 的连线
  由适配器 `ViewManager` 在入队前筛掉（选择器给不出 `LinkTemplate`），交给共享 overlay 画
  （见 `adapters/maui.md` §2.2 与 `WorkflowSystem/adapters/maui.md` §二·1）。
  ⚠ **2026-10-03 起 `workflow-tree-view` 条目还带「连线的右键菜单」**：**条目**声明在模板里（WPF/Avalonia/WinUI/MAUI/Jalium
  是资源里的 `ContextMenu`，Razor 是 `.razor` 里的按钮，WinForms 是基类的 `OnBuildLinkMenu` 钩子），
  而**接线在适配层**（2026-10-03 用户改定）：WPF/Avalonia/WinUI/MAUI/Jalium 五家只多一行附着属性
  `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="<资源键>"`，适配器按这个键取菜单、包办订阅 / 定位 / 弹出 /
  开合上报，**模板 code-behind 因此只剩 `InitializeComponent()`**（WPF/Avalonia/WinUI/MAUI 四家 tree-view code-behind 现为 11–12 行；Jalium 的 code-behind 另有那个 `CanvasTransform` 镜像属性，见 `WorkflowSystem/adapters/jalium.md` §2.4）。
  Razor 没有附着属性这一层：菜单是 `.razor` 里带内联 `@onclick` 的 `<LinkMenu>` 按钮（见下）。
  传键不传菜单本身，是因为这个属性挂在表面
  自己的根元素上，`{StaticResource}` 会在定义它的资源字典之前求值（见 [item-template-specifications.md](../../specifications/item-template-specifications.md) §五）。
  **落点是 tree-view 而不是 link-view**：右键落在表面上（很多家的连线视图不吃指针），而弹出要屏幕坐标、
  模型给的是画布坐标 —— 只有表面同时知道这两件事。
  ⚠ **条目是「声明 + 绑定」，不是「声明 + 处理器」**（2026-10-03 第二轮定）：弹出前把**菜单自己的上下文设成那条连线**
  （WPF/Avalonia 是 `DataContext`；MAUI 是 `BindingContext`；WinUI 是逐条给 `MenuFlyoutItem.DataContext`，
  因为 `MenuFlyout : FlyoutBase : DependencyObject` **没有 `DataContext`**），条目写 `Command="{Binding DeleteCommand}"`
  即成一个新动作。Avalonia 因为资源里没有 `x:DataType`、而 demo 开了编译绑定，必须写 `{ReflectionBinding …}`，不能退回 `Click`。
  Razor 没有绑定那一套，保留内联 `@onclick`（条目仍要一眼可增删）；WinForms 的条目在 `OnBuildLinkMenu` 里增删，Jalium 的条目在资源里的 `ContextMenu` 上增删。
  ⚠ **MAUI 的一份菜单有两副面孔**：Windows 上把声明的 `MenuFlyout` 翻成原生 flyout；非 Windows **没有任意点弹出**，
  翻成一层**由适配器自己搭的浮层**（2026-10-03 起：原来那层 `PART_LinkMenuLayer` 标记长在模板 XAML 里，
  它是呈现、不是声明，已随接线一起搬进 `WorkflowSurfaceBehavior.EnsureLinkMenuLayer`；不翻转、不出窗口、
  不是 OS 菜单、嵌套项会拍平）。非 Windows 的「请求菜单」手势是**长按**（`LongPressDelay = 500` ms，
  位移超过 `LongPressMoveSlop = 8` 设备无关单位即取消），由链接层翻译成一次合成右键，交给输入路由。
  ⚠ **菜单不会活得比它指着的那条线久**（2026-10-03）：判定归 Core —— hub 在「菜单开着、那条线却离开树」时发
  `LinkRemoved`；**接线在适配层，模板不参与**（`WorkflowSystem/architecture.md`）。
  ⚠ **模板里的注释一律英文**（2026-10-03 用户定，见 [code-comment-specifications.md](../../specifications/code-comment-specifications.md) §五）：
  只标扩展点、一行说清，函数体注释也算在内 —— 不是每个成员都配得上一行。
  ⚠ **2026-10-03 起 MAUI 的 Trimmed demo 与它的模板故意分叉了**：demo 已经改成**每线一视图**
  （`Demo/Controls/Workflow/LinkView.xaml(.cs)`，节点与连线共用一个池、选择器给 `LinkTemplate`），
  而 `workflow-link-view` / `workflow-tree-view` 两个模板**还是旧的 overlay 形态**。
  这是「先做 demo 看效果、再做模板镜像」的顺序决定的，**不是遗漏**；镜像那一轮要把这条注、§六 轴 1 的绑定说明、
  以及 §八 里「MAUI 模板没有本地连线视图」那句一起改掉（那三处现在只对模板成立）。
- **Jalium 的 tree-view 在标记里引用五个兄弟条目**：`NodeView` / `LinkView` / `TemplateSelector` /
  `GridDecorator` / `MinimapOverlay`（`workflow-tree-view/TemplateClass.jalxaml` 的资源与正文），所以
  "少生成一条兄弟就编译不过"在这家覆盖这五条。见 `adapters/jalium.md`。

---

## 六、平台族：同一条目在七家的结构差异

差异不是装饰性的。三条轴，各自分族 —— **跨平台抄模板前先确认两边同族**。

### 轴 1 · 标尺默认厚度：28 与 36

| 值 | 平台 | 依据 |
|---|---|---|
| **28** | WPF、Avalonia、WinUI、MAUI、Razor | WPF `workflow-grid-decorator/TemplateClass.cs:39`；Avalonia 同文件 `:38`；WinUI `:71`；MAUI `:24`；Razor 是 `workflow-grid-decorator/TemplateClass.razor.cs:20`，tree-view 里又写死一份 `workflow-tree-view/TemplateClass.razor:20` |
| **36** | WinForms、Jalium | **都不在模板的 tree-view 里**：WinForms 定义在适配器基类 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowGridDecorator.cs:31` 的 `const DefaultRulerThickness = 36`（tree-view 的 `RulerReserve` 也读它，`WorkflowTreeView.cs:45`）；Jalium 的 36 现在在**模板自己的 grid-decorator** 里 —— `workflow-grid-decorator/TemplateClass.cs:34` 的 `const DefaultRulerThickness = 36`（`RulerThickness` DP 的默认值） |

WinForms 的 36 是**有理由的、注释写明的偏离**：`WorkflowGridDecorator.cs:29` 的 `<remarks>` 写着
`Other flavours of this control use 28; WinForms reads visually smaller, so the platform default is 36.`
Jalium 的 36 **没有说明**（模板的 grid-decorator 只写「默认标尺带厚度」）。

⇒ **这个数字在别处被引用，且引用方式分两派**：

- **绑定式**（改厚度自动跟随）：WPF / Avalonia / WinUI / MAUI 在 tree-view 里把 `TranslateTransform` 的 X/Y
  绑到 `PART_GridDecorator` 的 `RulerThickness`（WPF `workflow-tree-view/TemplateClass.xaml:63-64`；
  Avalonia `TemplateClass.axaml:68-69`；WinUI `TemplateClass.xaml:60-61`；MAUI `TemplateClass.xaml:53-54`）。
- **复制式**（改厚度必须手改）：只剩 **Razor** —— tree-view 里写死 `RulerThickness="28"`
  （`workflow-tree-view/TemplateClass.razor:20`）。
- **单一来源**：**WinForms** —— 厚度只定义在适配器基类，表面 / 节点 / 连线都引用同一个常量，**模板侧零副本**
  （WinForms 旧的 tree-view `RulerReserve` 与 node-view 硬编码 `+ 36` 已随重构消失）；**Jalium** 的 36 只定义在
  模板的 grid-decorator 一处（`RulerBand => RulerThickness`），模板其余文件零副本。

### 轴 2 · 连线用什么方式画：四族

七家的 `workflow-link-view` **有一半的 XAML 是空壳**，看标记是看不出它怎么画的 —— 必须看代码。

| 族 | 平台 | 依据 |
|---|---|---|
| **立即模式**：继承一个能覆写绘制入口的元素，XAML 只是空壳 | WPF、Avalonia、Jalium、WinForms | WPF `workflow-link-view/TemplateClass.xaml` 全文是一个空 `<UserControl>`（5 行），几何全在 `.xaml.cs:92` 的 `OnRender`；Avalonia 同理（`TemplateClass.axaml` 是空 `<Control>`，`Render` 在 code-behind `:173`）；Jalium 的模板也是空 `<UserControl>`（5 行），几何在 `.jalxaml.cs` 里算、自盒化（`WorkflowLinkBounds.Apply`）并 `PublishCurve`；WinForms 的几何在**适配器助手** `WorkflowLinkAttachment`（`RebuildGeometry`：自盒化、雕窗口区域、发布曲线），视图模板自己是 `: Control`，只设调色板并在 `OnPaint` 里画 |
| **保留式几何在代码里构造**：XAML 是空壳，`Path` + `PathGeometry` 在构造函数里 new 出来 | WinUI | `workflow-link-view/TemplateClass.xaml` 只有 6 行（`Clip="{x:Null}"` 是全部内容）；`TemplateClass.xaml.cs:28-31` 是 `Path` / `PathGeometry` / `PathFigure` 字段，`:63-64` 在 ctor 里 new 并 `Children.Add`。原因是这一家**没有公共 `OnRender`**，理由见 `memory/modules/WorkflowSystem/adapters/winui.md` §二·L2，此处不抄 |
| **标记语言里的元素 + 代码给几何字符串** | Razor | `workflow-link-view/TemplateClass.razor` 的 `<path d=…>`（虚线的 `stroke-dasharray="@dash"`，`dash` 在 code-behind 里按 `IsVirtual` 取 `"6 4"`）；`data-veloxdev-link-curve="1"` 是给适配器缩放 JS 认的标记，不是样式 —— 见 `adapters/razor.md` §2 |
| **复用适配器的视口级图层** | MAUI | `workflow-link-view/TemplateClass.xaml:14` 直接放 `behaviors:WorkflowLinkOverlay`；文件头 `:2-7` 的注释写明取舍：**每条线一个 `GraphicsView` 会在深缩放下超出 Win2D 纹理上限并静默消失**，所以一个表面只放**一个** link 层 |

⇒ **WPF / WinUI 那一对容易看错**：WPF 的 tree-view 给 `LinkView` 绑了 `Width/Height` 到
`PART_Canvas` 的 `ActualWidth/ActualHeight`（`workflow-tree-view/TemplateClass.xaml:32-33`），
WinUI 的 tree-view **故意不绑**并在注释里写明理由（`workflow-tree-view/TemplateClass.xaml:25`：
`Do not bind Width/Height here; LinkView sizes its own box.`）。
两者最终都在代码里画，但"盒子谁给"是相反的。

### 轴 3 · minimap 是薄壳还是自带实现

| 族 | 平台 | 模板文件行数（实测） |
|---|---|---|
| 薄壳：继承/配置框架或适配器给的实现，只设颜色 | WPF 24、Avalonia 25、MAUI 21、Jalium 24、WinUI 42、Razor 80（`.razor` 15 + `.razor.cs` 65） | 见左 |
| 薄壳（2026-10-03 起也归此列） | WinForms 22 | `workflow-minimap-overlay/TemplateClass.cs` |

⇒ **2026-10-03 起七家全是薄壳**：WinForms 的 335 行实现搬进了适配器
（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowMinimapOverlay.cs`），原来"该家适配器里没有
创建控件的钩子、装饰器/小地图只能由用户代码提供"的推理链已作废（迁移判据见
[`adapter-base-class-specifications.md`](../../specifications/adapter-base-class-specifications.md) §2.2）。
Jalium 那 24 行设四个颜色符号（`workflow-minimap-overlay/TemplateClass.cs`）；最薄的现在是 MAUI 的 21 行。

**2026-10-03：WinForms 的七项全部翻面。** 适配器包为**每一个**角色提供可重写基类，模板只剩策略：

| 角色 | 之前 | 现在 | 其余六家同角色（含 Jalium，同口径：全部 `TemplateClass.*` 行数之和，Razor 含 `.razor` + `.razor.cs`） |
|---|---|---|---|
| tree-view | 1095 | **52** | 79–125（MAUI 最薄 79，Razor 最厚 125） |
| node-view | 790 | **404** | 83–243 |
| slot-view | 379 | **22** | 28–145 |
| link-view | 346 | **21** | 76–345 |
| grid-decorator | 279 | **41** | 111–535 |
| minimap-overlay | 325 | **22** | 21–80 |
| template-selector | 36 | **19** | 33–67 |
| **合计** | **3257** | **581** | — |

包内七个控件在 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`，另有三个共用件
（`WorkflowSurfaceColors` / `WorkflowSurfaceGraphics` / `WorkflowSurfaceGrid`）。

**这一条把「离群值」那把尺子换掉了。** 中途我曾用「`grid-decorator` 279 行落在其余六家 110–535 的中间、
不是离群值」当理由没做它 —— 那是**错的**：判据是「**这一段是不是用户该改的扩展点**」，
不是「这个角色像不像用户的」。该角色含平台机制（网格线的世界坐标换算、刻度与标签排版、每帧重绘），
用户该拿到的是派生 + 调色板。用户 2026-10-03 把这条定成规范，见
[`adapter-base-class-specifications.md`](../../specifications/adapter-base-class-specifications.md) §2.2。

**唯一还高的是 `node-view`**（404 vs 其余最多 243）：收掉的只有机制（绑定/定位/折叠/反射读名字），
**卡片长什么样是用户的设计** —— 里面三个嵌套面板（`DynamicOutputsPanel` / `DynamicSlotRow` /
`DoubleBufferedPanel`）与 `OnPaintBackground` 是它的视觉，按 §2.1 不该进包。

**2026-10-05：Jalium 转成标记驱动（与 WPF 逐行同形）。** 适配器不再提供可继承基类，角色落成附着行为
（见 [WorkflowSystem/adapters/jalium.md](../../WorkflowSystem/adapters/jalium.md) §一），模板是
`TemplateClass.jalxaml`（+ `.jalxaml.cs`）或纯 `.cs`：

| 角色 | 产物 | 行数（实测） |
|---|---|---|
| tree-view | `.jalxaml` + `.jalxaml.cs` | **68 + 25 = 93** |
| node-view | `.jalxaml` + `.jalxaml.cs` | **76 + 9 = 85** |
| slot-view | `.jalxaml` + `.jalxaml.cs` | **12 + 48 = 60** |
| link-view | `.jalxaml` + `.jalxaml.cs` | **5 + 314 = 319** |
| grid-decorator | `.cs`（`sealed class : Grid, IWorkflowGridDecorator`） | **449** |
| minimap-overlay | `.cs` | **24** |
| template-selector | `.cs` | **49** |
| **合计** | 四个 `.jalxaml` 条目 + 三个 `.cs` 条目 | **1079** |

⇒ Jalium 的 `Attached/Workflow/` 现在是 **9 文件 3435 行**（附着行为 + 池 + 小地图），另有模板侧 1079 行。校验：`Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict`。见 `extension.md` §4.1。

---

## 七、静默失败清单

### 7.1 12 个"命令行收得下、什么也不替换"的 symbol

`template.json` 里的 symbol 若 `type: parameter` 而**没有 `replaces`**，`dotnet new --help` 会把它列出来、
命令行能传、**不报错、也不替换任何文本**。逐文件核对的结果：共 12 个，分布在 7 个条目文件里，
**WinForms 与 Jalium 两个平台完全没有**（其余五家各 1–7 个，见下表）。

| 平台 | 条目 | 空转的 symbol |
|---|---|---|
| WPF | `workflow-slot-view` | `slotBorderColor` |
| WinUI | `workflow-slot-view` | `slotBorderColor` |
| Avalonia | `workflow-slot-view` | `slotColor`、`slotBorderColor` |
| MAUI | `workflow-slot-view` | `slotPath` |
| Razor | `workflow-slot-view` | `slotBackground` |
| Razor | `workflow-grid-decorator` | `gridBackground`、`minorGridColor`、`majorGridColor` |
| Razor | `workflow-tree-view` | `surfaceBorderBrush`、`surfaceBorderThickness`、`surfaceCornerRadius` |

**2026-10-04 起 12 个全都在自己的 `description` 里自陈**：`Accepted for cross-GUI CLI parity; <这家为什么没有消费者>`。
⚠ **这是刻意的跨 GUI 契约，不是漏接**（[skills/veloxdev-create-workflow/references/templates.md](../../../skills/veloxdev-create-workflow/references/templates.md)
「Style parameters」一节）：七家接受**同一套参数名、同一套默认值**，命令行因此能在 GUI 之间原样搬。
**别因为「它不生效」就把它删掉** —— 删了 `dotnet new` 会对这条参数报未知参数，命令行就不通了。
12 个现已全部自陈（此前只有 Razor 的 `slotBackground` 写了那句话）；补的是**说明**，
不是删参数，也不是给它硬造一个样式点。

分布上值得记住的两条：

- **`slotBorderColor` 在三个平台空转**（WPF/WinUI/Avalonia），是覆盖面最广的一个。
- **WinForms 与 Jalium 是 `workflow-slot-view` 零空转符号的平台。**

⚠ **`slotPath` 特别容易骗人**：七个平台的 `defaultValue` 是**逐字节相同**的一段 SVG 路径
（viewBox 1024×1024 的圆环图标），但**只有六个真的替换它** ——
MAUI 声明了却没有 `replaces`。⇒ 在 MAUI 上传 `--slotPath` 什么也不会发生，
而图标看起来"是那段路径"，会让人以为换成功了。

⚠ **人面向文档只举了子集，本表才是逐文件核对的完整清单**：
`skills/veloxdev-create-workflow/references/templates.md` 点名了 Razor 的树边框/圆角、MAUI 的 `slotPath`、
WPF/WinUI/Avalonia 的 `slotBorderColor` 与 Avalonia 的 `slotColor`，并声明 "Each `gui/<gui>.md` lists its pack's
inert parameters"；它不再给全量数字。逐文件核对的结论仍是 **12 个**（上表即是跨平台的权威枚举）。

⚠ **"pack says so in its own `template.json`"对七家现在全部成立**：2026-10-04 起 12 个参数的
`description` 里都带了 `Accepted for cross-GUI CLI parity; …` 这一句。
Razor 的 `workflow-slot-view`（`…/workflow-slot-view/.template.config/template.json:54`：
`Accepted for cross-GUI CLI parity; this GUI's slot has no separate background surface.`）只是**最早**这么写的一个。

### 7.2 六份 csproj 里有一条匹配不到任何文件的 Pack glob

```xml
<None Include="..\..\skills\veloxdev-workflow-item-templates\references\**\*" Pack="true" PackagePath="references" Visible="false" />
```

WPF 在 `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj:27`，
Avalonia / WinUI / MAUI / Razor / WinForms 同位置同内容（`git grep` 六份都命中 `:27`）。

⇒ **这条路径本身是错的**：从 `working/` 上两级落在 `Src/Templates/`，所以它指的是
`Src/Templates/skills/veloxdev-workflow-item-templates/references/**\*` —— 而 `Src/Templates/skills`
**整个目录都不存在**。要够到仓库根的 `skills/` 得写**四级**（`..\..\..\..\skills\…`）。
⇒ **它匹配 0 个文件，有两重原因**：① 连目录都没走到（从 `working/` 上两级是 `Src/Templates/`，
不是仓库根）；② 仓库根 `skills/` 下**根本不存在** `veloxdev-workflow-item-templates/` 这个子目录
（`skills/` 是 `README.md` + 七个目录：`veloxdev-add-aspects`、`veloxdev-create-animation`、
`veloxdev-create-workflow`、`veloxdev-drive-workflow-with-ai`、`veloxdev-switch-themes`、
`veloxdev-tick-loop`、`veloxdev-write-viewmodels`）。**包内不会有 `references/` 目录**，
而 `dotnet pack` 对空 glob 不报错。**Jalium 没有这一行**（它的 csproj 只有两条 `None`，`:22-23`）。

### 7.3 Avalonia 的 tree-view 生成出来就编译不过 —— 而它自己写明了

`workflow-tree-view/TemplateClass.axaml:1-4` 的注释：
`compiled bindings need a concrete x:DataType as their starting type … until you do, the build fails HERE on vm:TreeViewModel`；
`:9` 是 `xmlns:vm="using:TemplateNamespace"`，正文里是 `x:DataType="vm:TreeViewModel"`。
`vm:TreeViewModel` **不是**模板生成的任何一个类型，它是**用户自己的树 ViewModel**。

⇒ 这是七家里唯一一个把"生成即失败"写进产物的条目。它是**设计**（逼用户填对类型），不是缺陷；
别把它"修"成能编译。

### 7.4 插槽状态着色：Avalonia 完全没有

`SlotState`（决定插槽在连线手势下变紫/番茄红/青柠）在七家的实现方式：

| 平台 | 方式 | 依据 |
|---|---|---|
| WPF | slot-view 有 `SlotStateProperty` DP，node-view 两侧都绑 | `workflow-slot-view/TemplateClass.xaml.cs:12-16`；`workflow-node-view/TemplateClass.xaml:39,62` |
| WinUI | 同上，DP 在 slot-view，node-view 两侧都绑 | `workflow-slot-view/TemplateClass.xaml.cs:12-16,24-28`；`workflow-node-view/TemplateClass.xaml:40,63` |
| MAUI | `BindableProperty` + 绑 `Slot.State` / `State` | `workflow-slot-view/TemplateClass.xaml.cs:8-13`；`workflow-node-view/TemplateClass.xaml:32,53` |
| Jalium | 模板 slot-view 自己在 code-behind 里按 `SlotState` 算 `Foreground`；node-view 绑 `SlotState="{Binding State}"` | `workflow-slot-view/TemplateClass.jalxaml.cs` 的 `UpdateForeground`；`workflow-node-view/TemplateClass.jalxaml` |
| Razor | 不绑，slot-view 在 C# 里算 | `workflow-slot-view/TemplateClass.razor.cs:43-62` |
| WinForms | 不绑，状态色由**适配器基类** `WorkflowSlotView` 按 `SlotState` 算（`WorkflowSlotView.cs:219-223`）；模板 slot-view 只剩调色板（该家 slot-view 完全没有声明空转 symbol） | `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSlotView.cs:219` |
| **Avalonia** | **没有** —— slot-view 里搜 `SlotState` 零命中 | `workflow-slot-view/TemplateClass.axaml:12-13` 只把 `<Path Fill>` 绑到 `$parent[UserControl].Foreground`，而模板里没有任何地方写 `Foreground` |

⇒ 在 Avalonia 上，插槽**永远不会变色**，而且不会报错。要修就得给 slot-view 加一个状态属性再绑上去。

### 7.5 渲染就绪门：WPF / Jalium / Razor / WinForms 四家写了

| 有门 | 写法 | 依据 |
|---|---|---|
| WPF | `link.IsRenderReady()` | `workflow-link-view/TemplateClass.xaml.cs:96` |
| Jalium | `!link.IsRenderReady()` | `Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-link-view/TemplateClass.jalxaml.cs`（`Refresh`） |
| Razor | `WorkflowSlotUpdateGate.IsLinkRenderReady(link)` | `workflow-link-view/TemplateClass.razor.cs:237` |
| WinForms | `WorkflowSlotUpdateGate.IsLinkRenderReady(link)` | **已进适配器**：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowLinkAttachment.cs` 的 `RebuildGeometry`（模板 link-view 是自己的控件 + `Attach`） |

| 无门 | 它们各自有的东西 |
|---|---|
| Avalonia | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.axaml.cs` 只有 `CanRender`） |
| WinUI | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.xaml.cs` 只有 `CanRender`） |
| MAUI | 无门 |

⇒ `WorkflowSystem/extension.md` 那条"连线视图首行过渲染就绪门"的契约，**模板侧四家落地**。
另有两条 `double.IsNaN` 守卫：Razor（`workflow-link-view/TemplateClass.razor.cs:250`）与 Jalium 的模板
（`workflow-link-view/TemplateClass.jalxaml.cs` 的 `Refresh`，不加会抛 `ArgumentException` 退出）。

### 7.6 WinForms 少生成一条兄弟条目 ⇒ 编译不过（且没有任何地方写明）—— **2026-10-03 已对 tree-view 消除**

**修复前的形状（历史，代码里已不复存在）**：`IWorkflowMinimapScrollSource` 只声明在 minimap 条目的产物里
（旧的 `workflow-minimap-overlay/TemplateClass.cs:332-334`），tree-view 在同命名空间下直接模式匹配
（旧的行号已不存在）⇒ 只生成 `winforms-v-tree` 得到的代码编译不过（CS0246）。
这一条与 §7.3 的 Avalonia 不同：那一处的注释自己写明了"build fails HERE"，
**这一处没有任何文件提到过**，只能读代码发现。

**已修**：该接口搬进了适配器（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/IWorkflowMinimapScrollSource.cs`），
基类与本条都不再依赖 minimap 产物 —— 现在 `winforms-v-tree` 的编译期兄弟只有 NodeView 与 LinkView。
新的 `verify-workflow-item-templates-all.ps1` 会**一起生成并编译七条**（`verify-workflow-item-templates.ps1` 现在只是转发），所以再出现这类漏依赖会当场红。

---

## 八、调色板与连线：模板的默认值 = Trimmed 那一套，非 Trimmed 的 demo 分四套

**模板的默认配色不是随手写的，是 `template.json` 里 symbol 的 `defaultValue`**，七家同名 symbol 取值一致。它是全仓**唯一一套统一调色板**：

| 用途 | 值 |
|---|---|
| 画布底 / 边框 | `#1E1E1E` / `#33FFFFFF`（1px，圆角 3） |
| 次网格 / 主网格 / 轴 / 刻度 / 分隔 | `#2A2D2E` / `#3A3D40` / `#4D4D4D` / `#555555` / `#3A3D40` |
| 标尺底 / 标尺字 | `#C8252526` / `#888888` |
| 节点卡 底 / 字 / 边 | `#DDFFFFFF` / `#DD1E1E1E` / `#331E1E1E` |
| 槽位 待机 / 描边 / 三态 | `#DD1E1E1E` / `#FFFFFFFF` / `#FF6347`·`#32CD32`·`#EE82EE` |
| 连线 | `#DDFFFFFF`，粗 2 |
| 小地图四色 | `#D2141922` / `#DC94A3B8` / `#DC38BDF8` / `#F0FFFFFF` |

**七家 Trimmed demo 逐项等于上表**，只有两处刻意偏差：WinForms 标尺 alpha 用 `#70252526`（源码注释：让网格透出来）、Jalium 标尺四色自成一档。

**七家非 Trimmed demo 却是四套**：Avalonia / WPF / WinUI / MAUI 共用一套「青蓝 v1」（画布 `#141922`、轴 `#38BDF8`、连线 `Colors.Cyan` 或 `#22D3EE`、节点卡逐类型配色），WinForms、Blazor、Jalium 各又一套。

**2026-09-25 对齐的只是画布层，不是全部。** 那四家的画布底 / 网格 / 标尺 / 连线 / 边框已换成上表的值（各家 `WorkflowGridDecorator` 的八个静态字段 + 连线默认色 + `PART_SurfaceBorder`）。**节点卡的逐类型配色刻意保留** —— Controller 深灰、Timer/Python 蓝、Enum 紫是 feature demo 要展示的东西，对齐成一张白卡等于把这条信息删掉。用者的口径是「只对齐画布层」。

**改配色的落点**：网格与标尺集中在各家的 `WorkflowGridDecorator`（WinForms 拆在适配器的 `WorkflowGridDecorator.cs` 与 `WorkflowTreeView.cs` 两处；Razor 在适配器 `WorkflowGridDecorator.razor.cs` 加 surface）。连线的默认色一般在各家自己的连线视图里，**MAUI 模板是例外：模板没有本地连线视图，用的是适配器** `Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowLinkOverlay.cs`（demo 已于 2026-10-03 改成每线一视图，见 §五），在那里改会**同时改掉 Trimmed MAUI**（2026-09-25 删箭头就是这么做的）。

**连线：七家都没有箭头了。** 箭头原先七家都有（长 12、宽 8，`!IsVirtual` 时画），2026-09-25 全部删除。
模板与各家 Trimmed demo 现在都是**一条平线 + 悬停高亮**：高亮时绕线画一圈白色 halo
（如 Avalonia Trimmed `Demo/Demo/Views/Workflow/LinkView.axaml.cs:192` 的 `(color, 0.25)`、`thickness + 6`）。
**动的光带（旧称「流光」）只在部分非 Trimmed demo 里**，例如 Avalonia 的
`Examples/Workflow/Avalonia/Demo/Views/Workflow/PolylineCurveView.axaml.cs`（沿曲线裁剪几何做「彗星拖尾」，
中文注释在 `:28`）—— 它**不在模板、也不在 Trimmed demo 里**。
旧的「渐变刷三个停靠点 `0.06 / 0.34 / 0.66 / 0.94` + `550/650/550 ms`」那套已随 demo 重写消失，不要再照抄。

**旧的两处死代码已消失**：WPF / WinUI 的贝塞尔版 `BezierCurveView` 已随 demo 重写删除（全仓只剩 Avalonia 非 Trimmed demo 里的一个，且被 `WorkflowView.axaml` 引用，不是死代码）。

---

## 九、视图层的三个坑：端口被裁一半、删 axaml 会连带删掉交互、`Slot.State` 静默失败

这三个坑都不报错 —— 编译通过、端口照样画出来，只是**半边不见了**、**再也拖不动**、或**状态永远不变**。2026-09-25 一次自绘改造里同时踩到。

### 9.1 端口有一半骑在卡外，所以它上方不能有裁剪面

端口宽 32 设计单位（枚举列表里 24）、外溢一半骑在卡边上。任何一个 `ClipToBounds="True"` 的祖先都会把它切掉外侧那一半。

已经踩到的两处，**成因不同、修法也不同**：

| 卡 | 成因 | 修法 |
|---|---|---|
| `Examples/Workflow/Avalonia/Demo/Views/Workflow/ControllerView.axaml` | 端口**没有 `ZIndex`**，被卡面盖掉内侧一半。`TimerNodeView.axaml` 一直把端口包在 `ZIndex="6"` 的层里，Controller 漏了 | 补 `ZIndex="6"` |
| `…/EnumSelectorNodeView.axaml` | 输入端口原本放在 `<Border Grid.Row="1" ClipToBounds="True">` **里面** —— 那个裁剪是为「滚动内容不溢出圆角」而设的，**不能去掉** | 把端口挪成该 Border 的**兄弟**，直接挂根 Grid（与 Controller 同形） |

**Python 走的是第三条路**：它用「ScrollViewer 视口外扩 N / ItemsControl 内容内缩 N / 端口外溢 N」把端口留在滚动视口内。**三个 N 必须相等，且等于端口尺寸的一半** —— 端口从 16 换到 24 时 N 必须从 8 一起改到 12。这条约束在 `…/PythonNodeView.axaml` 的两条端口带里各有一份（左输入、右输出），改一处不改另一处就是对不齐。

**核查方法（比截图可靠得多）**：沿端口往上数祖先，只要有一个 `ClipToBounds="True"` 就是错的。五张卡 2026-09-25 审计后：Controller / Timer / Python / Enum 输入全部干净；Enum 的输出列表行仍在那层裁剪里，但行内容内缩 14、端口只外溢 12 → 还剩 2 单位在卡内，**够用**。

### 9.2 删掉 `SlotView.axaml` 会连带删掉交互，而且不报错

`Avalonia/Demo/Views/Workflow/` 下的 `SlotView` 原是一个 `UserControl` + 一个 `.axaml`，那个 axaml 的根上挂着**两样性命攸关的东西**：

- `behaviors:WorkflowSlotConnectionBehavior.IsEnabled="True"` —— 它给控件挂 `PointerPressed` / `PointerReleased`（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSlotConnectionBehavior.cs:22-32`）。没有它，**端口拖不出连线**。
- `Background="#01000000"` —— 那是端口的**命中测试面**。全部改自绘、删掉 axaml 之后它一起没了，指针事件到不了控件。

两样都不报错：端口照样画得好好的，只是静默失去交互。现在两者都写在 `SlotView` 的**构造函数**里（外加一个近透明的子 `Border` 作实在的命中面 —— 依赖 `UserControl` 自己的 `Background` 能否被命中是一层推断，落一个真元素就不是了），卡片忘了也不会再丢。

⇒ **把标记语言控件改成自绘之前，先列出被删掉的那个根元素上都挂了什么。** 附着属性、`Background`、`x:Name`（会被别处按名字找）都算。

### 9.3 输出槽的 `SlotState` 写成 `Slot.State` 会静默失败

输出槽在 `DataTemplate` 里（`ItemsSource="{Binding OutputSlots.Items}"`），那个 `DataTemplate` 的 DataContext 是**包装项**（成员是 `Slot` 与 `Name`，模板里两个都用到过）。所以 `DataContext="{Binding Slot}"` / `BindingContext="{Binding Slot}"` 是对的 —— 槽视图的上下文就该是槽自己。

错的是**同一个元素上**再写 `SlotState="{Binding Slot.State}"`：`DataContext` 一被设成槽 VM，这一行就改在槽 VM 里求值，而 `IWorkflowSlotViewModel` 的成员只有 `Targets` / `Sources` / `Parent` / `Channel` / `State` / `Anchor`（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowSlotViewModel.cs:15,20,25,30,35,40`），**没有 `Slot`** ⇒ 绑定失败、槽永远拿不到自己的状态，而**不报错**：编译通过、槽照常画出来。

正确写法是 `SlotState="{Binding State}"`（与输入槽那行 `{Binding State}` 同形 —— 输入槽的上下文本来就是槽）。

**模板与 demo 各有一份，必须一起改**：三家模板的 `workflow-node-view/TemplateClass.xaml` 输出槽那一行（MAUI `:53`、WPF `:62`、WinUI `:63`）与三个 Trimmed demo 的 `NodeView`。**方向是改模板** —— Trimmed demo 与模板对应，不单独改它们，否则两边分叉。Avalonia 模板不设 `SlotState`（`TemplateClass.axaml:39`、`:59` 只给 DataContext），所以这家没有这个问题。

---

## 十、我要改 X → 打开哪个文件

| 我要改 | 先打开 |
|---|---|
| 某平台某个部件生成后长什么样 | 那一条目目录下的 `TemplateClass.*` |
| 外壳结构（`PART_*` 嵌套、绑定关系） | 该平台的 `workflow-tree-view/TemplateClass.*` |
| 标尺厚度 | 该平台 `workflow-grid-decorator` 的 `RulerThickness`（Jalium 也在模板里：`workflow-grid-decorator/TemplateClass.cs:34` 的 `DefaultRulerThickness = 36`）。**加** Razor `workflow-tree-view/TemplateClass.razor:20`（写死 28）与 WinForms 的适配器 `WorkflowGridDecorator.cs:31` |
| 条目对外的 CLI 参数 | 该条目 `.template.config/template.json` 的 `symbols`（**记得 `replaces`**，否则就是 §7.1）+ `dotnetcli.host.json` 的 `symbolInfo` |
| 条目名 / 命令名 / 默认类名 | `.template.config/template.json` 的 `identity` / `shortName` / `defaultName`，**并回头查 tree-view 里对它的引用**（§五） |
| 新增一个平台 | `extension.md` §三 |
