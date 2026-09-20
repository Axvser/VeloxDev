# VeloxDev.Razor — 架构

> 代码：`Src/Adapters/VeloxDev.Razor/`。**21 个 .cs、2511 行**（`Attached/Workflow/` 10 个 1880 行，最大三个是
> `WorkflowSurfaceBehavior.razor.cs` 533、`WorkflowMinimapOverlay.razor.cs` 453、`WorkflowNodeDragBehavior.razor.cs` 215；
> `PlatformAdapters/` 10 个 628 行（9 个直接子文件 + `Samplers/StringSampler.cs` 287）；顶层 `GlobalUsings.cs` 3 行），
> 另有 **7 个 `.razor`**（全在 `Attached/Workflow/`）、`wwwroot/veloxdev.workflow.js` **1366 行**、
> `wwwroot/veloxdev.workflow.css` **235 行**、`README.md` 202 行。
> **计数写法**：`git ls-files Src/Adapters/VeloxDev.Razor | grep -c '\.cs$'`。别写 `'…/**/*.cs'` —— 这条 pathspec **不匹配目录本级**的
> 文件，会漏掉 `GlobalUsings.cs`（七家适配器全都正好漏这 1 个）。
>
> 本文只写「读完这 21 个 .cs 加那 1601 行 wwwroot 才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/razor.md`、`memory/modules/TransitionSystem/adapters/razor.md`、
> `memory/modules/Templates/adapters/razor.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴，外加别家都没有的第四样东西

这个项目叫「Razor 适配器」，但它和 WPF/WinForms 那几家有一条结构性的不同：**它同时是一个 Razor 类库（`Microsoft.NET.Sdk.Razor`），
带一个 `wwwroot/`**。三条轴的平台契约照旧在 C# 里，但很大一部分**平台实现**跑在浏览器里 —— 1366 行 JS 与 235 行 CSS
（`VeloxDev.Razor.csproj:1` 的 SDK；`wwwroot/` 两个文件）。

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（10 个 `.cs` + 7 个 `.razor`）+ **`wwwroot/`** | `memory/modules/WorkflowSystem/adapters/razor.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 采样器 | `PlatformAdapters/`（9 个类型 + `Samplers/` 1 个） | `memory/modules/TransitionSystem/adapters/razor.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs`（4 个） | 条数清单在 `memory/modules/DynamicTheme/architecture.md` §六 |

**三条轴在程序集内互不引用**（与别家同形）：`Attached/` 下零 `Transition<T>` / `Interpolator` / `ThemeManager` 符号，
`PlatformAdapters/` 下零 `Workflow` 符号。跨轴只能在宿主侧接线。

**不解决什么（常被误以为在这里）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 七个角色的职责、契约成员、注册位置 | `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 |
| 缩放/平移/虚拟化/槽位锚点的**数学** | Core `WorkflowSurfaceMath`、`WorkflowSlotUpdateGate` 等 |
| 生成器 / 分析器 | **本项目不引用分析器包** —— `VeloxDev.Razor.csproj` 的引用项只有 `VeloxDev.Core` 一条（`:26-27`） |
| `dotnet new` 模板包 | `Src/Templates/VeloxDev.Razor.Templates/`（模板侧的形状见 `memory/modules/Templates/adapters/razor.md`） |
| 「怎么用这套模板搭一个 Blazor 工作流视图」（人面向） | `skills/veloxdev-create-workflow/references/gui/razor.md` |

---

## 二、`wwwroot/`：唯一一家把平台实现放进浏览器的适配器

### 2.1 两个文件、两类角色

| 文件 | 角色 |
|---|---|
| `veloxdev.workflow.js` | **几何与手势的权威**：画布宿主尺寸、内容/网格/坐标轴平移、滚动上报、边缘扩张、节点拖拽、插槽连接手势、槽位实时测量、Ctrl+滚轮缩放、小地图视口块。全部包在 `window.veloxdevWorkflow` 这个 IIFE 里（`:37`），底部再给一份 ES 模块 `export const`（`:1349-1366`） |
| `veloxdev.workflow.css` | **不只是外观，还是测量契约的一部分**（见 §2.5） |

