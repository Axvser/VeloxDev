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
| 本仓库搜脚本（`.ps1` / `.sh` / `.yml` / `.py`）里的 `Templates` | **零命中**（`skills/` 下没有任何脚本文件，全仓这类脚本都不引用 `Src/Templates/`） |
| `Src/Templates/` 下的 `.md` / `.sln` / 测试 | `find -name "*.md"` 零命中 |

⇒ **49 个条目全部手写维护。下面所有清单都是"你必须亲手去改的地方"，不是"某个脚本会替你跟上的地方"。**

唯一会自动跟上的是 `template.json` 的**文本替换**（`replaces`），而它只在**生成那一刻**生效：
把 `TemplateClass` / `TemplateNamespace` / `TemplateSlotColor` 这类 token 换成用户传的值。
⇒ 加一个新 symbol 的正确做法是**两件事一起做**：在 `symbols` 里加带 `replaces` 的条目，
**并且**在模板文件里把对应字面量写成那个 token。只加 `symbols` 不写 token 就是
`architecture.md` §7.1 那 24 个空转参数之一。

**改动怎么验证**：没有自动化路径。模板项目虽然进了 `VeloxDev.slnx:166-173`，但
`EnableDefaultCompileItems=false` + `IncludeBuildOutput=false`（`VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj:16,18`）
⇒ 构建它等于什么都不做，**生成的代码编译不过不会被任何构建发现**。唯一可信的验证是
`dotnet new install` 本地包 → 在真实项目里生成 → 对照该平台的 demo 看差异。

### 1.1 那它们是从哪来的：**`Examples/Workflow/<GUI> Trimmed/` 的逐文件衍生**

"没有生成器"不等于"凭空手写"。实测：**每个条目文件都是该平台 `Trimmed` demo 里同名文件的一份改名副本**，
差异只有下面五类机械改动。这不是推理，是可逐行量出来的：

| 平台 | 条目 | 模板行数 / demo 行数 | 逐行差异行数 |
|---|---|---|---|
| WinForms | `workflow-template-selector` | 36 / 36 | **0**（改完命名空间与类名后逐字节相同） |
| WinForms | 其余六个条目 | 279–1151 / 282–1162 | 8 / 23 / 25 / 25 / 25 / 27 |
| Razor | `workflow-tree-view` 的 `.razor` / `.razor.cs` | 85 / 86、259 / 258 | 11 / 5 |
| WPF | `workflow-tree-view` 的 `.xaml` / `.xaml.cs` | 71 / 79、12 / 11 | 50 / 3 |
| MAUI | `workflow-tree-view` 的 `.xaml` / `.xaml.cs` | 62 / 62、107 / 118 | 54 / 21 |

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
| 2 | 字面量换成 `Template*` 占位符，并在 `template.json` 里配 `replaces` | WinForms demo 的 `TreeView.cs` 写 `ParseColor("#1E1E1E")`，模板写 `ParseColor("TemplateSurfaceBackground")`（`workflow-tree-view/TemplateClass.cs:63-66` 四行同形）；WPF demo 的 `<Border … Background="#1E1E1E" BorderBrush="#33FFFFFF" BorderThickness="1">` 在模板里是四个 `Template*`（`workflow-tree-view/TemplateClass.xaml:42-45`） |
| 3 | **删掉 demo 的 `InfoOverlay` HUD 及其绑定** | 模板里没有任何 InfoOverlay；demo 每个平台都有（如 `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/InfoOverlay.razor`，模板 tree-view 只把它连同 `<InfoOverlay …>` 那一行一起拿掉） |
| 4 | **兄弟类型改名**：demo 用 `WorkflowGridDecorator` / `CustomTemplateSelector`，模板条目必须叫 `GridDecorator` / `TemplateSelector` | WPF demo `TreeView.xaml` 里是 `<workflowViews:WorkflowGridDecorator>`，模板里是 `<workflowViews:GridDecorator>`；因为 tree-view 是按 `defaultName` 引用兄弟的（`architecture.md` §五） |
| 5 | 头注释换成 `… VeloxDev customization: …` 那一句 | 每个条目文件的第一行 |

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
2. **加进解决方案**：`VeloxDev.slnx` 的 `/Templates/` 文件夹（现有七行在 `:166-173`）。
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

⇒ 已经"抽"过的公共物只有一件：`TemplateSlotPath` 那段 SVG。它是**每个条目的 `template.json` 里各存一份字面量**
（七个平台的 `defaultValue` 逐字节相同，viewBox 1024×1024），**改图标要改七处**，
而且其中两处（MAUI/Jalium）根本没有 `replaces`。

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
| 标尺厚度（28 或 36） | Jalium `workflow-link-view/TemplateClass.cs:30` 的 `RulerReserve`、Jalium `workflow-node-view/TemplateClass.cs:159-160` 的字面量 `+ 36`、**WinForms `workflow-node-view/TemplateClass.cs:419-420` 的字面量 `+ 36`**（该家 tree-view 的常量叫 `RulerReserve`，在 `workflow-tree-view/TemplateClass.cs:37`，两个文件没有共享类型所以只能各写一份）、Razor `workflow-tree-view/TemplateClass.razor:19` 的 `RulerThickness="28"` |
| 节点设计尺寸 `260×180` | WPF/WinUI node-view 的 `Grid Width/Height`、MAUI `workflow-node-view/TemplateClass.xaml.cs:6` 的 `DesignWidth`、Razor `workflow-node-view/TemplateClass.razor.cs:77`、Jalium `workflow-slot-view/TemplateClass.cs:19-20`（Jalium 的 node/link/tree 三处都读它） |
| 黄金比 `0.6180339887` | 六个非 Bootstrap 派生的 link-view 与 Razor/MAUI 的 grid 代码各存一份 |

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
5. 动过标尺厚度 / 设计尺寸 / 黄金比吗？§4.5 表里那几处复制点都改了吗？
6. 七条命令的 `--namespace` 是不是同一个？
7. 新增平台时，抄的那个平台与目标平台**同族**吗（`architecture.md` §六 的三条轴）？
