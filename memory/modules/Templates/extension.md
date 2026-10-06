# Templates — 扩展

> 服务于"改模板要走什么路"。**契约本身**（七个视图角色、附着属性、注册位置、`Viewport` 谁写、渲染就绪门、
> 滚轮方向、缩放提交顺序）在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3，本文不重复。
> 人面向的"怎么用这些模板"在 `skills/veloxdev-create-workflow/references/templates.md`。
> 路径约定同 `architecture.md`：相对 `Src/Templates/`；引用 Core、适配器、demo、skill 时写全路径。

---

## 一、先确认一件事：模板内容没有生成器，也没有脚本

上一版容易犯的错是"以为模板是生成的、改一处就同步了"。**不是。**

| 探针 | 结果 |
|---|---|
| `Src/Generators/VeloxDev.Core.Generator` 里搜 `Templates` | **零命中** |
| 本仓库搜脚本（`.ps1` / `.sh` / `.yml` / `.py`）里的 `Templates` | 2026-10-04 起 `Src/Verification/verify-workflow-item-templates-all.ps1` 会引用 `Src/Templates/`（旧的 `verify-workflow-item-templates.ps1` / `verify-jalium-item-templates.ps1` 现在是它的薄转发）；其余脚本零命中 |
| `Src/Templates/` 下的 `.md` / `.sln` / 测试 | `find -name "*.md"` 零命中 |

⇒ **49 个条目全部手写维护。下面所有清单都是"你必须亲手去改的地方"，不是"某个脚本会替你跟上的地方"。**

唯一会自动跟上的是 `template.json` 的**文本替换**（`replaces`），而它只在**生成那一刻**生效：
把 `TemplateClass` / `TemplateNamespace` / `TemplateSlotColor` 这类 token 换成用户传的值。
⇒ 加一个新 symbol 的正确做法是**两件事一起做**：在 `symbols` 里加带 `replaces` 的条目，
**并且**在模板文件里把对应字面量写成那个 token。只加 `symbols` 不写 token 就是
`architecture.md` §7.1 那 12 个空转参数之一。

**改动怎么验证**：模板项目虽然进了 `VeloxDev.slnx:168-175`，但
`EnableDefaultCompileItems=false` + `IncludeBuildOutput=false`（`VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj:16,18`）
⇒ 构建它等于什么都不做，**生成的代码编译不过不会被任何构建发现**。

**2026-10-04 起七家共用一条自动化**：入口 `Src/Verification/verify-workflow-item-templates-all.ps1`
（`-Platform <name…>` 默认七家全跑，另有 `-Strict`、`-KeepProbe`）。旧的两条现在是薄转发：
`verify-workflow-item-templates.ps1` → `-Platform WinForms`、`verify-jalium-item-templates.ps1` → `-Platform Jalium`，
已有调用照旧。对每家它证明：七个 CLI 短名能解析、**没有任何 `replaces` 占位符漏进生成文件**（脚本读该平台所有
item template 的 `replaces` token，逐个在生成文件里搜，残留即“模板引用了自己没声明的 symbol”）、
`primaryOutputs` 完整、七个生成文件能对着适配器一起编译、模板文本 == `Examples/Workflow/<GUI> Trimmed` 的镜像
（机械改动除外）；证明不了任何运行期行为，也分不出颜色对不对（错的颜色照样编译）。

⚠ **占位符守卫能抓到 pack 的真缺陷**（第一次跑就抓到 Jalium 的）：**`workflow-link-view`** 模板引用了 `TemplateLinkColor`，
而该条 `template.json` 必须声明这个 symbol —— `workflow-link-view/.template.config/template.json:33`
声明了 `"replaces": "TemplateLinkColor"`（默认值 `#DDFFFFFF`），源码侧因此**保持占位符 token、不写死颜色**
（`workflow-link-view/TemplateClass.jalxaml.cs:102` 把它交给 `ParseColor`）。
这一轮全平台化同时补了四对镜像同步：WinUI `LinkView`、MAUI `LinkView` / `TreeView` / `SlotView`、WPF `SlotView`。

⚠ **它第一次跑就照出五对既有漂移**，其中 decorator 那处是**真的值差异**（模板 `#C8252526` vs 镜像
`#70252526`）而不是措辞 —— 也就是说规格 §一「模板是源、镜像跟随」这条规则一直没有人执行过：
从那以后没有任何东西检查过它们。**2026-10-03 已全部清掉**（decorator 按镜像的注释把**模板**的 symbol 默认值
降到 0x70 并补上理由，其余是把镜像从模板机械派生一次）。现在七对里只有 tree-view 逐字不同 ——
那是 `Examples` 侧的 HUD，属五类机械改动之一，脚本把它标成 expected 而不计入失败。⇒ **可以开 `-Strict` 了。**

