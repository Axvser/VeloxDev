# 功能落层：Core / 适配器 / item template / demo

> 用户口述、agent 落笔（2026-10-04）。约束**一个连线/视图相关的功能该落在哪一层**。
> **触发**：判断一段连线或视图的功能该落在 Core / 适配器 / item template / demo 哪一层之前；给连线做**外观**（高亮、选中、主题、流光）之前。
> 判据全在 §一，不需要读代码。

---

## 一、四层各是什么

**用户的判断标准是一句话：交互效果（悬停高亮、选中、Delete 删线、右键菜单条目）只存在于 Demo，作为「如何基于事件订阅实现定制」的示例；只有直接触及 WorkflowSystem 后端命令 / 数据 / 逻辑的部分（以及平台机制），才在适配器提供实现。**

> **Core 自己不做任何动作**（2026-10-04 定）：输入路由只持有「指针停在哪」这一件状态，`KeyDown` 不再自带删除、指针事件不再自带高亮。库给的是**事件与时机**，效果写在你自己的视图/宿主里 —— 与 `InfoOverlay` 那种 demo 的地位一致。

| 层 | 收什么 | 举例 |
|---|---|---|
| **Core**（`Src/Core/VeloxDev.Core/`） | **平台无关的机制**：状态、算法、输入面 | 连线命中算法（对着已发布的曲线判距）、**标准输入的输入路由** `WorkflowInput.For(tree)`（`Route` + 目标冒泡）、`IInputEvents`（组件上的指针/键盘事件）、`WorkflowEventHandle` |
| **适配器**（`Src/Adapters/VeloxDev.*/`） | **平台机制**，以及**直接触及后端命令 / 数据 / 逻辑**者 | 指针与按键翻译后转发进输入路由、键盘焦点路由（Delete 键靠它才能到达）、WinForms 的窗口区域雕刻、几何与每帧记账 |
| **item template**（`Src/Templates/*/working/content/`） | **被动视觉**：把模型画出来，不含任何交互外观 | 画线、`PublishCurve(curve, this)`、NaN 就绪门、调色板与线宽 |
| **demo**（`Examples/Workflow/…`） | **外观与策略**：交互效果在这里演示怎么写 | 悬停/选中高亮、流光、菜单条目、`InfoOverlay` 那类 HUD |

**一句话记法**：Core 决定「发生了什么」，适配器做「平台才做得到的事」，模板画「静止的样子」，demo 演「好看的反馈」。

### 1.1 外观怎么在 Demo 里做

订 Core 的事件，自己画 —— 这是本规范要求演示的写法：

```csharp
// demo only：每条线只在轮到自己时重画，没有广播
// 订这条线自己的 Helper 就够了 —— 路由会告诉它指针何时来、何时走
((IInputEvents)link.GetHelper()).Input.PointerEntered += (_, _) => _lit = true;
((IInputEvents)link.GetHelper()).Input.PointerExited += (_, _) => _lit = false;
```

- 事件在**被命中的那个组件**上先发，再沿祖先链冒泡（`link → tree` / `slot → node → tree` / `node → tree` / 空白画布 → `tree`）；`e.Target` 是适配器判出的命中者，`e.Source` 是发布曲线时登记的「画它的那个控件」，`e.Position.Layer` 是来源视图的图层。
- 指针换目标时路由**先给留下那个发 `Exited`、给新那个发 `Entered`**，所以逐组件订阅不必自己比 target。
- 用户订阅**天然在框架默认动作之前**；想要「这一次不要框架那一手」置 `PreventDefault`（Delete 删线就是它），想要「到此为止、祖先收不到」置 `StopPropagation`（`GUI/Events/WorkflowEventHandle.cs`）。
- 池化视图必须在换绑 / 卸载时退订 —— 事件活到树那一层，视图活得比它短。

### 1.2 「后端逻辑留在适配器」不等于「外观也留在适配器」

适配器可以**拥有画面**（MAUI 的 `WorkflowLinkOverlay` 一个人画完所有线），但**默认不许画出交互反馈**：
它只提供**默认不画的覆写钩子**（`protected virtual` 空实现，或一个由宿主赋值的输入属性）。宿主给了值才有效果，没给就是没有 ——
「开箱就有高亮」不是本规范的目标。

### 1.3 Core 的事件机制不因外观下沉而缩水

外观下沉到 Demo **不改变** Core 的事件面：两相（Preview/Outcome）、共用句柄、`PreventDefault` / `StopPropagation`、
以及「平台无关地配置冒泡行为」都在 Core，七家写法一致。下沉的只是**画什么颜色**。

### 1.4 七家的定制入口只有一套形状

「用户越过适配层去定制」这件事，**七家必须是同一套入口** —— 否则每加一个平台就是又一份要学的 API。

| 要定制 | 入口（七家同一套） |
|---|---|
| 一次指针/键盘动作（悬停、按下、松开、滚轮、按键） | **订阅**：`((IInputEvents)vm.GetHelper()).Input.<事件> += …`。视图自己吃指针的平台（Razor / MAUI overlay / 逐线视图）与表面转发指针的平台（WPF / WinUI / Avalonia / WinForms / Jalium）都是这一句 |
| 这一笔的效果怎么画 | **画在你自己的视图里**（连线 / 节点卡 / 端口都一样）：WPF / WinUI / Avalonia / Razor / **Jalium**（2026-10-05 起走标记）是你本来就拥有的那个视图类（`OnRender` / 标记）；WinForms 也是你的控件，调 `WorkflowLinkAttachment` / `WorkflowNodeAttachment` / `WorkflowSlotAttachment` 的 `Attach(this)` 挂上其余机制后自己画。**MAUI 的连线是唯一的例外**（见下） |
| 这一次要不要走框架那一手 | args 上的 `Handle.PreventDefault`；要不要继续往上冒 = `StopPropagation` |
| 整块表面级的策略 | `WorkflowInput.For(tree)` 的开关与只读口 |
| 声明式的东西（右键菜单条目、模板选择器） | 标记平台：附着属性 / 组件参数；无标记的 **WinForms**：基类的 `protected virtual` 钩子 |

