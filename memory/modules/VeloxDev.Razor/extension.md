# VeloxDev.Razor — 扩展

> 读法：七角色契约与注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> 三条轴上「这家和别家不一样」的地方在 `memory/modules/{WorkflowSystem,TransitionSystem,Templates}/adapters/razor.md`。
> 本文**只回答一件事**：在这个项目里加一个 X，动哪几处、按什么顺序、哪条看着能编译的捷径是错的。
> 目录与轴的对照、`wwwroot/` 的角色划分、C#↔JS 契约的四张表、csproj 的取值见 `architecture.md`，本文不抄。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 让一个类型可动画 | 实现 `ISampler`，再 `RegisterInterpolator(typeof(T), new XSampler())`，**并补手写 `Property` 重载** | `PlatformAdapters/Interpolator.cs:7-10`；`PlatformAdapters/Samplers/`；`PlatformAdapters/Transition.cs:31-129` |
| 换线程句柄 / 优先级 / 解释器 | 改 `TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, NonPriority>` 的型参 | `PlatformAdapters/TransitionScheduler.cs` |
| 让 DynamicTheme 在这家真动画 | 覆写 `InterpolatorCore.CreateScheduler` | `PlatformAdapters/Interpolator.cs:12-15` |
| 主题字符串 → 值 | 实现 `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs`（4 个 public 类） |
| 一条过渡时长预设 | `TransitionEffects` 的三个静态属性（可 `set`，进程级） | `PlatformAdapters/TransitionEffects.cs`（Empty 0s / Theme 0.46s / Hover 0.32s） |
| 一个新的画布操作面 / 换七角色之一 | 在 `Attached/Workflow/` 加一个组件：`.razor`（首行 `@namespace VeloxDev.WorkflowSystem.AttachedBehaviors`）+ `.razor.cs`（`ComponentBase, IAsyncDisposable`），在 `OnAfterRenderAsync(firstRender)` 里 `IsEnabled` 闸门 → `import` → `init*` | `Attached/Workflow/` |
| 在已有画布上插一个覆盖层（装饰器 / 小地图 / HUD） | 实现 `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 的组件，作为 surface 的 `GridDecorator` / `Minimap` **片段参数**传入 | `WorkflowSurfaceBehavior.razor.cs:43,47`；宿主写法 `Examples/Workflow/Blazor Trimmed/Demo/Components/Workflow/TreeView.razor:16-30` |
| JS 侧加一个能被 C# 调的入口 | IIFE 里加函数 + 底部 `export const` 加一行 | `wwwroot/veloxdev.workflow.js`（IIFE `:37`；export 区 `:1349-1366`） |
| 加一份静态资源（新的 css/js/字体） | 放进 `wwwroot/`，宿主用 `_content/VeloxDev.Razor/<文件>` 取 | `VeloxDev.Razor.csproj:1` 的 Razor SDK；宿主的 `<link>` 见 `Examples/Workflow/Blazor/Demo/Demo/Components/App.razor:9` |
| 换视图池的实现 | 同名组件（这家**没有** `ViewManager` —— `ViewPool` 是 `ComponentBase, IDisposable` 的薄包装，池化交给 Blazor 渲染器） | `Attached/Workflow/ViewPool.razor(.cs)` |

**这里没有的扩展点（别去找）：**

- **没有附着属性、没有 `IsEnabled` 回调、没有程序集级入口，也没有 `Initialize()`。** 唯一的静态构造是 `Interpolator.cs:7`；
  「启停一个行为」在这家不是回调，而是**组件的挂载与卸载**（见 §二·3）。
- **没有 `ViewManager` / 模板缓存**（`git grep -ln "class ViewManager" -- Src/Adapters/VeloxDev.Razor` 零命中）——
  这是与 WPF/WinUI/Avalonia/MAUI/Jalium 五家最直接的结构差异，搬别家的代码过来时会发现找不到对应文件。
- **没有「注册一个 ViewModel 类型」的地方**：适配器不 `new` Tree/Node，只消费宿主给的模型
  （`memory/modules/WorkflowSystem/extension.md` 二·20）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条走捷径都能编译通过，其中几条跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 把工作流那份 JS/CSS 复制到宿主的 `wwwroot/` 里改 | 组件 import 的是**包内**路径 `./_content/VeloxDev.Razor/veloxdev.workflow.js`（绝对硬编码在三处）；宿主那份永远不会被加载，改了没反应也不报错 | 改适配器的 `wwwroot/`，宿主只管 `<link>` 与包引用 | `WorkflowSurfaceBehavior.razor.cs:160`、`WorkflowMinimapOverlay.razor.cs:195`、`WorkflowSlotLayoutBehavior.razor.cs:47` |
| 2 | 在 `Attached/` 的组件里直接驱动 `Transition<T>` / `ThemeManager` | 三条轴在程序集内互不引用是既成事实（`Attached/` 零 `Transition`/`Interpolator`/`ThemeManager` 符号，`PlatformAdapters/` 零 `Workflow` 符号）；跨一条就再也拆不开 | 想跨轴在宿主侧接线 | `architecture.md` §一 |
| 3 | 用 `IsEnabled` / `ZoomEnabled` 的变更来启停或改行为 | 两个参数只在 `firstRender && IsEnabled` 那一刻被读（`WorkflowSurfaceBehavior.razor.cs:158`、`WorkflowSlotLayoutBehavior.razor.cs:45`、`WorkflowMinimapOverlay.razor.cs:193`），之后改它们**什么都不会发生**；五家 XAML 适配器有 `OnIsEnabledChanged`，这家没有 | 启停 = 挂载/卸载组件；要「运行时可变」就在组件内部自己订阅参数变化并自己重初始化 | 同上三处；对照 `WorkflowSystem/adapters/razor.md` 的注册差异表 |
| 4 | 缩放时让节点/插槽/连线各自按模型自己算几何 | 缩放手势期间会与 JS 的原子提交打架 —— 一次 `applyZoomSurface` 才是一个帧的权威，散写的几何会被画成半新半旧（闪烁/错位） | 在这家让开：几何写手检查 `WorkflowGeometryScope.IsZooming` 后 `return` | `WorkflowNodeDragBehavior.razor.cs:98`、`:115`；`WorkflowGeometryScope.cs` |
| 5 | 在跨端参数里传「C# 自己算的」host / scroll / 内容尺寸 | 边缘预留（自动扩建的那部分）**只有 JS 知道**；.NET 的 `_offsetX/_offsetY` 只是「不小于标尺厚度」的镜像，是滞后的 | 传**有效长度**（`DOM 值 − 边缘预留`），让 JS 再把自己那份预留加回去 | `veloxdev.workflow.js:335-338`；`WorkflowSurfaceBehavior.razor.cs:141-142`、`:240-244` |
| 6 | 省掉节点/插槽上的 `data-veloxdev-*` 属性 | JS 靠它们建 `{id → wrapper}` 映射与量插槽；找不到就**静默**跳过（不抛、不报） | 每个 node wrapper 写 `data-veloxdev-node-id`，每个 slot 写 `data-veloxdev-slot-id` | `WorkflowNodeDragBehavior.razor:5`、`WorkflowSlotConnectionBehavior.razor:3`；JS `:381-387`、`:997` |
| 7 | 两个 surface 都用默认的 `ScrollViewerId` / `CanvasId` | 10 个模块级字典都以滚动器 id 为键，同页第二个 surface 会覆盖第一个的注册项 ⇒ 前者滚动不上报、边缘不扩张，两个小地图一起指向后者。**不报错** | 同页多画布必须显式传不同的 `ScrollViewerId` | JS `:66,71,76,80,86,92,423,433,436,538`；默认值在 `WorkflowSurfaceBehavior.razor.cs:35` |
| 8 | 让 surface 自己去「轮询模型」来刷新节点 | 服务端没有元素，模型变化不会自己过桥；适配器只有一个 `SurfaceViewportFeed` 是给**装饰器**用的廉价通道（`WorkflowGridDecorator.razor.cs:101`），节点/连线的重渲染归**模板** | 模型 → `PropertyChanged` → `StateHasChanged`，由模板组件负责（`memory/modules/Templates/adapters/razor.md` §二·2） | `WorkflowSurfaceBehavior.razor.cs:109-111` |
| 9 | 给 `WorkflowMinimapOverlay` 不传 `ScrollViewerId`，指望它自己找 | 参数没有默认值（可空），`initMinimap` 被 `!string.IsNullOrWhiteSpace(ScrollViewerId)` 挡住 ⇒ 小地图只画不动，视口块也不出现（它的 `x/y/width/height` 全由 JS 写） | surface 与 minimap **各传一次**同样的 id | `WorkflowMinimapOverlay.razor.cs:82`、`:193`；`WorkflowMinimapOverlay.razor:21-23`；宿主 `TreeView.razor:27` |
| 10 | 把 `CanvasId` 当有用的参数去配 | 它被渲染成元素 id，但 JS 全部用 class 找画布，`initSurface` 的 9 个形参里也没有它 | 忽略它（改它没有任何后果） | `WorkflowSurfaceBehavior.razor:10`、`.razor.cs:165-166`；JS `:540` |
| 11 | 在 `PlatformAdapters/Samplers/` 加个 `ISampler` 类就以为生效了 | 注册表是唯一开关，不登记 = **永不运行**；而这家还多一道：手写 `Property` 表没有对应重载时，用户在 `.Property(...)` 里**根本写不出来** | 建文件 → 静态构造登记 → 补 `Transition.cs` 的重载 → 补测试条目 | `Interpolator.cs:7-10`；`Transition.cs:31-102` |
| 12 | 采样器类名与别家重名 | 七家共用同一个命名空间 `VeloxDev.Adapters.NativeSamplers`；同时引用两家的工程（如 `Examples/Transition/AUTO TEST`）会类型歧义 | 类名全仓唯一（`StringSampler` 就是全仓唯一的一个）；确实要重名就照 `Samplers/WpfEntries.cs` 的反射别名写法绕 | `Samplers/StringSampler.cs:10`（`class StringSampler` 全仓仅此一处）；`RazorEntries.cs:2` |
| 13 | 加新的 CSS 时按「纯外观」对待 | 本家有三处 CSS 是**测量契约**（宿主尺寸、内容层定位、插槽盒贴字形），改动会改几何行为 | 改前先读 `architecture.md` §2.5 那张表 | `veloxdev.workflow.css:27`、`:85-90`、`:223-230`、`:233` |

---

## 三、步骤清单

### A. 加一个可动画的类型（加一个采样器）—— 最常见的路径

1. **建文件** `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 `VeloxDev.Adapters.NativeSamplers`，实现 `ISampler`。
   端点约定：`t == 0` / `t == 1` 原样交出调用方给的起点/终点**实例**（本家范式 `StringSampler.cs:26-27`，
   理由写在它的 XML `:18-19`：字符串有损，端点不能归一化）。
