# 无标记语言平台的适配器基类

> 用户口述、agent 落笔。约束**没有标记语言的那几家平台的适配器**与它们的 item template。
> **触发**：给一个无标记语言的平台新增或修改适配器 / 它的 item template 之前；判断「这段代码该进适配器包还是留在模板里」之前；给这类平台新增一个模板角色之前。
> 判据「这家算不算无标记语言」在 §一，不需要读代码。

---

## 一、适用范围：无标记语言的那一家

**判据：这一家的 item template 产物里没有标记文件 —— 产物是纯 `.cs`。**

- **不适用**于有标记语言的平台：WPF / Avalonia / WinUI / MAUI（XAML）、Razor / Blazor（`.razor`）、
  **Jalium（`.jalxaml`）**。它们的模板把布局与装配交给框架，本来就只有几十行。
  Razor 的行数比 XAML 那几家高，但那是组件模型而不是「没有标记」，**仍不适用**。
- **适用**于纯代码：**WinForms**。

⚠ **2026-10-05 改判 —— Jalium 移出这份名单**。它原先在里面，依据是「它的模板产物是纯 `.cs`」。
**那是把现状当成了判据。** 判据问的是「这一家**有没有**平台自带的标记语言」，不是「现有模板**用没用**它」。
Jalium 两样都齐：完整 `.jalxaml` 工具链（`Jalium.UI.Build` 的 `EnableDefaultJalxamlItems` +
`**\*.jalxaml` 通配 + `JalxamlCodeBehindTask`）与一个 WPF 同构的 UI 框架（`UserControl` /
`ItemsControl` / `DataTemplate` / `DataTemplateSelector` / `FindName` / `ItemContainerGenerator`）。
它的模板当时是纯 `.cs`，只是**没用** —— 因为这条判据先把它判成了「没有」，而后续一切绕路都从这一步长出来。

⇒ 新增一家平台时，先答「它有没有标记语言」—— 去查它的 SDK 与运行时里有什么，**不要**看现有模板长什么样。

---

## 二、规则

**适配器包必须为每一个「需要 item template 的角色」提供一个可重写的基类；模板产物派生它，只做简单重写。**

两条判据：

### 2.1 基类收「平台机制」，模板留「策略」

分界线只有一条：**这段代码是不是用户该改的扩展点。**

| 进基类 | 留模板 |
|---|---|
| 平台硬限制（WinForms 的分层窗口标尺、`BackColor` 必须 alpha 255、`AutoScrollMinSize` 不能赋值） | 调色板 |
| 机械装配（`PART_*` 的名字与搭壳、附着行为的注册） | 要画什么图形（插槽的路径、连线的曲线形状） |
| 视图池接线、布局调度 | 卡片长什么样 |
| 坐标换算、缩放折叠、每帧记账 | 用哪个模板选择器 |
| 反射读模型里的名字（标题、输入口、插槽标签） | |

**模型事件也是基类的扩展点**（2026-10-03 用户定）：无标记语言的两家没有附加属性可挂，所以**基类必须为它管的那个角色提供可重写的钩子** ——
`WorkflowNodeView` 出 `OnMoving/OnMoved/OnResizing/OnResized/OnDeleting/OnDeleted`、`WorkflowSlotView` 出 `OnChannelChanging/OnChannelChanged`、
`WorkflowTreeView` 出 `OnConnecting/OnConnected`（都 `protected virtual`，默认空实现）。基类自己用 `WorkflowEventRelay.Attach(...)` 接上模型事件并转发进这些钩子，
宿主（模板产物）只需重写。转发逻辑在 Core 一份，七家共用 —— 不要在基类里重新订阅一遍 Helper 的事件。

**基类必须是 `public`、非 `sealed`**，扩展点是 `protected virtual` / `protected abstract`，或既有的公开接口
（如 `IWorkflowTemplateSelector` / `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay`）—— 能复用接口就不要新造虚方法。

**钩子的命名规则：`On` + 事件名，参数就是那次事件的 args。** 与上面那组模型事件钩子同一条规矩
（`OnMoving(NodeMoveEventArgs)`）。
⚠ 名字**不许断言效果** —— 写「指针进来了」，不写「该高亮了」：用户订的是事件，做什么由他决定
（`OnPaintHighlight` 那种名字已经被否掉，见 [layer-ownership-specifications.md](layer-ownership-specifications.md) §1.4）。
**两家给同一个概念时必须同名同形**：`OnRenderHighlight` vs `OnPaintHighlight` 那种只差一个动词的分裂，等于让用户学两套。