JS 里另有两个 `window` 级工具函数 `downloadFile` / `openFileDialog`（`:6`、`:21`），**适配器自己不用**，
只有 demo 调（`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:198,205`）。

### 2.2 谁调用谁：三个组件各 `import` 一次模块，`init*` 返回句柄

适配器不注入 CSS（宿主自己在 `App.razor:9` 写 `<link href="_content/VeloxDev.Razor/veloxdev.workflow.css">`），
JS 则由组件在 `OnAfterRenderAsync` 里按**模块路径**导入：

```
import "./_content/VeloxDev.Razor/veloxdev.workflow.js"
```

三处（`WorkflowSurfaceBehavior.razor.cs:160`、`WorkflowMinimapOverlay.razor.cs:195`、`WorkflowSlotLayoutBehavior.razor.cs:47`），
各自拿一个 `IJSObjectReference` 句柄存起来，`DisposeAsync` 时释放。⇒ **路径是硬编码的 `_content/` 约定**，
它由包 id 与 `wwwroot/` 的相对位置决定（`VeloxDev.Razor.csproj` 的包 id = `VeloxDev.Razor`）。

**模块内部是「按 scroller id 键控的 10 个模块级字典」**（这是这家独有的结构，也是它最容易踩的坑，见 §六·2）：
`surfaceRegistry:66`、`minimapMappings:71`、`minimapRects:76`、`minimapLastWorld:80`、`surfaceLayouts:86`、`surfaceSizers:92`、
`surfaceNodeWrappers:423`、`surfaceZoomState:433`、`surfaceSettleRunning:436`、`surfaceReporters:538`。
键一律是**滚动器的 DOM id**（即 `WorkflowSurfaceBehavior.ScrollViewerId`，默认 `"veloxdev-wf-scroll"`，`:35`）。

### 2.3 C# ↔ JS 的数据形状：哪一侧定义，哪一侧消费

**（a）JS 用 `document.getElementById(scrollerId)` 找滚动器，其余一切靠 class 找**（`getElementById` 的全部 11 处命中
`:144/159/188/344/444/492/824/833/1220/1251/1302` 拿的都是**滚动器**；`.veloxdev-wf-canvas-host` / `-canvas-content` /
`-grid` / `-axis-x` / `-axis-y` / `-canvas` 一律 `querySelector`）。⇒ **`CanvasId`（默认 `"veloxdev-wf-canvas"`）
被渲染成元素 id（`WorkflowSurfaceBehavior.razor:10`）却再也没有人读它** —— `initSurface` 的 9 个形参里没有它
（`veloxdev.workflow.js:540` 与调用点 `WorkflowSurfaceBehavior.razor.cs:165-166`
都只有 `_scroller, _canvasHost, _dotNetRef, _canvasW, _canvasH, contentX, contentY, _offsetX, _offsetY`）。
它是给 XAML 那几家 `CanvasName` 对齐用的**空转参数**：改它没有任何后果。

**（b）适配器写、JS 读的 DOM 钩子（只有两个）：**

| 钩子 | 写在哪 | JS 拿它做什么 |
|---|---|---|
| `data-veloxdev-node-id` | `WorkflowNodeDragBehavior.razor:5` | 拖拽起手、`setNodePosition`、缩放的节点几何（扫 `.veloxdev-wf-node-drag` 建 `{id: wrapper}` 映射，`:381-387`） |
| `data-veloxdev-slot-id` | `WorkflowSlotConnectionBehavior.razor:3` | 槽位实时测量（`initSlotLayout` 的 `measure()`，`:997` 起扫 `[data-veloxdev-slot-id]`）与连接手势的命中判定 |

**（c）消费方（模板 / demo）写、JS 读的钩子 —— 适配器保证不了它们：**

