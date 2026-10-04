# 无标记语言平台的适配器基类

> 用户口述、agent 落笔。约束**没有标记语言的那几家平台的适配器**与它们的 item template。
> **触发**：给一个无标记语言的平台新增或修改适配器 / 它的 item template 之前；判断「这段代码该进适配器包还是留在模板里」之前；给这类平台新增一个模板角色之前。
> 判据「这家算不算无标记语言」在 §一，不需要读代码。

---

## 一、适用范围：无标记语言的那两家

**判据：这一家的 item template 产物里没有标记文件 —— 产物是纯 `.cs`。**

- **不适用**于有标记语言的平台：WPF / Avalonia / WinUI / MAUI（XAML），Razor / Blazor（`.razor`）。
  它们的模板把布局与装配交给框架，本来就只有几十行。Razor 的行数比 XAML 那几家高，但那是组件模型而不是「没有标记」，**仍不适用**。
- **适用**于纯代码的两家：**WinForms、Jalium**。

⇒ 新增一家平台时，先答「它有没有标记语言」，再决定这条规则跟不跟。

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

### 2.1.1 例外：`workflow-link-view` 给的是「附加」，不是基类

**2026-10-04 用户定**：连线的组件视图是**完全用户定制**的，所以**整个视图的构建不该被藏进基类**。这一角色的产物
应该像一个**视图**（用户写 `OnPaint` / `OnRender`），适配器给的是一个**可以附到控件上的助手**，在用户构建视图时
顺带把其余东西挂上去：

| | 写法 |
|---|---|
| 其余六个角色 | `public class TemplateClass : <适配器的基类>` —— 派生 + 设调色板 |
| **`workflow-link-view`** | `public sealed class TemplateClass : Control`（或 `FrameworkElement`），构造里一行 `WorkflowLinkAttachment.Attach(this)`，自己在 `OnPaint` / `OnRender` 里画 |

助手负责**不是画**的那部分：端点订阅（含池化换绑）、窗口区域雕刻 / 自适应盒子、几何、命中契约的发布、这条线的指针事件
（`PointerEntered` / `PointerLeft` / `PointerPressed` / `PointerReleased`，事件名与 `IWorkflowInputEvents` 一致）。
`Curve` 把四个控制点交出来，`Paint(…)` 提供「最短的一版静息线」供调用或忽略。

**为什么这条能例外而 `grid-decorator` 不能**：判据始终是 §2.1 的「这段代码是不是用户该改的扩展点」。
网格那些平台机制（世界坐标换算、刻度排版、每帧重绘）**用户不会想重写**；而连线视图的**画法本身就是用户的**，
把它藏进基类的唯一效果是用户每次想改一笔都要先读一遍基类。

### 2.2 「每一个角色」是字面的

七项逐项适用：`workflow-grid-decorator` / `workflow-link-view` / `workflow-minimap-overlay` /
`workflow-node-view` / `workflow-slot-view` / `workflow-template-selector` / `workflow-tree-view`。

**不做「这个角色看起来本来就该用户写」的豁免。** 判据是「这一段是不是扩展点」，不是「这个角色感觉像不像用户的」——
反例：`grid-decorator` 在七家都是「你自己的网格」，看着像纯用户代码，但它照样含平台机制（网格线的世界坐标换算、
标尺刻度与标签的排版、每帧重绘），用户该拿到的是「派生 + 调色板」而不是「写一个网格渲染器」。

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

**要一起想的**：一个角色的基类若要引用**另一个角色的产物**（Jalium 做这一项时正是这种情况：树基类要端口几何，
而端口几何起初在模板的 `SlotView` 静态类里），那个依赖必须一起进包，否则基类做不了自己的活 —— Jalium 的解法是把
端口枚举/定位搬成包内的 `WorkflowPortGeometry`，模板只留一个 `WorkflowPortLayout` 值。这类跨角色的牵扯是这一步最容易漏的。

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
| **WinForms** | 六项有基类；**`workflow-link-view` 改为 `WorkflowLinkAttachment`**（2026-10-04，见 §2.1.1） | —— |
| **Jalium** | 六项有基类；**`workflow-link-view` 改为 `WorkflowLinkAttachment`**（2026-10-04，见 §2.1.1） | ——（`slot-view` 在这家没有「视图」可派生，见下） |

**WinForms 的七个基类**（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/`）与它们把模板压到的行数：
`WorkflowTreeView` 52、`WorkflowNodeView` 404、`WorkflowSlotView` 22、`WorkflowGridDecorator` 41、
`WorkflowMinimapOverlay` 22、`WorkflowTemplateSelector` 19；连线那一角色换成 `WorkflowLinkAttachment`
（助手，2026-10-04）。
另有三个共用件：`WorkflowSurfaceColors`（颜色解析）、`WorkflowSurfaceGraphics`（圆角矩形）、
`WorkflowSurfaceGrid`（网格线判定与刻度标签格式化，此前在包内有**两份**逐字相同的私有副本）。
校验：`Src/Verification/verify-workflow-item-templates-all.ps1 -Platform WinForms -Strict` 全绿。

**Jalium 的七个角色**（`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/`）：`WorkflowTreeView` 819、
`WorkflowNodeView` 309、`WorkflowSlotView` 167（端口图形）、`WorkflowGridDecorator` 234、
`WorkflowTemplateSelector` 51、`WorkflowMinimapOverlay` 311，加分层的两个共用件 `WorkflowPortLayout`（设计值，41）
与 `WorkflowPortGeometry`（端口枚举与定位，反射，125）。
七个模板条目现压到 32 / 59 / 35 / 20 / 25 / 25 / 14 行，合计 **210**（原 1262）。
校验：`Src/Verification/verify-workflow-item-templates-all.ps1 -Platform Jalium -Strict` 全绿。

**六个角色是「基类 + 派生」，连线那一角色是「附加助手」**（2026-10-04，见 §2.1.1）。 曾经不是：`slot-view` 的产物一度是一份「端口在哪」的静态几何，
**端口图形由卡片自己画成圆点**；现在 `WorkflowSlotView` 是一个真正的控件，卡片按 `WorkflowPortLayout` 托管
一个实例在每个端口位置上，模板的 `slot-view` 条目是它的子类。剩下的 `WorkflowPortLayout` 是**设计值**
（尺寸与端口位置），不是「因为造不出控件而留下的替代品」。

**连线交互那层归属已决**（此前是这一项最大的纠结点，见 [item-template-specifications.md](item-template-specifications.md) §五最后一条）：
Jalium 的连线命中/拖拽/虚拟预览**进了包**（`WorkflowTreeView` 的手势与 `WorkflowLinkAttachment` 的拖拽预览跳过），
模板与 Trimmed demo 因此不必自绘那层 —— 交互在**适配器基类**里，生成的模板拿到的仍是「被动视觉 + 可覆写画法」。

**外观那一半也归用户**（2026-10-04）：这两家的 `WorkflowLinkAttachment` **只画静息线、且只在用户调 `Paint` 时才画**；
悬停光、焦点环、角标一律由用户的视图自己在 `OnPaint` / `OnRender` 里画，事件从助手上订
（`PointerEntered` / `PointerLeft` / `PointerPressed` / `PointerReleased`，名字与 `IWorkflowInputEvents` 一致）。