### 2.1.1 「视图类角色」给的是「附加」，不是基类

**2026-10-04 用户定**：一个角色的产物若是**用户自己要画的东西**，它就**不该被藏进基类** —— 产物应该像一个
**视图**（用户写 `OnPaint` / `OnRender`），适配器给的是一个**可以附到控件上的助手**，在用户构建视图时顺带把
其余机制挂上去。

**判据是「这个角色的产物里有没有属于用户的画」，不是「它是不是工作流组件」**：

| 角色 | 形态 | 为什么 |
|---|---|---|
| `workflow-link-view` / `workflow-node-view` / `workflow-slot-view`（**WinForms**；Jalium 2026-10-05 移出，见 §一） | `public sealed class X : Control` / `: Canvas` / `: UserControl`，构造里一行 `Workflow<角色>Attachment.Attach(this)`，自己在 `OnPaint` / `OnRender` 里画 | 这三样的样子**就是**用户的：卡片长什么样、端口画成什么、线怎么走 |
| `workflow-grid-decorator` / `workflow-minimap-overlay` / `workflow-template-selector` | 仍是派生 + 调色板 | 它们是**实现**（`IWorkflowGridDecorator` 等），里面的平台机制用户不会想重写 |
| `workflow-tree-view` | 仍是派生 | **它是引擎不是视图**：绑定、视图池、手势、虚拟化、菜单；用户自己的树模板只设属性 + 两个工厂（详见 §2.1.2） |
| 标记四家（WPF / Avalonia / WinUI / MAUI）的全部角色 | 仍是派生 | 有标记语言时，「视图」本来就在用户手里（`x:Name` + `OnRender`），不需要多一层助手 |

助手的形状是同一套：**不是画**的那部分归它（绑定、改绑退订、几何、命中契约、事件），画归你；它另外给
「最短的一版」（`Paint(...)`）与可供你自绘的原料（`Curve` / `IconPath` / `Brush` / `PortLayout`）。事件名与
`IInputEvents` 逐字相同（`PointerEntered` / `PointerLeft` / `PointerPressed` / `PointerReleased`），
模型事件则与 §2.1 那组同名（`Moving` / `Moved` / …）。三种 view 的助手访问器一律叫 `Attachment`。

### 2.1.2 树是引擎，不是视图 —— 它留在基类，但**部件按对象交出去**

**2026-10-04 用户定**：树这一角色跟上面三个不同性质 —— 它是「绑一个 tree → 视图池把可见集逐个物化成 View」的那台
机器（Jalium `WorkflowTreeView.SetTree` → `ViewPool.SetItemsSource(this, tree.GetHelper().VisibleItems)`）；
用户在这条路上只提供三样：**每个 VM 用哪个 View 类**（工厂 / 选择器）、**容器怎么组装**、调色板。用户自己的树模板
因此天然很短（Jalium 32 行 / WinForms 52 行），**里面没有一行属于他的画**。

⇒ 别为了「七个角色形态齐一」把它也搬成助手：那不会把任何画法还给用户，只是把「摆一个控件」变成「造一个控件再挂助手」。

**但无标记语言的平台上，「按名字找控件」是一层假装的标记，要去掉。** XAML 里 `x:Name` 是标记语言自己的
手交出去方式，正当；纯代码平台没有这个理由 —— WinForms 的完整 demo 为此不得不把自己命名成 `nameof(WorkflowCanvas)`
再登记「我就是滚动容器、是画布、是网格装饰器」。所以：

- **WinForms**：`WorkflowSurfaceBehavior` 的部件从**名字字符串**改成**对象**（`SetScrollViewer/SetCanvas/SetGridDecorator/SetMinimapOverlay`）。
- **Jalium（2026-10-05 反转）**：它**改成**了按名字解析 —— 表面与节点视图各挂一组附着行为
  （`WorkflowSurfaceBehavior` / `WorkflowSlotLayoutBehavior`），部件靠 `x:Name` + `FindName` 交出，
  与 WPF 同形。所以上面那条「无标记平台没有名字作用域」对它**不再成立**；WinForms 仍是对象件。
  ⚠ 它同时带来两条 Jalium 特有的限制（实测）：`RelativeSource AncestorType` 只认**框架类型**，
  自定义 `clr-namespace` 类型不解析；**带前缀的附加属性路径**（`(behaviors:X.Y)`）在绑定路径里根本不解析。