### 1.1 那它们是从哪来的：**`Examples/Workflow/<GUI> Trimmed/` 的逐文件衍生**

"没有生成器"不等于"凭空手写"。实测：**每个条目文件都是该平台 `Trimmed` demo 里同名文件的一份改名副本**，
差异只有下面五类机械改动。这不是推理，是可逐行量出来的：

| 平台 | 条目 | 模板行数 / demo 行数 | 逐行差异行数 |
|---|---|---|---|
| WinForms | `workflow-template-selector` | 20 / 19 | **1**（多出的那行是模板侧的注释：`workflow-template-selector/TemplateClass.cs:17`「连线视图由适配器那份『附加』收绑…」；其余是命名空间与类名的机械改名） |
| WinForms | 其余六个条目 | 22–436 / 22–432 | tree 35 / node 24 / slot 17 / link 60 / grid 20 / minimap 8 |
| Razor | `workflow-tree-view` 的 `.razor` / `.razor.cs` | 90 / 91、35 / 34 | 11 / 1 |
| WPF | `workflow-tree-view` 的 `.xaml` / `.xaml.cs` | 76 / 84、12 / 11 | 48 / 1 |
| MAUI | `workflow-tree-view` 的 `.xaml` / `.xaml.cs` | 69 / 77、11 / 20 | 68 / 9（demo 已按每线一视图分叉，见 §五） |

对照方法（可复现）：把模板文件的 `TemplateNamespace` 换成该 demo 的命名空间、`TemplateClass` 换成
demo 的类名，再 `diff`；文件是 CRLF，比对前要去掉行尾 `\r`，否则整份文件都会被算成"差异"。

demo 侧位置（每个平台一个目录，**七个角色 + 一个 `InfoOverlay` 共八个文件**）：

| 平台 | demo 目录 |
|---|---|
| WPF | `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/` |
| Avalonia | `Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/` |
| WinUI | `Examples/Workflow/WinUI Trimmed/Demo/Views/Workflow/` |
| MAUI | `Examples/Workflow/MAUI Trimmed/Demo/Controls/Workflow/` |
| Blazor | `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/` |
| WinForms | `Examples/Workflow/WinForms Trimmed/Demo/Views/Workflow/` |
| Jalium | `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/` |

五类机械改动（**改模板前先看这几条，能省掉一半的"重新设计"**）：

| # | 改什么 | 例 |
|---|---|---|
| 1 | 命名空间与类名 token 化 | `TemplateNamespace` / `TemplateClass` |
| 2 | 字面量换成 `Template*` 占位符，并在 `template.json` 里配 `replaces` | WinForms demo 的 `TreeView.cs` 写 `ParseColor("#1E1E1E")`，模板写 `ParseColor("TemplateSurfaceBackground")`（`workflow-tree-view/TemplateClass.cs:20-23` 四行同形）；WPF demo 的 `<Border … Background="#1E1E1E" BorderBrush="#33FFFFFF" BorderThickness="1">` 在模板里是四个 `Template*`（`workflow-tree-view/TemplateClass.xaml:46-49`） |
| 3 | **删掉 demo 的 `InfoOverlay` HUD 及其绑定** | 模板里没有任何 InfoOverlay；demo 每个平台都有（如 `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/InfoOverlay.razor`，模板 tree-view 只把它连同 `<InfoOverlay …>` 那一行一起拿掉） |
| 4 | **兄弟类型改名**：demo 用 `WorkflowGridDecorator` / `CustomTemplateSelector`，模板条目必须叫 `GridDecorator` / `TemplateSelector` | WPF demo `TreeView.xaml` 里是 `<workflowViews:WorkflowGridDecorator>`，模板里是 `<workflowViews:GridDecorator>`；因为 tree-view 是按 `defaultName` 引用兄弟的（`architecture.md` §五） |
| 5 | 头注释换成 `… VeloxDev customization: …` 那一句 | 每个条目文件的第一行 |
| 6 | **把注释翻成英文**：demo 的注释是简体中文，模板产物一律英文、一行说清、只标扩展点 | [code-comment-specifications.md](../../specifications/code-comment-specifications.md) §五；七家 `workflow-tree-view` 2026-10-03 已清到零中文 |