2. **登记**：`PlatformAdapters/Interpolator.cs:7-10` 的静态构造里加一行 `RegisterInterpolator(typeof(X), new XxxSampler());`。
   **不登记 = 永不运行**，没有任何东西会报错。
3. **补手写重载**（**这家独有、别家不需要的一步**）：若新类型要能被用户声明出来，在 `PlatformAdapters/Transition.cs:31-102`
   的 `Property` 重载表里加一个 —— 这张表是手写的，没有泛型兜底。**只加采样器不加重载的后果在这家是「写不出来」**，
   不是别家的「运行期静默」。（表的两处已知缺口见 `memory/modules/TransitionSystem/adapters/razor.md` §二·3。）
4. **补验证条目**（**最容易漏、漏了直接红**）：`Examples/Transition/AUTO TEST/Samplers/RazorEntries.cs` 里加大致三样：
   一个带目标属性的私有 `Target` 类、一条 `EntryFactory.Create<XxxSampler, Target, T>(...)`、放进 `All`（现只有一个颜色条目）。
   判定是**反射**所有 `VeloxDev.*` 程序集里的 `ISampler` 再与 `SamplerRegistry.Entries` 求差
   （`Samplers/SamplerCoverageTests.cs:44-47`、`:64-87`），而 `VeloxDev.Razor` 必须在
   `ExpectedAdapterAssemblies` 名单里、且被真正加载（`:23-33`、`:50-62`）。