| 钩子 | 谁写 | 漏了会怎样 |
|---|---|---|
| `.veloxdev-wf-node-card` | 模板/demo 的 node-view | 深缩放时卡片不跟着 wrapper 缩放（`applyNodeGeometry`：`:205-240`）与 settle 重断言（`:473-479`）；**不报错** |
| `.veloxdev-wf-slot-svg` | 模板/demo 的 slot-view | `glyphRect` 退化成量整个插槽盒（`:247-250`），锚点会因内联 SVG 的基线间隙漂 |
| `data-veloxdev-link-id` / `-sender-slot` / `-receiver-slot` | 模板/demo 的 link-view | 深缩放时 polyline 端点折不动（`resolveLinkPolyline:256`、`applyZoomLinkPoints:305`）；**不报错** |

**（d）两个方向的载荷形状：**

| 方向 | 名字 | 形状 |
|---|---|---|
| JS → .NET | `OnSurfaceScroll`（C# `:326`） | 8 个 `double`：`scrollLeft, scrollTop, viewportW, viewportH, hostW, hostH, offsetX, offsetY`（JS `:628-636`） |
| JS → .NET | `OnWheelZoom`（C# `:184`） | 7 个：`wheelDelta, scrollX, scrollY, viewportW, viewportH, reachW, reachH`。**`scrollX/Y` 与 `reachW/H` 是「有效长度」= DOM 值 − JS 的边缘预留**，由 JS 在突发开始时从 DOM 现读（`:1156-1162` + 注释 `:204-208`） |
| JS → .NET | `OnSlotLayoutBatch`（C# `:54`） | `string[][]`，每项 `[id, cx, cy]`，数字是 JS 的 `toFixed(2)` 字符串（`:1009`、`:1023-1024`、`:1032`） |
| JS → .NET | `OnNodeDrag(dx,dy)` / `OnNodeDragEnd` / `OnSlotConnectionStart/Move/End` | 见 `WorkflowNodeDragBehavior.razor.cs:153,169`、`WorkflowSlotConnectionBehavior.razor.cs:59,77,92` |
| .NET → JS | `applyZoomSurface` 的 `nodeGeometry`（C# `:269-274`，JS `:343`） | `string[][]`，每项 `[nodeId, left, top, w, h]`，由 C# 用 `"0.###"` + `InvariantCulture` 写成（`:315-318`） |
| .NET → JS | `setMinimapMapping` / `setMinimapViewport` / `setSurfaceLayout` / `setNodePosition` / `refreshMinimapViewport` | 一维数值 + id，见 `WorkflowMinimapOverlay.razor.cs:394-400`、`WorkflowNodeDragBehavior.razor.cs:130-137` |

**契约里最硬的一条写在 JS 的注释里**（`veloxdev.workflow.js:335-338`）：

> 除布局偏移外，所有长度都是**有效（画布/世界）长度** …… JS 拥有边缘预留，因此在这里加上；
> **.NET 绝不该传入一个用它自己（滞后的）边缘算出来的原始 host/scroll 值。**

⇒ 这是这家「C# 与服务端模型在一边、DOM 在另一边」的必然结果：**边缘扩张的量只有 JS 知道**，
.NET 那份 `_offsetX/_offsetY` 只是「不小于标尺厚度」的镜像（`WorkflowSurfaceBehavior.razor.cs:141-142`），
它会因为 JS 侧的自动扩张而落后。凡是新增跨端参数，先问一句「这个长度是哪一侧在权威地维护」。

### 2.4 必须同帧完成的事：`applyZoomSurface` 是唯一一个原子事务

一次 Ctrl+滚轮突发在服务端被折成**一次** `OnWheelZoom` 调用（JS 侧先把净 `deltaY` 加总再发，`:1181`），
C# 侧一个 `using var _zoomScope = WorkflowGeometryScope.Zoom()`（`:199`）罩住整段，然后：

