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
| 但仍然进解决方案 | `VeloxDev.slnx:166-173` 的 `/Templates/` 文件夹列了七个项目（Jalium 那行的路径没有 `working/`，见 §二） |

⇒ 由此得出第一件必须记住的事：**模板产物编译不过，本仓库的任何构建都不会发现**。

生成的 `workflow-tree-view/TemplateClass.xaml:7` 写着
`xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"` ——
这个程序集模板项目**自己从没引用过**，是**用户**必须自己装的那一个。所以模板里所有的
`behaviors:` 前缀、`VeloxDev.WorkflowSystem` 命名空间引用，都只在**用户的项目里**才可能解析。

**不解决什么**：

- **不是 demo**。`Examples/Workflow/<GUI>*/` 才是能跑的；模板里没有树 ViewModel、没有窗口、没有入口。
- **不是适配器**。七个角色的实现全在 `Src/Adapters/VeloxDev.<GUI>/Attached/Workflow/`。
- **不是"一套模板七家共享"**。同一角色在七家**结构不同**（§六），不是同一份文件换个后缀。
- **不能独立验证**。仓库里没有一条命令能"跑一下看看生成的模板对不对"——没有测试项目、没有 `.md`、
  没有任何脚本引用 `Src/Templates/`。
- **不是新设计**。每个条目文件都是该平台 `Examples/Workflow/<GUI> Trimmed/` 里同名文件的一份改名副本
  （实测逐行差异最小 0 行、最大 54 行），源头与五类机械改动见 `extension.md` §1.1。

---

## 二、七份包共有的形状与唯一的离群

| 事实 | 值 | 覆盖 |
|---|---|---|
| `TargetFramework` | `netstandard2.0` | 7/7 |
| `Version` | `9.0.0` | 7/7 |
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
- `identity` 是 `VeloxDev.<平台>.<角色名>`（如 `VeloxDev.Jalium.WorkflowLinkView`）。
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
| 七个条目**必须落进同一个命名空间**，机制是 tree-view 用 `xmlns:local` 与 `xmlns:workflowViews` **两个前缀指向同一个** `clr-namespace:TemplateNamespace` | `workflow-tree-view/TemplateClass.xaml:5-6`，随后 `:17,25,36,46,65` 都用 `workflowViews:` 取兄弟 |
| `sourceName: "TemplateClass"` 是**全局文本替换的锚**：`-n` 同时改文件名、类名、`x:Class` 和别处的引用 | 49/49 个 `template.json` 的 `sourceName` 都是这个值 |
| `preferNameDirectory: false` ⇒ 产物不建子目录 | 49/49 |
| **改动无法验证**：模板没有构建产物、仓库里没有任何脚本或测试引用 `Src/Templates/` | §一 |
| 替换是**纯文本**的，所以颜色占位符也会被替换进生成文件的**注释**里 | 见 `skills/veloxdev-create-workflow/references/templates.md` 的同一条 |

---

## 五、跨条目契约：tree-view 按 `defaultName` 引用兄弟

**这是这份记忆里最容易断的一处。** 七个条目不是七份独立产物，而是一个宿主加六个部件；
tree-view 的产物里**写死了另外六条的 `defaultName`**。七家各自用自己语言的写法，但引用的是同一批名字：