5. **同步** `memory/modules/TransitionSystem/adapters/razor.md` 的条数与清单。
6. **别按这家的条数去猜别家**：各家注册数本来就不等（清单在 `memory/modules/TransitionSystem/adapters/razor.md`）。

> 判断「我登记对了没」：`RegisterInterpolator` 什么也不告诉你，`TryGetInterpolator` 才告诉你。**没有「列出全部登记项」的公开 API。**

### B. 加一个视图行为 / 视图角色（这家独有的路径）

1. **建两个文件** `Attached/Workflow/Xxx.razor` + `Xxx.razor.cs`；`.razor` **第一行**必须是
   `@namespace VeloxDev.WorkflowSystem.AttachedBehaviors`（七家共用的同一份命名空间，Core 里不存在；换它 = 宿主找不到、且不报错）。
2. **类声明**：`public partial class Xxx : ComponentBase, IAsyncDisposable`，`[Inject] private IJSRuntime JS`。
3. **参数**：`[Parameter]`，按七角色的契约成员一一对应（契约清单在 `WorkflowSystem/extension.md` §3.9）。
   对外参数请**给默认值** —— 这家有反面案例：`WorkflowMinimapOverlay.ScrollViewerId` 没有默认值，
   忘了传的表现是「组件还在、只是永远不动」（§二·9）。