- **边界**：WinForms 的**画布交不出来** —— `SurfaceCanvas` 就是池的宿主（它拿 `_owner.CreateNodeView/CreateLinkView`
  建视图、`RecordRole` 记角色、读 `SurfaceBackground` 画网格），交出画布等于交出引擎。

### 2.2 「每一个角色」是字面的

七项逐项适用：`workflow-grid-decorator` / `workflow-link-view` / `workflow-minimap-overlay` /
`workflow-node-view` / `workflow-slot-view` / `workflow-template-selector` / `workflow-tree-view`。

**不做「这个角色看起来本来就该用户写」的豁免。** 判据是「这一段是不是扩展点」，不是「这个角色感觉像不像用户的」。

> ⚠ **2026-10-05 更正**：这里原本拿 `grid-decorator` 当反例，说「用户该拿到派生 + 调色板，而不是写一个网格渲染器」。
> **实测的七家不是这样**：WPF / Avalonia / WinUI / MAUI / Razor **五家**把整个网格渲染器放在**模板**里（111–535 行），
> 只有 WinForms 与 Jalium 把它藏在适配器。用户 2026-10-05 定「WPF 怎么来，Jalium 就怎么来」，Jalium 随之改成第六家。
> ⇒ **那条反例作废**；下面这句抽象规则仍然成立，只是别再用 grid-decorator 举例。

### 2.3 验收（不是设计目标）

**模板产物的行数应当与有标记语言那几家同角色在同一量级。** 数倍于其余六家同角色的最大值 ⇒ 没做到。
这条只用来验收，**不要按行数去凑**：行数是结果，判据始终是 §2.1。

---

## 三、开一个角色的基类：步骤

1. **先读该平台 Trimmed demo 里那个同名的视图文件**（模板是它的镜像，见 [item-template-specifications.md](item-template-specifications.md) §一）——
   不要从模板文件开始读。
2. **把文件按 §2.1 分堆**：哪些是平台硬限制与机械装配，哪些是这个项目的调色板与画法。分不清就写下来问，不要猜。
3. **把机制那堆搬进适配器**成 `public`、非 `sealed` 的基类；策略留成 `protected virtual` 属性、`public` 可设属性或抽象工厂。
4. **注释分家**：公开成员英语 `///`，非公开与函数体内简体中文 `//`（[code-comment-specifications.md](code-comment-specifications.md)）。
   搬移是「按本文回归那一行」的时机 —— 模板里的注释是 §五 那套（只讲扩展点），进了包就变 §一/§二。
5. **模板与 Trimmed demo 同时改成派生**（同一笔；只改一边等于没改）。
6. **跑校验脚本**（见 §四）。

**worked example**：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/` 下五个基类
（`WorkflowTreeView` / `WorkflowNodeView` / `WorkflowSlotView` / `WorkflowMinimapOverlay`，加连线的 `WorkflowLinkAttachment`），
对应模板 1095→52、790→404、379→22、346→21、325→22 行。

**要一起想的**：一个角色的基类若要引用**另一个角色的产物**，那个依赖必须一起进包，否则基类做不了自己的活。
⚠ 这条原先举的例子（Jalium 的 `WorkflowPortGeometry` / `WorkflowPortLayout`）**已于 2026-10-05 作废** ——
那两个类型随 Jalium 转标记驱动一起删了（端口位置改由标记里声明的槽控件实测写回 `slot.Anchor`）。
现在这条的实例是 **`WorkflowLinkBounds`**：连线视图的自盒化是 Jalium 渲染器的硬限制（按 `RenderSize` 盒裁剪，
画到盒外静默丢弃），它属于适配器、不属于模板 —— 那是**实测走不通**才留的差异，不是偏好。

---

## 四、连带义务

- **模板与 Trimmed demo 的镜像关系照旧**：模板改成派生之后，demo 同形。
- `template.json` 的 `primaryOutputs` / `symbols` / `dotnetcli.host.json`：颜色 token 一般不用动（仍在模板里，只是从
  `ParseColor` 的参数变成属性的赋值）。**改名或新增文件才要动 `primaryOutputs`。**
- **文本没有构建产物**（模板项目 `EnableDefaultCompileItems=false`），所以「生成 → 一起编译 → 与镜像比对」必须脚本化。
  **七家统一走** `Src/Verification/verify-workflow-item-templates-all.ps1`（`-Platform <平台名>` 选一家、
  `-Strict` 严格模式；不传 `-Platform` 就是七家一起）。两个旧的平台专用脚本现在是它的**薄转发**，老调用照常可用。
  它还会**硬断言生成文件里不残留任何 `replaces` 占位符** —— Jalium 那个 `TemplateLinkColor` 缺陷就是这条抓到的
  （旧的 Jalium 脚本会把 `ColorConverter.ConvertFromString("…")` 归一化掉，正好抹掉要查的东西）。
- 模块记忆（`memory/modules/Templates/adapters/<平台>.md` 与 `memory/modules/VeloxDev.<平台>/`）要跟着改：
  角色表里的「形状」一列与扩展点清单都会变。

---

## 五、现状：与第二节不一致的地方（2026-10-03）

**这一节是现状，不是规范。** 要动相关代码时照第二节改。

| 平台 | 有基类 | 还缺 |
|---|---|---|
| **WinForms** | 四项有基类（grid-decorator / minimap-overlay / template-selector / tree-view）；**link / node / slot 三项改为附加助手**（2026-10-04，见 §2.1.1） | —— |
| **Jalium** | **本规范不再适用于它**（2026-10-05 移出 §一）：它改成标记驱动，与 WPF / Avalonia / WinUI / MAUI 同一形态 | —— |

**WinForms 的七个基类**（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`）与它们把模板压到的行数：
`WorkflowTreeView` 52、`WorkflowNodeView` 404、`WorkflowSlotView` 22、`WorkflowGridDecorator` 41、
`WorkflowMinimapOverlay` 22、`WorkflowTemplateSelector` 19；连线那一角色换成 `WorkflowLinkAttachment`
（助手，2026-10-04）。
另有三个共用件：`WorkflowSurfaceColors`（颜色解析）、`WorkflowSurfaceGraphics`（圆角矩形）、
`WorkflowSurfaceGrid`（网格线判定与刻度标签格式化，此前在包内有**两份**逐字相同的私有副本）。
校验：`Src/Verification/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict` 全绿。