| 平台 | 引用写法 | 依据 |
|---|---|---|
| WPF | `workflowViews:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay`；前缀指向本命名空间 | `workflow-tree-view/TemplateClass.xaml:17,25,36,46,65` |
| WinUI | `local:NodeView` / `LinkView` / `TemplateSelector` / `GridDecorator` / `MinimapOverlay` | `workflow-tree-view/TemplateClass.xaml:17,27,35,45,64` |
| Avalonia | `local:NodeView` / `LinkView` / `GridDecorator` / `MinimapOverlay`；**没有 `TemplateSelector` 这一条**（改用隐式 `DataTemplate`） | `workflow-tree-view/TemplateClass.axaml:27,34,50,67` |
| MAUI | `local:NodeView` / `TemplateSelector` / `GridDecorator` / `LinkView` | `workflow-tree-view/TemplateClass.xaml:18,23,32,36` |
| Razor | 组件标签 `<GridDecorator>` / `<LinkView>` / `<MinimapOverlay>` / `<TemplateSelector>` / `<NodeView>` | `workflow-tree-view/TemplateClass.razor:17,26,35,46,60` |
| WinForms | `new TemplateSelector()`、`List<LinkView>` 字段、`NodeViewFactory`；**不引用 `GridDecorator`**（`PART_GridDecorator => PART_Canvas`），小地图只经接口引用 | `workflow-tree-view/TemplateClass.cs:55,70,96,150` |
| Jalium | 静态类/属性：`GridDecorator.RulerThickness`（`:38-39`）、`SlotView.*`（端口数学，`:249-258,359,389,423,533`）；`TemplateSelector` 是**宿主赋值的属性**（`:104`），它的类型由 selector 条目产出 | `workflow-tree-view/TemplateClass.cs:38,104,249` |

⇒ **改名是这里唯一会断的地方**：用户在生成时给某一条传了非默认 `-n`，必须回 tree-view 的手写照改。
没有任何工具、测试或编译器会提示你漏改了哪一处。

这一家在"引用兄弟"上还有两种别家没有的形态：

- **WinForms 的 `workflow-grid-decorator` 条目与 tree-view 无引用关系**：整个 tree-view 文件里
  `GridDecorator` 只出现在第一行注释，正文用的是 `PART_GridDecorator => PART_Canvas`（`:55`）
  与 `SetGridDecoratorName(this, "PART_Canvas")`（`:158`）。⇒ **生成不生成这一条，tree-view 行为不变**；
  其余六家的 tree-view 都真的引用它（WPF/WinUI/Avalonia/MAUI 与 Razor 在标记/组件里实例化，
  Jalium 读它的静态成员，见本节上表）。
- **WinForms 的 tree-view 与 minimap 是"类型级"耦合**：`IWorkflowMinimapScrollSource` 这个接口
  **不属于 Core、不属于适配器，只声明在 minimap 条目的产物里**
  （`workflow-minimap-overlay/TemplateClass.cs:332-334`，同一文件的 `TemplateClass` 是它唯一的实现），
  而 tree-view 在同命名空间下直接拿它做模式匹配（`:674`、`:686`）。
  ⇒ **只生成 `winforms-v-tree` 而不生成 `winforms-v-minimap`，生成出来的项目编译不过（CS0246）。**
  `grep IWorkflowMinimapScrollSource` 全仓 **15 处，全部落在 WinForms 一族**：模板 5 处
  （`workflow-minimap-overlay/TemplateClass.cs:17,20,332`、`workflow-tree-view/TemplateClass.cs:674,686`），
  两个 demo 各 5 处（`Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:124,141` +
  `…/Views/MinimapOverlay.cs:16,19,328`；`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/`
  下的 `MinimapOverlay.cs:17,20,332` + `TreeView.cs:682,694`）。
  **`Src/Core/` 与 `Src/Adapters/` 里零命中** —— 所以它不是契约，而是 WinForms 一族各自手写的同名接口：
  每多一个消费者就多一份声明，改名只会在这一族内部一起改。
- 对照：**WinForms 的 tree-view 喂给池的是全量 `Nodes`**（`ViewPool.SetItemsSource(PART_Canvas, _tree?.Nodes)`，`:728`），
  其余五家（WPF/WinUI/Avalonia/MAUI/Jalium）喂 `Helper.VisibleItems`；`Helper.VisibleItems` 在
  `Src/Adapters/VeloxDev.WinForms/` 里零命中，demo 同形
  （`Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/TreeView.cs:736`）。
  **Razor 与 WinForms 同族**：`workflow-tree-view/TemplateClass.razor:46` 的 `Items="Tree.Nodes"`，
  整个 Razor 模板目录零 `VisibleItems` 命中（demo 里它唯一的消费者是模板会删掉的 `InfoOverlay`，
  见 `adapters/razor.md` §二·6）。