⇒ **要改一个条目的内容，第一步是打开 demo 的对应文件，不是打开模板文件**；
两边不一致时，先判断是"模板落后于 demo"还是"模板刻意偏离"（第 3、4 类就是刻意的）。
⚠ 仍然**没有脚本**做或检查这个同步，也没有任何东西保证某天它们还对应得上。

---

## 二、改一个条目：必须同时动的地方

看"模板长什么样"像是一个文件的事，实际有四层。**漏第 2、3 层是静默的**：

| 层 | 改哪 | 漏了会怎样 |
|---|---|---|
| 1 · 模板内容 | `workflow-<角色>/TemplateClass.*`（源头通常是 `Examples/Workflow/<GUI> Trimmed/` 的同名文件，见 §1.1） | —— |
| 2 · token 与 CLI | `.template.config/template.json` 的 `symbols`（**要 `replaces`**）+ `.template.config/dotnetcli.host.json` 的 `symbolInfo` | 参数在 CLI 上叫不出短名（`-ns` / `-bg` 这类）；只加 `symbols` 不加 token 则**彻底空转**，`--help` 里还看得见 |
| 3 · 产物清单 | 同文件 `primaryOutputs` | 观察到的规律：它列出的路径**恰好等于条目目录里除 `.template.config/` 外的全部文件**（49/49 如此）。⇒ 新增一个源文件时必须把它加进去，否则它不在"产物"里出现 |
| 4 · 引用它的 tree-view | 同平台的 `workflow-tree-view/TemplateClass.*` | 见 `architecture.md` §五：tree-view 用 `defaultName` 的字面类型名引用兄弟，改名即断链，且**不会报错**（编译不过是最好结果） |

---

## 三、新增一个平台：步骤清单

前提：该平台的适配器已就位（`Src/Adapters/VeloxDev.<平台>/Attached/Workflow/` 七角色齐全）。

1. **建项目**：`Src/Templates/VeloxDev.<平台>.Templates/`。
   - 目录结构照**六家的多数形态**（csproj 与 `content/` 同在 `working/` 下），**不要照 Jalium** ——
     它的离群结构（`architecture.md` §二 那四条）会让 `content\**\*` 与 logo 的相对路径同时错。
   - csproj 整份抄 `VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj`，只改
     `PackageId` / `Title` / `Description` / `PackageTags` 四处。
   - ⚠ **不要抄第 27 行**（`..\..\skills\veloxdev-workflow-item-templates\references\**\*`）——
     这条相对路径**够不到仓库根的 `skills/`**（从 `working/` 上两级是 `Src/Templates/`，
     而 `Src/Templates/skills` 不存在），是六份包共有的悬空 glob，见 `architecture.md` §7.2。
2. **加进解决方案**：`VeloxDev.slnx` 的 `/Templates/` 文件夹（现有七行在 `:168-175`）。
3. **建七个条目目录**：名字必须与另外六家**逐字相同**（`workflow-grid-decorator`、
   `workflow-link-view`、`workflow-minimap-overlay`、`workflow-node-view`、`workflow-slot-view`、
   `workflow-template-selector`、`workflow-tree-view`）。
4. **每个条目写三份东西**：
   - `TemplateClass.<ext>`，命名空间写 `namespace TemplateNamespace;` / `clr-namespace:TemplateNamespace`；
   - `.template.config/template.json`：`identity = VeloxDev.<平台>.<角色名>`、
     `shortName = <平台小写>-v-<后缀>`（后缀表在 `architecture.md` §三）、`sourceName = "TemplateClass"`、
     `defaultName = <角色名>`、`preferNameDirectory: false`、`tags.type = "item"`、`primaryOutputs`；
   - `.template.config/dotnetcli.host.json`：给**每一个** symbol 一个 `longName` 与 `shortName`。
5. **tree-view 里按 `defaultName` 引用另外六个**。引用语法看该平台的适配器形状 ——
   七家的写法对照表在 `architecture.md` §五，照着同族的抄。
6. **对照该平台已有的 demo 逐文件搬**（`Examples/Workflow/<GUI> Trimmed/`）：七个角色一对一，删掉 `InfoOverlay`，
   再按 §1.1 那五类机械改动处理。**这是最快的路** —— 从零设计一遍几乎必然与 demo 分叉，而 demo 是唯一跑过的版本。

---

## 四、官方做法 vs 看着能编译、但错的捷径