4. **初始化**：`OnAfterRenderAsync(bool firstRender)` 里 `if (firstRender && IsEnabled)` → `import` 模块 →
   `InvokeAsync<IJSObjectReference>("init*", element, DotNetObjectReference.Create(this))` → **存句柄**。
   `firstRender` 是闸门不是提示：组件若第一帧被 `@if` 挡住，之后再出现也不会初始化。
5. **每元素状态**：放**组件实例字段**。**不要**写 `static Dictionary<...>` 存状态 —— Blazor Server 一个进程多个 circuit，
   static 是跨用户共享的（`memory/modules/TransitionSystem/adapters/razor.md` §四·3 有同一件事的判据）。
   跨组件共享请用 `CascadingValue`（本家的范式是 `SurfaceViewportFeed`）。
6. **释放**：`DisposeAsync` 里逐项 `try/catch` + `await _handle.InvokeVoidAsync("dispose")` → `_handle.DisposeAsync()` →
   `_module.DisposeAsync()`（三处同形：`WorkflowSurfaceBehavior.razor.cs:481-532`、`WorkflowNodeDragBehavior.razor.cs:177-215`、
   `WorkflowMinimapOverlay.razor.cs:403-453`）—— JS 侧句柄的 `dispose` 会摘掉监听器并清该 surface 的注册项，别省。
   模型/`SurfaceViewportFeed` 的**订阅与延时回调**另有两种既有收尾形状（退订 / `CancellationTokenSource` 取消），
   见 `architecture.md` §六·7；全模块没有 `_disposed` 标记，别去引一个不存在的惯例。
7. **JS 侧**：新的注册表/状态一律**以滚动器 id 为键**，别用常量键；`init*` 要返回一个带 `dispose` 的句柄对象。
8. **联动**见 §四·2。

### C. 改 JS 契约（加一个跨端入口或参数）

1. 在 IIFE 里加函数（保持「一个函数一个职责」的现状）；**要给 C# 用的，在底部 `export const` 区（`:1349-1366`）补一行**
   —— C# 是通过 ES 模块导入的，导出漏了 C# 调不到。给宿主 DOM 用的则挂 `window`（两个 `window.*` 工具函数就是这种）。
2. **参数形状**：跨端数组一律 `string[][]`（本家的既有形状 —— `OnSlotLayoutBatch` 与 `applyZoomSurface` 的 `nodeGeometry`），
   数字在 C# 侧用 `"0.###"` + `InvariantCulture` 格式化。**写 CSS/SVG 的 `double` 必须不变文化**，
   解析侧同样（`WorkflowSlotLayoutBehavior.razor.cs:70` 是现存的漏网，清单见 `WorkflowSystem/adapters/razor.md` §四·1）。
3. **问一句「这个长度是谁在权威维护」**：涉及边缘预留/宿主尺寸的，按 §二·5 传有效长度。
4. **若它参与缩放**：必须落在 `applyZoomSurface` 的**同一个同步块**里，并把结果写进 `surfaceZoomState` 的戳
   （否则会被 250ms 尾窗之外的一次迟到渲染盖回去）。改完自查 `architecture.md` §2.4 的七步顺序。