- **Jalium 的 tree-view 还多耦合一个 `SlotView`**（端口数学与命中都用它的静态常量/方法，
  见本节上表），所以"少生成一条兄弟就编译不过"在这家覆盖两条：`GridDecorator` 与 `SlotView`
  （见 `adapters/jalium.md` §二·3）。

---

## 六、平台族：同一条目在七家的结构差异

差异不是装饰性的。三条轴，各自分族 —— **跨平台抄模板前先确认两边同族**。

### 轴 1 · 标尺默认厚度：28 与 36

| 值 | 平台 | 依据 |
|---|---|---|
| **28** | WPF、Avalonia、WinUI、MAUI、Razor | `workflow-grid-decorator/TemplateClass.cs:38`（WPF）、`…/Avalonia…/TemplateClass.cs:38`、`…/WinUI…/TemplateClass.cs:71`、`…/MAUI…/TemplateClass.cs:24`；Razor 是 `workflow-grid-decorator/TemplateClass.razor.cs:20` |
| **36** | WinForms、Jalium | `…/WinForms…/workflow-grid-decorator/TemplateClass.cs:21`、`…/Jalium…/workflow-grid-decorator/TemplateClass.cs:17` |

WinForms 的 36 是**有理由的、注释写明的偏离**：`workflow-grid-decorator/TemplateClass.cs:20` 写着
`// Other template code uses 28px, but WinForms reads visually smaller, so the default is enlarged to 36px.`
Jalium 的 36 **没有说明**。

⇒ **这个数字在别处被引用，且引用方式分两派**：

- **绑定式**（改厚度自动跟随）：WPF / Avalonia / WinUI 在 tree-view 里把 `TranslateTransform` 的 X/Y
  绑到 `PART_GridDecorator` 的 `RulerThickness`（WPF `workflow-tree-view/TemplateClass.xaml:58-59`）；MAUI 用
  `{Binding RulerThickness, Source={x:Reference PART_GridDecorator}}`（`workflow-tree-view/TemplateClass.xaml:41,46-47`）。
- **复制式**（改厚度必须手改）：Jalium 把它抄成 `RulerReserve = 36`（`workflow-link-view/TemplateClass.cs:30`，
  注释要求与 GridDecorator 的常量一起改），又在 node-view 里硬编码字面量 `+ 36`
  （`workflow-node-view/TemplateClass.cs:159-160`）；**WinForms 同形**：常量在 tree-view
  （`workflow-tree-view/TemplateClass.cs:37` 的 `RulerReserve`），node-view 里那处是硬编码的 `+ 36`
  （`workflow-node-view/TemplateClass.cs:419-420`）；Razor 在 tree-view 里写死 `RulerThickness="28"`
  （`workflow-tree-view/TemplateClass.razor:19`）。

### 轴 2 · 连线用什么方式画：四族

七家的 `workflow-link-view` **有一半的 XAML 是空壳**，看标记是看不出它怎么画的 —— 必须看代码。