1. 写出枢轴 → `Scale`（复合 `count` 次）→ `EnsureNegativeCover`（`:210-228`）；
2. 用 `reachW/H`（JS 现读）夹一次 `contentW/H`、算一次 `PivotCenterScroll` + `ClampScrollOffset`，
   **再用夹完之后的新 offset 重算一次 scroll**（`:240-249`，注释 `:246-248` 说明第一遍用的是夹取前的 offset）；
3. `await _module.InvokeVoidAsync("applyZoomSurface", …)`（`:269`）。

JS 那一边（`:343-418`）在**一个同步块**里依次写：内容/网格/坐标轴平移 → 宿主尺寸增长 → 每个节点 wrapper 与卡片的
折叠几何 → 用 `getBoundingClientRect` 现读槽位中心重写 polyline 端点 → `scrollLeft/Top` → 打戳 `surfaceZoomState` 并启动
settle 循环 → 回报 `surfaceReporters`。

**为什么必须同帧**（注释 `:253-266`、`:375-396`）：节点几何与连线端点在 .NET 侧要经过「JS 测量 → `OnSlotLayoutBatch` 回传 →
写 `slot.Anchor` → 组件重渲染」这一整圈异步 SignalR 往返；中间任何一帧用旧 translate / 旧 scale 画出来就是**闪烁或错位**。
所以这一家把「DOM 上的最终几何」在 JS 里就地写定，再由 .NET 的 `applyZoomSurface` **await 到底**，让下一轮突发读到的 DOM 是稳定态。

**settle 尾窗**是这台机器的另一半：`stampedAt` + `SETTLE_TAIL_MS = 250`（`:512`）。每个动画帧重断言被戳上的节点几何与连线端点
（`:440-505`），直到最后一次戳之后 250 ms 内没有新的戳 —— 用来盖住「上一轮突发的 .NET 渲染比下一轮到达得还晚」。
用户的真实 pointerdown 会**删掉这个戳**（`:747-750`），让守卫让位于手势。⇒ 别把 250 当调参旋钮：它是「一轮 SignalR 往返 +
渲染」的量级估计，改小会漏帧、改大会和用户平移打架。

### 2.5 CSS 是测量契约的一部分

三处非外观的 CSS 承担着几何语义，改它们会改行为：

| 声明 | 为什么承重 |
|---|---|
| `.veloxdev-wf-canvas-host { width:0; height:0 }`（`veloxdev.workflow.css:27`） | 宿主尺寸**只由 JS 写**；给 CSS 一个初值就会让 Blazor 重渲染把已扩张的尺寸缩回去 |
| `.veloxdev-wf-canvas-content { position:absolute; left:0; top:0 }`（`:85-90`） | 内容层靠 `left/top` 被 JS 平移到 `边缘 + 布局偏移`，世界原点才落在网格/坐标轴的交点 |
| `.veloxdev-wf-slot { display:inline-flex; align-items:center; justify-content:center; line-height:0 }`（`:223-230`） | 注释写明了目的：让插槽包装盒**紧贴字形**，`getBoundingClientRect` 的中心才落在图形上而不是被内联 `<svg>` 的基线间隙带偏。`line-height:0` 是这条契约的一部分，不是排版洁癖 |
| `.veloxdev-wf-slot-svg { display:block }`（`:233`） | 同上的另一半 |

---

## 三、`PlatformAdapters/`：注册与工厂

| 要注册的东西 | 注册点 | 什么时候真的发生 |
|---|---|---|
| 1 个采样器（`string`） | `Interpolator` 的**静态构造**（`PlatformAdapters/Interpolator.cs:7-10`） | 第一次构造 `Transition<T>` 时（Core 的字段初始化器 `new()`） |
| 宿主/解释器/优先级 | `TransitionScheduler` 的类型实参（`TransitionScheduler.cs`） | 编译期写死 |
| DynamicTheme 的调度器工厂 | `Interpolator.CreateScheduler`（`Interpolator.cs:12-15`） | 宿主必须显式调 `ThemeManager.SetPlatformInterpolator(new Interpolator())` |