### 4.1 捷径：让模板之间共享代码（加项目引用、抽公共基类）

**没有地方放。** 模板项目不编译一行 C#（`EnableDefaultCompileItems=false`），
七份 csproj 里**零** `PackageReference` / `ProjectReference`。
七个条目是**七个平级的文本产物**，它们只是碰巧共用同一个 `TemplateNamespace` token
（WPF `workflow-tree-view/TemplateClass.xaml:5-6` 的两个 `xmlns` 前缀都指向它）。

⚠ **这条只管「模板项目之内」。它不禁「适配器包发基类、生成代码继承」** —— 那是另一条通道，
而且早就在用：生成文件引用适配器程序集（`TemplateClass.xaml:7` 的 `assembly=VeloxDev.WPF`）本来就要
用户自己装包，minimap 这个角色在六家就是「包里发控件 + 模板薄派生」（WPF 24 / Avalonia 25 / WinUI 42 /
MAUI 21 / Jalium 24 / Razor 15 行）。

**2026-10-03 起 WinForms 的七项全部走这条**（Jalium 在 2026-10-05 转标记驱动后不再派生基类，见下面第二条）。

- WinForms（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`）：
`WorkflowTreeView`（装配、网格、分层窗口标尺、平移引擎、视图池）、`WorkflowNodeView`（绑定、定位、缩放折叠、
反射读标题/输入口/插槽标签）、`WorkflowSlotAttachment`（含那个 SVG 路径解析器，`SvgPathParser.cs:20` 起）、`WorkflowLinkAttachment`
（雕窗口区域、端点订阅、几何）、`WorkflowMinimapOverlay`、`WorkflowGridDecorator`（网格与标尺的绘制）、
`WorkflowTemplateSelector`（四个工厂与分流）。模板合计 3257 → 581 行。
- Jalium（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`，9 文件 3435 行）：**2026-10-05 起不再走这条** ——
角色落成附着行为（`WorkflowSurfaceBehavior` / `WorkflowSlotLayoutBehavior` / `WorkflowNodeDragBehavior` /
`WorkflowSlotConnectionBehavior` / `WorkflowEvents` / `WorkflowLinkBounds`）+ 小地图 `WorkflowMinimapOverlay` +
池 `ViewPool` / `ViewManager`；网格装饰器归**模板**。模板合计 1079 行（四个 `.jalxaml` 条目 + 三个 `.cs`）。
见 `../WorkflowSystem/adapters/jalium.md` §一。

判据是**「这段代码是不是用户该改的扩展点」**：平台硬限制与机械装配进包，策略（调色板、卡片长什么样、用哪个图形）留在模板里。

⚠ **判据不是「这个角色像不像用户的」，也不是「离群不离群」** —— 后者不能当理由跳过 `grid-decorator`
（279 行落在其余六家 110–535 的中间），那个角色照样含平台机制。用户把
「每一个需要 item template 的角色都要有可重写基类」定成了规范，见
[`adapter-base-class-specifications.md`](../../specifications/adapter-base-class-specifications.md)。

⇒ 已经"抽"过的公共物只有一件：`TemplateSlotPath` 那段 SVG。它是**每个条目的 `template.json` 里各存一份字面量**
（七个平台的 `defaultValue` 逐字节相同，viewBox 1024×1024），**改图标要改七处**，
而且其中一处（MAUI）根本没有 `replaces`。

### 4.2 捷径：从另一家复制条目，改后缀

**多数情况下是对的**（七家条目同集同形），但**先确认两边同族**。
三条分歧轴及其族划分在 `architecture.md` §六；抄错轴的典型后果是"生成成功、编译通过、就是不显示"
—— 例如把 WPF 的 link-view（声明式元素 + 代码后置写点）抄给 MAUI（单一视口级 link layer）。

### 4.3 捷径：给空转参数补一个 `replaces` 就完事

**先确认这一家真的读这个值。** `replaces` 只做文本替换；如果该家**没有对应的绘制面**，
补上只会把 token 换成一个没人读的字面量 —— 反而让 CLI 看起来"支持了"，比现在更难发现。
Razor 的 `slotBackground` 就是这一类的正面样本：它的 `description` 自陈
`this GUI's slot has no separate background surface`
（`…/VeloxDev.Razor.Templates/working/content/workflow-slot-view/.template.config/template.json:54`），
于是**刻意留成空转**。

### 4.4 捷径：把 Avalonia tree-view 的 `x:DataType` 改掉，让它能编译