5. **同步文档**：`architecture.md` §2.3 的四张表（JS→.NET 载荷、.NET→JS 载荷、两侧的 DOM 钩子）—— 那是契约的唯一清单。

### D. 加一个网格装饰器 / 小地图实现

在组件里实现 `IWorkflowGridDecorator`（`ScrollOffsetX/Y`、`ContentOffsetX/Y`、只读 `RulerBand`）或
`IWorkflowMinimapOverlay`（额外 `ViewportWidth/Height`、`WorkflowTree`、`IsMinimapVisible`），
然后**作为片段参数**传进 surface（`WorkflowGridDecorator.razor.cs:26` 的 `[CascadingParameter] SurfaceViewportFeed` 是廉价刷新通道）。
- `RulerBand` 要转发给 `WorkflowSpatialEx.SetVirtualizeInset`，否则有标尺那条边上的节点看不见
  （`memory/modules/WorkflowSystem/extension.md` §3.8）。
- 最小地图数学在 Core 的 `WorkflowSurfaceMath`（`MinimapFit` / `MinimapLocal` …）—— 别自己推。
- 现成实现的 `RulerBand` 返回 `0`（`WorkflowMinimapOverlay.razor.cs:97`），是「标尺避让在模板里做」的产物，不是漏写。

### E. 改 TFM / 引用方式

1. 本家是**单 TFM** `net6.0`（`VeloxDev.Razor.csproj:4`），`Transition.cs:104` 的 `#if !NETSTANDARD2_0` 因此**恒为真**；
   加回 `netstandard2.0` 会让那 4 个 `System.Numerics` 重载消失。加 TFM 还会改变 `wwwroot/` 的静态资源打包面（RCL 按 TFM 分发）。
2. `:26` / `:27` 是一对**互斥**的双轨（Debug `ProjectReference` / 非 Debug `PackageReference`），改一条要同时看另一条。
3. **别用 `obj/` 下的目录推断 TFM**：那里有 csproj 未声明的 `net8.0` / `net10.0` 产物目录（来自树外的属性覆盖）。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一个采样器

- [ ] `Src/Adapters/VeloxDev.Razor/PlatformAdapters/Samplers/XxxSampler.cs`（命名空间 `VeloxDev.Adapters.NativeSamplers`，类名全仓唯一）
- [ ] `PlatformAdapters/Interpolator.cs:7-10` 登记（**不登记 = 永不运行**）
- [ ] `PlatformAdapters/Transition.cs:31-129` 补手写 `Property` 重载（这家独有；缺了是「声明不出来」）
- [ ] `Examples/Transition/AUTO TEST/Samplers/RazorEntries.cs`：`Target` 属性 + 一条 `Entry` + 放进 `All`（**漏了直接红**）
- [ ] `memory/modules/TransitionSystem/adapters/razor.md`
- [ ] 这条类型在别家也该有时：各家 `Interpolator.cs` 各加一份 —— **条数本来就不等**，别按这家猜

### 4.2 加 / 换一个视图组件，或改 JS 契约

- [ ] `Src/Adapters/VeloxDev.Razor/Attached/Workflow/` 的 `.razor` + `.razor.cs`
- [ ] `wwwroot/veloxdev.workflow.js`（`init*` / 函数 / 底部 `export const`）与 `wwwroot/veloxdev.workflow.css`
- [ ] `Src/Templates/VeloxDev.Razor.Templates/working/content/` 下 7 个条目里对应的那个
      （`workflow-tree-view` / `-node-view` / `-slot-view` / `-link-view` / `-grid-decorator` / `-minimap-overlay` / `-template-selector`）
- [ ] **消费方要写的 DOM 钩子**：`.veloxdev-wf-node-card` / `.veloxdev-wf-slot-svg` / `data-veloxdev-link-id` 这三个是模板/demo 提供的，
      适配器保证不了它们（见 `architecture.md` §2.3(c)）—— 模板改了这几处，深缩放的连线/卡片就会静默不跟手