**Jalium 的七个角色**（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`，2026-10-05 转标记驱动后重写）：
`WorkflowSurfaceBehavior`（宿主表面的 8 个附着属性）、`WorkflowSlotLayoutBehavior`（`slot.Anchor` 的唯一写回点）、
`WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowEvents` / `WorkflowLinkBounds`
（四个与 WPF 同名同形的行为）、`WorkflowGridDecorator`（**已是控件**，`Grid, IWorkflowGridDecorator`，
16 个 DP）、`WorkflowMinimapOverlay`（14 个 DP）、`ViewPool` / `ViewManager`（用 Jalium 自己的
`DataTemplateSelector` + `DataTemplate.LoadContent()` + 三级回退）。**没有** `WorkflowTreeView`、
三个 `*Attachment`、`WorkflowPortGeometry`、`WorkflowPortLayout`、`IWorkflowTemplateSelector` —— 那一整套已删。
四个模板条目是 `TemplateClass.jalxaml` + `.jalxaml.cs`（node / slot / link / tree），三个仍是 `.cs`。
校验：`Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict` 全绿
（generated 7/7、built True、match 9 / expected-diff 2 / drift 0）。

**六个角色是「基类 + 派生」，连线那一角色是「附加助手」**（2026-10-04，见 §2.1.1）。 曾经不是：`slot-view` 的产物一度是一份「端口在哪」的静态几何，
**端口图形由卡片自己画成圆点**；现在 `WorkflowSlotView` 是一个真正的控件，卡片按 `WorkflowPortLayout` 托管
一个实例在每个端口位置上，模板的 `slot-view` 条目是它的子类。剩下的 `WorkflowPortLayout` 是**设计值**
（尺寸与端口位置），不是「因为造不出控件而留下的替代品」。

**连线交互那层归属已决**（此前是这一项最大的纠结点，见 [item-template-specifications.md](item-template-specifications.md) §五最后一条）：
Jalium 的连线命中/拖拽/虚拟预览**进了包**（`WorkflowTreeView` 的手势与 `WorkflowLinkAttachment` 的拖拽预览跳过），
模板与 Trimmed demo 因此不必自绘那层 —— 交互在**适配器基类**里，生成的模板拿到的仍是「被动视觉 + 可覆写画法」。

**外观那一半也归用户**（2026-10-04）：这两家的 `WorkflowLinkAttachment` **只画静息线、且只在用户调 `Paint` 时才画**；
悬停光、焦点环、角标一律由用户的视图自己在 `OnPaint` / `OnRender` 里画，事件从助手上订
（`PointerEntered` / `PointerLeft` / `PointerPressed` / `PointerReleased`，名字与 `IInputEvents` 一致）。