| 族 | 平台 | 依据 |
|---|---|---|
| **立即模式**：继承一个能覆写绘制入口的元素，XAML 只是空壳 | WPF、Avalonia、Jalium、WinForms | WPF `workflow-link-view/TemplateClass.xaml` 全文是一个空 `<UserControl>`（5 行），几何全在 `.xaml.cs:66` 的 `OnRender`；Avalonia 同理（`TemplateClass.axaml` 是空 `<Control>`，`Render` 在 code-behind）；Jalium `workflow-link-view/TemplateClass.cs:232` 的 `OnRender`；WinForms 是 `Render(Graphics)`，由宿主调 |
| **保留式几何在代码里构造**：XAML 是空壳，`Path` + `PathGeometry` 在构造函数里 new 出来 | WinUI | `workflow-link-view/TemplateClass.xaml` 只有 6 行（`Clip="{x:Null}"` 是全部内容）；`TemplateClass.xaml.cs:26-28` 是 `Path` / `PathGeometry` / `PathFigure` 字段，`:57-58` 在 ctor 里 new 并 `Children.Add`。原因是这一家**没有公共 `OnRender`**，理由见 `memory/modules/WorkflowSystem/adapters/winui.md` §二·L2，此处不抄 |
| **标记语言里的元素 + 代码给几何字符串** | Razor | `workflow-link-view/TemplateClass.razor` 的 `<polyline points=…>`（虚线的 `stroke-dasharray="6 4"` 也在标记里） |
| **复用适配器的视口级图层** | MAUI | `workflow-link-view/TemplateClass.xaml:14` 直接放 `behaviors:WorkflowLinkOverlay`；文件头 `:2-7` 的注释写明取舍：**每条线一个 `GraphicsView` 会在深缩放下超出 Win2D 纹理上限并静默消失**，所以一个表面只放**一个** link 层 |

⇒ **WPF / WinUI 那一对容易看错**：WPF 的 tree-view 给 `LinkView` 绑了 `Width/Height` 到
`PART_Canvas` 的 `ActualWidth/ActualHeight`（`workflow-tree-view/TemplateClass.xaml:31-32`），
WinUI 的 tree-view **故意不绑**并在注释里写明理由（`workflow-tree-view/TemplateClass.xaml:24-26`：
`Width/Height are NOT bound here: LinkView drives its own box … never lags an ElementName ActualWidth binding`）。
两者最终都在代码里画，但"盒子谁给"是相反的。

### 轴 3 · minimap 是薄壳还是自带实现

| 族 | 平台 | 模板文件行数（实测） |
|---|---|---|
| 薄壳：继承/配置框架或适配器给的实现，只设颜色 | WPF 24、Avalonia 25、MAUI 21、Jalium 14、WinUI 42、Razor 79（`.razor` 15 + `.razor.cs` 64） | 见左 |
| **自带实现** | WinForms 335 | `workflow-minimap-overlay/TemplateClass.cs` |

⇒ **WinForms 那 335 行是唯一的例外**，原因是该家适配器里**没有"创建控件"的钩子**，
装饰器/小地图只能由用户代码提供 —— 推理链在 `memory/modules/WorkflowSystem/adapters/winforms.md` §一，
此处只指路。Jalium 那 14 行是七家里最薄的（一个空构造器：`workflow-minimap-overlay/TemplateClass.cs`）。

---

## 七、静默失败清单

### 7.1 24 个"命令行收得下、什么也不替换"的 symbol

`template.json` 里的 symbol 若 `type: parameter` 而**没有 `replaces`**，`dotnet new --help` 会把它列出来、
命令行能传、**不报错、也不替换任何文本**。逐文件核对的结果：共 24 个，分布在 11 个条目文件里，
**七个平台里有四个一个都没有**（WPF/Avalonia/WinUI 之外还有 MAUI 的 grid-decorator、minimap、node… 见下表）。

| 平台 | 条目 | 空转的 symbol |
|---|---|---|
| WPF | `workflow-slot-view` | `slotBorderColor` |
| WinUI | `workflow-slot-view` | `slotBorderColor` |
| Avalonia | `workflow-slot-view` | `slotColor`、`slotBorderColor` |
| MAUI | `workflow-slot-view` | `slotPath` |
| Jalium | `workflow-slot-view` | `slotBackground`、`slotColor`、`slotBorderColor`、`slotPath` |
| Razor | `workflow-slot-view` | `slotBackground` |
| Razor | `workflow-grid-decorator` | `gridBackground`、`minorGridColor`、`majorGridColor` |
| Jalium | `workflow-grid-decorator` | `gridBackground` |
| Jalium | `workflow-minimap-overlay` | `minimapBackground`、`minimapBorder`、`nodeFill`、`viewportStroke` |
| Razor | `workflow-tree-view` | `surfaceBorderBrush`、`surfaceBorderThickness`、`surfaceCornerRadius` |
| Jalium | `workflow-tree-view` | 同上三个 |