**MAUI 也不能例外**（2026-10-05 用户定，走的是「宿主的层自己画」这条路）：那家**一个 overlay 画完所有线、没有每线视图**（每线一个 `GraphicsView` 撞 Win2D 纹理上限，Trimmed demo 实测过），所以「在我的那一笔视图里画」没有对应物 —— 但**效果仍是宿主的**：宿主在连线层之上**再叠一层自己的视图**，订 `IInputEvents`，再沿 `ILinkHitTestable.Curve` 画。所以 Core 把**已发布的曲线**开成只读的公开事实（`Curve`）—— 那本来就是命中判定用的那一条，藏着只会逼每个宿主再推一遍几何。

⇒ **七家现在是同一句话**：库给事件与几何（`Curve`），效果画在你自己的视图里。

**适配器基类给无标记语言那一家（WinForms）的可重写钩子，命名规则与模型事件那组同一条**：`On` + 事件名，参数就是那次事件的 args
（`OnPointerEntered(PointerEnteredEventArgs)`、`OnMoving(NodeMoveEventArgs)` …）。

⚠ **钩子名不许断言效果**：写 `OnPointerEntered`，不写 `OnPaintHighlight` —— 用户订的是「指针进来了」，
不是「我要做高亮」；基类对「这条线为什么该长得不一样」没有意见，它只提供时机与画布。

---

## 二、边界上的常见误判

| 看着像 | 其实归 | 为什么 |
|---|---|---|
| 「高亮是视觉，那它该在适配器」 | **demo** | 适配器只在它**必须**拥有画面时画（如 MAUI 的单 overlay），且默认不画反馈 |
| 「命中判定是交互，那它该在适配器」 | **Core** | 命中是算法，对着视图发布的曲线判距，七家一份 |
| 「发布曲线是命中逻辑，那它该进适配器」 | **item template** | 只有视图知道自己画了什么形状；曲线由视图发布，`this` 一并交出去（既是命中载体，也是事件的 `sender`） |
| 「右键菜单是外观，那它该进 demo」 | **适配器接线 + 模板声明条目** | 菜单条目是用户要改的内容（在 `workflow-tree-view` 的资源里声明）；订阅、定位、弹出、开合上报一行都不许留在模板 |
| 「Delete 是交互，那它该进 demo」 | **适配器（焦点）+ Core（裁决）** | 键盘事件沿焦点冒泡，适配器必须能把焦点收到连线视图上；删哪一条由 Core 侧的共享命中判定（`LinkHitTestEx`）给出 |

---

## 三、与其它规范的关系

- 模板与 Trimmed demo 的镜像关系见 [item-template-specifications.md](item-template-specifications.md) §一：
  **外观下沉到 demo 意味着这两份本来就该不一样** —— 镜像校验脚本对这类条目按「预期差异」放行（同 `tree` 条目的 HUD）。
- 无标记语言的一家（**WinForms**）的基类归属见 [adapter-base-class-specifications.md](adapter-base-class-specifications.md)
  —— Jalium 已于 2026-10-05 移出那份名单，它与标记四家同形：
  本规范只改**外观**这一层，§2.1 那条「平台机制进基类」的划分不动。
- 注释照 [code-comment-specifications.md](code-comment-specifications.md)：模板里的注释只讲扩展点（§五）。

---

## 四、这一层的现行归属（来回翻过，别再翻回来）

| 事 | 归谁 |
|---|---|
| 命中、删除 | **库能力**（Core 侧） |
| 高亮等外观 | **demo** —— 订那条线自己的 `IInputEvents` 指针事件自己画。Core 里没有 `ILinkHighlight`、也没有 `AutoHighlight` |
| 连线的指针/键盘输入 | 一套**标准输入**（抄 Avalonia 的指针/键盘 API；位置转 `Anchor` 并带上来源视图的图层、按 target 冒泡、句柄管「走不走框架那一手 / 还传不传」）。**不要按组件定制事件** |
| 右键菜单 | **适配器**从自己的 `PointerPressed(Right, link)` 里弹，`LinkRemoved` 收尾 |
| 画布滚动（滚轮） | **适配器执行、宿主决定去向**（2026-10-11 用户定）：平台滚动容器**一次都不许滚画布**，适配器整笔拦下、路由，没人接管就执行默认竖滚；订阅方置 `PreventDefault` 后经 `WorkflowInput.Scroller`（`IWorkflowSurfaceScroller`）决定往哪滚。**平台原生的 Shift+滚轮横滚被有意取代**，别当回归去「修」 |
| 平台接线方式 | 标记六家（WPF/Avalonia/WinUI/MAUI/Razor/Jalium，Jalium 与 WPF 逐行同形）；**只剩 WinForms 没有标记语言**，走 `Attach(this)` 那套基类钩子。`slot.Anchor` 由标记里声明的槽控件**实测**写回，不是模型几何算出来的 |

⇒ 这一层来回翻过不止一次。**再要动它，先问用户**，不要按「上一次是怎么做的」推断。