**没有程序集级入口，也没有 `Initialize()`**；唯一的静态构造就是 `Interpolator.cs:7`。
工厂写 `FindOrCreate` 而不是 `new`（`Interpolator.cs:13`）—— 只有它会把调度器按 target 归档。

**`Transition<T>` 的 `Property` 是逐类型手写的重载表**（`PlatformAdapters/Transition.cs:31-102`，13 组；`:104-129` 的
`#if !NETSTANDARD2_0` 块因本家只有一个 TFM 而**恒为真**）。后果与缺口（`long` 声明不出来、`decimal` 声明得出但无采样器）
已经在 `memory/modules/TransitionSystem/adapters/razor.md` §二·3 写全，本文不重复 —— 只补一句：
**在这家，漏登记采样器的症状是「写不出来」，不是别家的「运行期静默」**，因为那张表没有泛型兜底。

---

## 四、`Attached/Workflow/`：没有附着属性系统，组件即行为

XAML 那几家靠 `DependencyProperty.RegisterAttached` 把行为挂到任意元素上。Blazor 没有这套东西，
所以这家的七个角色是**七个 Razor 组件**，靠 `[Parameter]` + `RenderFragment` 槽装配：

- 画布宿主 `WorkflowSurfaceBehavior`（`.razor` 32 行）用 `CascadingValue TValue="SurfaceViewportFeed"`（`:3`，
  类型来自 Core）把视口快照发给子树，用三个 `RenderFragment` 形参接收装饰器/小地图/内容
  （`WorkflowSurfaceBehavior.razor.cs:43,47,51`）；
- `@namespace VeloxDev.WorkflowSystem.AttachedBehaviors` 写在每个 `.razor` 的第一行（`WorkflowSurfaceBehavior.razor:1`）——
  **这是七家共用的同一份命名空间**（Core 里不存在它），宿主必须 `@using VeloxDev.WorkflowSystem.AttachedBehaviors`
  （`Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/TreeView.razor:3`）。换命名空间 = 宿主找不到组件、**且不报错**。

**每个组件都长成同一个形状，只有一处例外：**

```
OnAfterRenderAsync(firstRender) → if (firstRender && IsEnabled) { import 模块 → init* → 存句柄 }
```

三处（`WorkflowSurfaceBehavior.razor.cs:158`、`WorkflowSlotLayoutBehavior.razor.cs:45`、
`WorkflowMinimapOverlay.razor.cs:193`）。两个后果：

1. **`IsEnabled` / `ZoomEnabled` 只在第一次渲染读一次。** 之后改这两个参数**没有任何效果**，也不会重新初始化 ——
   与五家（WPF/Avalonia/WinUI/MAUI/Jalium）各自注册 `OnIsEnabledChanged` 的做法是**刻意背离**（WinForms 也不注册，
   所以这条不是这家独有的；差异清单见 `WorkflowSystem/adapters/razor.md`）。要「换开关」= 重新挂载组件。
2. **`firstRender` 是闸门，不是提示** —— 组件若在第一帧被条件渲染挡住，之后再出现也不会去 `import`。
   模板在 `@if (Tree is not null)` 里包住整个 surface（`TreeView.razor:6`），就是这个形状的实例。

`WorkflowMinimapOverlay` 多一道门槛：它的 `ScrollViewerId` **没有默认值**（`WorkflowMinimapOverlay.razor.cs:82` 是可空 string），
而 `initMinimap` 被 `!string.IsNullOrWhiteSpace(ScrollViewerId)` 挡住（`:193`）⇒ **不传 `ScrollViewerId` 的小地图只画不动**
（视口块也不会出现，因为 `x/y/width/height` 全由 JS 写）。宿主必须显式传（`TreeView.razor:27`）。

**谁掌握重渲染**：这家没有「模型改了就自动刷」的绑定。`WorkflowSurfaceBehavior` 用 `SurfaceViewportFeed`（Core 类型）
把视口快照按廉价通道广播给装饰器，避免拖着一个子树重渲染（`WorkflowSurfaceBehavior.razor.cs:109-111`）；
而**节点/连线的重渲染由模板负责**（`memory/modules/Templates/adapters/razor.md` §二·2）。