**只有其中一个自己承认了**：Razor 的 `workflow-slot-view` 里 `slotBackground` 的描述写着
`Accepted for cross-GUI CLI parity; this GUI's slot has no separate background surface.`
（`VeloxDev.Razor.Templates/working/content/workflow-slot-view/.template.config/template.json:54`）。
其余 23 个都是静默的。

分布上值得记住的两条：

- **`slotBorderColor` 在四个平台空转**（WPF/WinUI/Avalonia/Jalium），是覆盖面最广的一个。
- **WinForms 是唯一一个 `workflow-slot-view` 零空转符号的平台。**

⚠ **`slotPath` 特别容易骗人**：七个平台的 `defaultValue` 是**逐字节相同**的一段 SVG 路径
（viewBox 1024×1024 的圆环图标），但**只有五个真的替换它** ——
MAUI 与 Jalium 声明了却没有 `replaces`。⇒ 在 MAUI/Jalium 上传 `--slotPath` 什么也不会发生，
而图标看起来"是那段路径"，会让人以为换成功了。

⚠ **与人面向文档不一致，以代码为准**：`skills/veloxdev-create-workflow/references/templates.md:66-68`
只列出 15 个空转参数，而逐文件核对的结论是 **24 个**。文档没提到的 9 个是：

| 平台 | 条目 | 文档未列的空转 symbol |
|---|---|---|
| Jalium | `workflow-slot-view` | `slotBackground`、`slotColor`、`slotBorderColor`、`slotPath`（四个全空转） |
| Jalium | `workflow-grid-decorator` | `gridBackground` |
| Razor | `workflow-grid-decorator` | `gridBackground`、`minorGridColor`、`majorGridColor` |
| Razor | `workflow-slot-view` | `slotBackground` |

Razor 的 `workflow-slot-view` 是**唯一在 `template.json` 的 `description` 里自己承认**的一个
（`…/VeloxDev.Razor.Templates/working/content/workflow-slot-view/.template.config/template.json:54`：
`Accepted for cross-GUI CLI parity; this GUI's slot has no separate background surface.`），
其余 23 个都是静默的 —— 文档的口径是"pack says so in its own `template.json`"，对 Jalium 的四个不成立。

### 7.2 六份 csproj 里有一条匹配不到任何文件的 Pack glob

```xml
<None Include="..\..\skills\veloxdev-workflow-item-templates\references\**\*" Pack="true" PackagePath="references" Visible="false" />
```

WPF 在 `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj:27`，
Avalonia / WinUI / MAUI / Razor / WinForms 同位置同内容（`git grep` 六份都命中 `:27`）。

⇒ **这条路径本身是错的**：从 `working/` 上两级落在 `Src/Templates/`，所以它指的是
`Src/Templates/skills/veloxdev-workflow-item-templates/references/**\*` —— 而 `Src/Templates/skills`
**整个目录都不存在**。要够到仓库根的 `skills/` 得写**四级**（`..\..\..\..\skills\…`）。
⇒ **它匹配 0 个文件，不是因为仓库根 `skills/` 缺那个子目录**（那里是 `README.md` + 六个目录，
`veloxdev-add-aspects`、`veloxdev-create-animation`、`veloxdev-create-workflow`、
`veloxdev-drive-workflow-with-ai`、`veloxdev-switch-themes`、`veloxdev-write-viewmodels`，一家不缺），
而是因为**连目录都没走到**。**包内不会有 `references/` 目录**，而 `dotnet pack` 对空 glob 不报错。
**Jalium 没有这一行**（它的 csproj 只有两条 `None`，`:22-23`）。

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
| Jalium | 不绑，node-view 在 C# 里算画刷 | `workflow-node-view/TemplateClass.cs:135-138` |
| Razor | 不绑，slot-view 在 C# 里算 | `workflow-slot-view/TemplateClass.razor.cs:42-62` |
| WinForms | 不绑，slot-view 在 C# 里算（该家 slot-view 完全没有声明空转 symbol） | 见 `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-slot-view/TemplateClass.cs` |
| **Avalonia** | **没有** —— slot-view 里搜 `SlotState` 零命中 | `workflow-slot-view/TemplateClass.axaml:12-13` 只把 `<Path Fill>` 绑到 `$parent[UserControl].Foreground`，而模板里没有任何地方写 `Foreground` |