`vm:TreeViewModel` 是**故意**指向用户类型的占位（`architecture.md` §7.3，
`workflow-tree-view/TemplateClass.axaml:1-4` 的注释把这件事写明了）。
要改的是"让用户填对"，不是"让模板自己编过"。

### 4.5 捷径：改常量只改一处

三根跨文件复制的常量，改一处会留下不一致且不报错：

| 常量 | 被复制到 |
|---|---|
| 标尺厚度（28 或 36） | 模板侧唯一副本是 **Razor**：`workflow-tree-view/TemplateClass.razor:20` 的 `RulerThickness="28"`。**WinForms 与 Jalium 都是单一来源**：WinForms 在适配器 `Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowGridDecorator.cs:31` 的 `DefaultRulerThickness`（tree-view 的 `RulerReserve` 读它，`WorkflowTreeView.cs:47`，2026-10-03 起），Jalium 在**模板** `workflow-grid-decorator/TemplateClass.cs:34` 的 `DefaultRulerThickness = 36`（2026-10-05 起），模板其余文件零副本 |
| 节点设计尺寸 `260×180` | WPF node-view `TemplateClass.xaml:17` / WinUI `:18` 的 `Grid Width/Height`、MAUI `workflow-node-view/TemplateClass.xaml.cs:6` 的 `DesignWidth`、Razor `workflow-node-view/TemplateClass.razor.cs:77`、Jalium `workflow-node-view/TemplateClass.jalxaml` 的 `Grid Width="260"/Height="180"` |
| 连线控制点最小拉出量 `40`（配 `0.5·\|dx\|`） | 模板各自一份命名常量：Avalonia `TemplateClass.axaml.cs:19`、WPF `:19`、WinUI 是字面量 `40`（`:300`）、Razor `PullMinimum`（`:20`）；**Razor 再加适配器 JS 的 `LINK_PULL_MIN`**（`Src/Adapters/VeloxDev.Razor/wwwroot/veloxdev.workflow.js:262`）；无标记语言的 WinForms 与 MAUI 在适配器各一份（WinForms `WorkflowLinkAttachment.cs` 的 `pullMinimum`、MAUI `WorkflowLinkOverlay.cs:304`），Jalium 在模板 `workflow-link-view/TemplateClass.jalxaml.cs` 的 `MinimumPull = 40`。镜像 demo 各存对应的那一份。**Razor 那两份最危险**：JS 在缩放塌缩那一帧独立重算同一条曲线，两边公式一岔就闪回旧形状 |

绑定式的那几家（WPF/Avalonia/WinUI/MAUI 的 tree-view 把 `TranslateTransform` 绑到
`PART_GridDecorator.RulerThickness`）会自动跟随，**复制式的那几处不会**。

---

## 五、一个平台内的七条是"一套"，不是"七选一"

七个条目生成的是**一个宿主 + 六个部件**，所以联动发生在**同一个平台内**，而不只是跨平台：

- 七条 `dotnet new` 必须同一个 `--namespace`（`architecture.md` §四）。
- 给某条传了非默认 `-n` 时，必须回 tree-view 手改对它的引用（§二 第 4 层）。
- 改了一个角色的**公开形状**（构造参数、DP/BindableProperty 名、`PART_*` 名），
  tree-view 里对它的绑定一起改 —— 而 `PART_*` 名字本身是**适配器侧**的契约，
  见 `memory/modules/WorkflowSystem/extension.md` §3.9 与
  `skills/veloxdev-create-workflow/references/view-layer.md`，本文不重抄。
- 该平台"模板生成之后还需要手工做什么"的人面向清单在
  `skills/veloxdev-create-workflow/references/gui/<平台>.md` 的 `## Item templates` 一节。

---

## 六、自查

1. 改的是模板**内容**，还是**包元数据**（`symbols` / `primaryOutputs` / `defaultName`）？两处都要动。
2. 新加的 symbol 有 `replaces` 吗？模板文件里有对应的 token 吗？**两个都"是"才有效。**
3. 新增的文件进 `primaryOutputs` 了吗？
4. 动过某个条目的名字或公开形状吗？tree-view 里对它的引用改了吗？
5. 动过标尺厚度 / 设计尺寸 / 连线最小拉出量吗？§4.5 表里那几处复制点都改了吗？
6. 七条命令的 `--namespace` 是不是同一个？
7. 新增平台时，抄的那个平台与目标平台**同族**吗（`architecture.md` §六 的三条轴）？