- [ ] 两套 demo 都要过：`Examples/Workflow/Blazor/Demo/` 与 `Examples/Workflow/Blazor Trimmed/Demo/`
- [ ] `skills/veloxdev-create-workflow/references/gui/razor.md`
- [ ] `memory/modules/WorkflowSystem/adapters/razor.md`；模板侧形状变了才动 `memory/modules/Templates/adapters/razor.md`
- [ ] `VeloxDev.slnx` **只在新增项目时**才动

### 4.3 以这家为模板搬一个新平台（**多数东西不能抄**）

| 这家的东西 | 能抄吗 |
|---|---|
| `wwwroot/` + `_content/` 模块导入路径 | **不能** —— 这是「服务端 + 浏览器」两家制才有的形状；XAML 那几家没有浏览器这一侧 |
| 「组件即行为」+ `[Parameter]` + `RenderFragment` 槽 | **不能** —— 别家有附着属性/`DataTemplate` 那套；契约成员相同、装配方式不同 |
| `firstRender && IsEnabled` 的闸门 | **不能直接抄** —— 这是「没有 `IsEnabled` 回调」的结果；XAML 那几家有 `OnIsEnabledChanged`（WinForms 除外） |
| `WorkflowGeometryScope`（`AsyncLocal` 深度计数）「缩放中让开」 | 思路可抄，**但它在 Core 侧**，别在适配器里复制一份 |
| 原子提交顺序（枢轴 → `Scale` → `EnsureNegativeCover` → 重布局 → `PivotCenterScroll` → `ClampScrollOffset`） | 该抄的在这里 —— 但它是**契约**，见 `memory/modules/WorkflowSystem/extension.md` §3.9 与 `WorkflowSurfaceMath` |
| `WorkflowGridDecorator` 放在**适配器**里（而非模板包） | 与 Jalium 同族的选择，可以对照；另五家把它放模板包（见 `WorkflowSystem/extension.md` §4.3 的对照表） |

其余注册位置（`VeloxDev.slnx`、7 个模板条目、两套 demo、skill 平台页、`adapters/<平台>.md`）见
`memory/modules/WorkflowSystem/extension.md` §4.3，不重复。

---

## 五、几个「以为能改、其实不该改」的地方

1. **`SETTLE_TAIL_MS = 250`（`veloxdev.workflow.js:512`）** 不是调参旋钮：它是「一轮 SignalR 往返 + 渲染」的量级估计，
   改小会漏掉迟到的 .NET 渲染（闪烁回来），改大会与用户的真实平移/拖拽抢。配套的 `pointerdown` 删戳（`:749`）
   是它「不跟手势打架」的另一半，也不能删。
2. **设计宽 `260` 有两处（`:223`、`:475`），且它是「消费方卡片的设计宽」**：改它要同时改模板 node-view 的定尺寸卡片，
   否则缩放因子按错误的分母算（模板侧的另一份 260 见 `memory/modules/Templates/adapters/razor.md` P7）。
3. **CSS 里那三处承重声明**（`architecture.md` §2.5）：宿主尺寸归 JS、内容层靠 `left/top` 平移、插槽盒贴字形。
   把 `.veloxdev-wf-canvas-host` 的 `width:0;height:0` 改成初值、或删掉 `line-height:0`，行为都会变。
4. **`GlobalUsings.cs` 三行与七家逐字相同** —— 它不是这家的差异点，改它等于改七家（且 `global using` 不随包传，改了对宿主也没用）。
5. **那 5 个零调用导出**（`getCanvasTranslate` / `getViewportSize` / `scrollToRatio` / `scrollByDelta` / `ensureCanvasSize`）：
   它们在 C# 侧没有调用点，但都是 `window.veloxdevWorkflow` 的**公开面**（demo 就通过全局对象调了 `scrollToPosition`）。
   删之前先确认没有宿主在用；其中 `ensureCanvasSize` 的注释还是过期的（`architecture.md` §六·4）。
6. **`WorkflowMinimapOverlay.ViewportFill` 的适配器默认值（`:66`）改了不影响生成的项目**：
   模板写死 `transparent`（见 `Templates/adapters/razor.md` P4），要改观感得改模板那一行。
7. **`Interpolator.cs:7-10` 那一行不要随手加类型当试验**：加错了不会报错，只会让某个类型静默走到「无采样器」路径
   （`decimal` 就是六家共有的这一格：声明得出、没有采样器）。