---

## 五、`GlobalUsings.cs`、`VeloxDev.Razor.csproj`、`README.md`

**`GlobalUsings.cs` 三行，七个适配器逐字相同**（`global using` `VeloxDev.TransitionSystem` / `…Abstractions` / `VeloxDev.Threading`）——
所以它不是这家的特征，别拿它当差异。注意 `VeloxDev.TransitionSystem.Abstractions` 是**在 `TransitionSystem/` 的文件里声明的命名空间**，
不存在 `Abstractions/` 目录。它不含 `VeloxDev.WorkflowSystem`，于是 `Attached/` 里的文件各自写 `using VeloxDev.WorkflowSystem;`。
`global using` 是编译期的，**不随包传给消费者**。

`README.md`（202 行）是给宿主看的 API 表面说明（每个组件的参数与默认值）。**参数默认值要以代码为准**：
它与 `WorkflowMinimapOverlay.ViewportFill` 的默认（`:66` 是 `rgba(255,255,255,0.15)`，而模板写死 `transparent`，
见 `Templates/adapters/razor.md` P4）这类地方容易各说各话。

`VeloxDev.Razor.csproj`（30 行）里影响代码的取值：

| 事实 | 行 | 后果 |
|---|---|---|
| `Microsoft.NET.Sdk.Razor` | `:1` | 唯一让 `.razor` 成为编译输入、`wwwroot/` 成为静态资源的 SDK |
| `<TargetFramework>net6.0</TargetFramework>`（**单一**） | `:4` | 七家里只有这家与 Jalium 是单 TFM；`Transition.cs:104` 的 `#if !NETSTANDARD2_0` 因此恒真 |
| `Nullable` / `ImplicitUsings` / `AddRazorSupportForMvc` / `LangVersion` | `:5-9` | — |
| `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | `:22` | 提供 `ComponentBase` / `IJSRuntime` / `DotNetObjectReference`；**不是** `PackageReference`，所以没有可升的包版本 |
| Core 双轨（Debug `ProjectReference` / 非 Debug `PackageReference` 9.0.0） | `:26-27` | 与生成器那套同形；改一条要同时看另一条 |
| `GeneratePackageOnBuild` | `:8` | 所以 `obj/` 下会堆各版本 `.nupkg`（6.0.0 ~ 9.0.0），**那是产物、不是配置** |

**`obj/` 里存在 `net8.0` / `net10.0` 两个 csproj 未声明的 TFM 目录**（Release 侧只有 `net6.0`）⇒ csproj 的单一 TFM
不是最终值，那两个目录只能来自树外的 `-p:TargetFramework=…` 覆盖（与 `VeloxDev.Core.Generator/architecture.md` §七
记的是同一类现象）。**别按 obj 目录推本项目的 TFM。**

---

## 六、陷阱（带依据）

1. **`refreshMinimapViewport` 的理由引用了一个不存在的成员。** `WorkflowMinimapOverlay.razor.cs:201-207` 每次渲染都
   调它，注释说「重渲染会用过期的 .NET 状态（`MappedViewport`）覆盖视口块」；而 **`MappedViewport` 在全仓只出现在两处注释里**
   （`.razor.cs:202`、`veloxdev.workflow.js:134`），没有任何声明 —— 因为视口块的 `x/y/width/height` **根本不在渲染树里**
   （`WorkflowMinimapOverlay.razor:21-23` 只有 `class` / `fill` / `stroke` / `stroke-width` / `rx`）。⇒ 这条调用**不是空转**：
   JS 侧 `refreshMinimapViewport`（`:135-139`）会重放 `minimapLastWorld`，幂等、代价是一次 interop；但**注释描述的机制不存在**。
   读它时以代码为准。

2. **默认 id 在同一页上必须唯一，否则两个画布会串。** §2.2 那 10 个字典都以滚动器 id 为键，而默认值是常量
   （`WorkflowSurfaceBehavior.razor.cs:35` 的 `"veloxdev-wf-scroll"`）。两个 surface 都不传 id 时，第二个 `initSurface`
   会覆盖第一个的注册项：`surfaceRegistry` 指向后者、`surfaceReporters` 只剩后者 ⇒ 前者**滚动不上报、边缘不扩张**，
   而两个小地图会一起指向后一个滚动器。**不报错。**

3. **小地图的 XML 与 JS 对导航方式的说法相反，以 JS 为准。** 类文档说「抓取块导航（与其他适配器一致）」
   （`WorkflowMinimapOverlay.razor.cs:23-24`），JS 在按下的第一行就写「点击点**始终**成为视口中心 —— 指示块上没有抓取锚点」
   （`veloxdev.workflow.js:1231-1233` 及其注释，并自称与 Jalium 一致）。改导航手感前先把这两句对齐。

4. **有 5 个 ES 导出在 C# 侧零调用点，其中一条的注释还是过期的。** 底部 `export`（`:1349-1366`）共 18 条，
   全仓按名字数调用点：`getCanvasTranslate` / `getViewportSize` / `scrollToRatio` / `scrollByDelta` / `ensureCanvasSize`
   各 **0** 次（余下 12 条各 1 次）。其中 `ensureCanvasSize` 的注释写着「工作区缩放路径（`ensureCanvasSize`）必须让 DOM 宿主长大」
   （`:88-91`），但缩放路径走的是 `applyZoomSurface`（C# 调的是它，`WorkflowSurfaceBehavior.razor.cs:269`）；
   `scrollByDelta` 只被 `initMinimap` 内部用；`scrollToPosition` 则是 **demo 通过 `window.veloxdevWorkflow` 全局调的**
   （`Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor.cs:224`），不走 export。
   ⇒ 删这些之前先确认没有宿主在用它们 —— 它们是 `window.veloxdevWorkflow` 的公开面，不是内部细节。

5. **`ScrollViewerId` / `CanvasId` 之外，宿主还要给 `Minimap` 传一次同样的 id。** surface 的 id 只管自己
   （`WorkflowSurfaceBehavior.razor:5`），小地图要**再传一次**（`TreeView.razor:27`）—— 两者是各自独立的参数，
   不是级联。忘了第二个的症状见 §四。

6. **`WorkflowGeometryScope` 是 `AsyncLocal`，不是线程静态。** `Attached/Workflow/WorkflowGeometryScope.cs` 用
   `AsyncLocal<int>` 计深度，`IsZooming` 期间各几何写手自己让开（`WorkflowNodeDragBehavior.razor.cs:98-101`、`:115-118`）。
   ⇒ 一次缩放手势里所有 **await 之后仍在同一 `ExecutionContext`** 的代码都能读到它；但**放到 `Task.Run` 或另一个
   circuit 的线程上就读不到**。新增「缩放中让开」的写手时，先确认自己在那条 `AsyncLocal` 链上。

7. **全模块没有一个 `_disposed` 标记 —— 但「没有标记」不等于「没有收尾」，两种既有收尾形状都有效。**
   其一是**退订**：`WorkflowGridDecorator` 订阅 `SurfaceViewportFeed.Changed`（`WorkflowGridDecorator.razor.cs:101`），
   在 `Dispose`（`:129-136`）里摘掉。其二是**取消令牌**：`WorkflowMinimapOverlay` 的 `MarkDirty`（`:293-310`）
   每次重建 `CancellationTokenSource`，`RebuildAfterThrottleAsync` 用 `await Task.Delay(16, ct)`（`:316`）当闸门，
   `DisposeAsync`（`:403-412`）先 `Cancel` 再 `ResubscribeTree(null)` 摘模型订阅 ⇒ 挂起的节流被 `OperationCanceledException`
   挡在 `return` 上（`:318-321`），不会对已释放组件 `StateHasChanged`。⇒ 新写「带延时 / 带订阅」的组件，
   照这两种形状之一收尾即可；**不要**引一个全模块都还没有的标记（会让读者以为别处也有）。

8. **「Blazor Trimmed」这个 demo 并没有被裁剪。** `Examples/Workflow/Blazor Trimmed/Demo/Demo.csproj` 里
   **没有 `PublishTrimmed`**（Avalonia Trimmed / Jalium Trimmed 有，WinUI Trimmed 显式关掉）。⇒ 别拿它当
   「Blazor 侧过了裁剪验证」的证据；本项目也从不产出裁剪元数据。

9. **`ctx export` 与 `window` 全局是两套面。** C# 用的是**模块导入**（§2.2），所以只有底部的 `export const` 与
   `init*` 返回值进得来；`window.downloadFile` / `openFileDialog` 是给宿主的普通脚本面（demo 用了，
   `Workflow.razor.cs:198,205`）。新增接口要同时想清「给 C# 的走 export、给宿主 DOM 的走 window」。

---

## 七、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 滚轮缩放、平移、滚动上报、边缘扩张、每帧原子提交 | `wwwroot/veloxdev.workflow.js` 的 `initSurface`（`:540`）/ `applyZoomSurface`（`:343`）/ `scheduleZoomSettle`（`:513`），C# 侧是 `Attached/Workflow/WorkflowSurfaceBehavior.razor.cs` |
| 缩放的枢轴与夹取顺序 | `WorkflowSurfaceBehavior.razor.cs` 的 `OnWheelZoom`（`:184`）—— 数学在 Core `WorkflowSurfaceMath` |
| 节点拖拽的落点、包装盒尺寸、z-index | `Attached/Workflow/WorkflowNodeDragBehavior.razor(.cs)` |
| 插槽连接的两阶段命令 | `Attached/Workflow/WorkflowSlotConnectionBehavior.razor(.cs)` |
| 槽位锚点回传与写回 | `WorkflowSlotLayoutBehavior.razor.cs`（`:54`）+ JS 的 `initSlotLayout`（`:997`） |
| 标尺/网格装饰器 | `WorkflowGridDecorator.razor(.cs)`（模板侧的形状见 `Templates/adapters/razor.md`） |
| 小地图外观与导航 | `WorkflowMinimapOverlay.razor(.cs)` + JS 的 `initMinimap`（`:1198`） |
| 视图池 | `ViewPool.razor(.cs)` |
| 组件 ↔ DOM 的挂钩名 | `wwwroot/veloxdev.workflow.js` §2.3 那四张表 |
| 线程、优先级、pacer、采样器 | `PlatformAdapters/` —— 差异与坑见 `TransitionSystem/adapters/razor.md` |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs` |
| 包结构、TFM、双轨引用 | `VeloxDev.Razor.csproj` |
| **宿主怎么把这些接起来（最小可读样本）** | `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/TreeView.razor`（87 行，三层片段一次看全）；完整版在 `Examples/Workflow/Blazor/Demo/Demo/Components/Pages/Workflow.razor` |

---

## 八、这份文件没写的东西

- 七个角色各自要暴露什么成员、七角色职责表 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 与
  `skills/veloxdev-create-workflow/references/view-layer.md`。
- 这家的平台硬限制与逐条坑（服务端没有元素、几何往返、区域设置全量清单、`TryFind` 是线性扫描、
  两处注释与代码不符）—— `memory/modules/WorkflowSystem/adapters/razor.md`。
- TransitionSystem 侧为什么只有一个采样器、`Property` 手写表的两个缺口、circuit 级同步上下文、
  编组没有失败信号 —— `memory/modules/TransitionSystem/adapters/razor.md`。
- 模板侧的形状、六个 `ToCss` 副本、双重 `GridDecorator` 命名、树模型自订阅 —— `memory/modules/Templates/adapters/razor.md`。
- 其余六家的对应做法 —— `memory/modules/WorkflowSystem/extension.md` §4.3 那张「搬一家」的对照表。