⇒ 在 Avalonia 上，插槽**永远不会变色**，而且不会报错。要修就得给 slot-view 加一个状态属性再绑上去。

### 7.5 渲染就绪门：只有三家真的写了

| 有门 | 写法 | 依据 |
|---|---|---|
| WPF | `link.IsRenderReady()` | `workflow-link-view/TemplateClass.xaml.cs:71` |
| Razor | `WorkflowSlotUpdateGate.IsLinkRenderReady(link)` | `workflow-link-view/TemplateClass.razor.cs:184` |
| WinForms | `WorkflowSlotUpdateGate.IsLinkRenderReady(_link)` | `workflow-link-view/TemplateClass.cs:268` |

| 无门 | 它们各自有的东西 |
|---|---|
| Jalium | 端点回退时的 `double.IsNaN` 检查（`workflow-link-view/TemplateClass.cs:152`） |
| Razor | 除了门，还有一道显式 `double.IsNaN` 守卫（`workflow-link-view/TemplateClass.razor.cs:193`）—— 七家里唯一两道都有 |
| Avalonia | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.axaml.cs` 只有 `CanRender`） |
| WinUI | 无门、无 NaN 守卫（`workflow-link-view/TemplateClass.xaml.cs` 只有 `CanRender`） |
| MAUI | 无门 |

⇒ `WorkflowSystem/extension.md` 那条"连线视图首行过渲染就绪门"的契约，**模板侧只有三家落地**。

### 7.6 WinForms 少生成一条兄弟条目 ⇒ 编译不过（且没有任何地方写明）

`IWorkflowMinimapScrollSource` 只在 minimap 条目的产物里声明（`workflow-minimap-overlay/TemplateClass.cs:332-334`），
tree-view 直接用它做模式匹配（`workflow-tree-view/TemplateClass.cs:674,686`）。
⇒ 只生成 `winforms-v-tree` 得到的是**编译不过**的代码（CS0246）。详见 §五。
这一条与 §7.3 的 Avalonia 不同：那一处的注释自己写明了"build fails HERE"，
**这一处没有任何文件提到过**，只能读代码发现。

---

## 八、我要改 X → 打开哪个文件

| 我要改 | 先打开 |
|---|---|
| 某平台某个部件生成后长什么样 | 那一条目目录下的 `TemplateClass.*` |
| 外壳结构（`PART_*` 嵌套、绑定关系） | 该平台的 `workflow-tree-view/TemplateClass.*` |
| 标尺厚度 | 该平台 `workflow-grid-decorator` 的 `RulerThickness`，**加** Jalium `workflow-link-view/TemplateClass.cs:30`，**加** Jalium `workflow-node-view/TemplateClass.cs:159-160`，**加** Razor `workflow-tree-view/TemplateClass.razor:19` |
| 条目对外的 CLI 参数 | 该条目 `.template.config/template.json` 的 `symbols`（**记得 `replaces`**，否则就是 §7.1）+ `dotnetcli.host.json` 的 `symbolInfo` |
| 条目名 / 命令名 / 默认类名 | `.template.config/template.json` 的 `identity` / `shortName` / `defaultName`，**并回头查 tree-view 里对它的引用**（§五） |
| 新增一个平台 | `extension.md` §三 |
