# VeloxDev.Jalium — 扩展

> 读法：七角色契约、附着属性名、注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3 —— **但 Jalium 侧那六个行为角色已从适配器删除**，现在只有模板产物在实现它们（见下 §一）。
> 两条轴上「这家和别家不一样」的地方在 `memory/modules/{TransitionSystem,WorkflowSystem,Templates}/adapters/jalium.md`。
> 本文**只回答一件事**：在这个项目里加一个 X，动哪几处、按什么顺序、哪条看着能编译的捷径是错的。
> 目录与轴的对照、登记表所在、csproj 的取值、陷阱清单见 `architecture.md`，本文不抄。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 让一个 Jalium 类型可动画 | 实现 `ISampler`，再 `RegisterInterpolator(typeof(T), new XSampler())` | `PlatformAdapters/Interpolator.cs:14-23`（唯一的登记表，10 行） |
| 换线程句柄 / 优先级 / 解释器 | 改 `TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>` 的类型实参 | `PlatformAdapters/TransitionScheduler.cs:5-8` |
| 一条过渡时长预设 | `TransitionEffects` 的三个静态属性（可 `set`，进程级） | `PlatformAdapters/TransitionEffects.cs:5` / `:9` / `:13` |
| 让 `.Property(...)` 能顺滑写某个 Jalium 类型 | 加一个 `Property(Expression<Func<T, TXxx>>, TXxx, object?)` 重载 | `PlatformAdapters/Transition.cs:34-137`（**这一家 13 个**） |
| 换「item 类型 → 视图」的工厂 | 实现 `IWorkflowTemplateSelector.CreateView(object item)` | `Attached/Workflow/IWorkflowTemplateSelector.cs:7`（11 行，`DataTemplateSelector` 的等价物） |
| 换池化行为 | 改 `ViewPool` 的两个附着属性 / `ViewManager` 的集合同步 | `Attached/Workflow/ViewPool.cs:15-25`、`ViewManager.cs:33-49` |
| 小地图换皮 | 继承 `WorkflowMinimapOverlay`（`public class`，可继承），或实现 `IWorkflowMinimapOverlay` | `Attached/Workflow/WorkflowMinimapOverlay.cs:15` |
| 让 DynamicTheme 在这一家真动画 | 覆写 `InterpolatorCore.CreateScheduler` —— **已实现但本仓库无人调用** | `PlatformAdapters/Interpolator.cs:26-29`；理由见 `architecture.md` §一 |

**这里没有的扩展点（别去找）：**

- **七个视图角色的行为不在适配器里。** `WorkflowSurfaceBehavior` / `WorkflowCanvasTransformBehavior` / `WorkflowNodeDragBehavior` / `WorkflowSlotConnectionBehavior` / `WorkflowSlotLayoutBehavior` / `WorkflowGridDecorator` 与自装配外壳 `WorkflowTreeView` 已删除（零消费者）。要加 / 改画布行为、拖拽、插槽、网格标尺，改的是模板产物 `workflow-tree-view` 的 `TemplateClass : Canvas`（`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs`）或 demo 表面 —— 见 `architecture.md` §四。
- **没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造是 `Interpolator.cs:10`。所以「注册」这件事没有统一挂点 —— 要么落在那行静态构造里，要么由宿主自己调。
- **没有 `ThemeValueConverters.cs`，也没有主题 demo**：这一条轴在这家是空的（`architecture.md` §一）。
- **没有标记语言**：宿主无法用 XAML 挂附着属性，也没有 XAML 名字作用域可借 —— 名字由表面自己在代码里 `NameScope.SetNameScope` + `RegisterName` 造出来（模板产物 `TemplateClass.cs`），适配器不参与。
- **没有 `adapters/` 子目录**：模块名本身就是平台。
- **没有「注册一个 ViewModel 类型」的地方**：适配器不 `new` Tree/Node，只消费宿主给的模型。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条走捷径都能编译通过，其中几条跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 在 `Samplers/` 加一个 `ISampler` 类就以为生效了 | 登记表是唯一开关；没登记 = 该采样器**永不运行**，且库里没有任何东西会报错 | 同时在 `Interpolator` 的静态构造里加一行 | `PlatformAdapters/Interpolator.cs:14-23` |
| 2 | 登记了但忘了改测试表 | 不会编译错，运行到 `EveryShippedSampler_IsAccountedFor` 才红：它反射所有 `VeloxDev.*` 程序集里的 `ISampler`，再与 `SamplerRegistry.Entries` ∪ `UnreachableSamplers.All` 求差 | `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加一条 `Entry(...)`（`All` 在 `:183-257`，`Adapter` 常量在 `:34`） | `Samplers/SamplerCoverageTests.cs:65-87`；`SamplerRegistry.cs:18-22` → `AdapterSamplerEntries.cs:15` → `JaliumEntries.All` |
| 3 | 改采样器类名只改文件 | 测试按**字符串**反射取类型：`Type.GetType($"VeloxDev.Adapters.NativeSamplers.{name}, VeloxDev.Jalium", throwOnError: true)`。编译不报错，**测试运行时才炸** | 改名要同步 `JaliumEntries.cs` 里那 9 个字符串 | `JaliumEntries.cs:38-39`（`throwOnError: true`） |
| 4 | 把登记代码写在采样器自己的文件里 / `App` 里 / `Main` 里 | 没人保证它会跑在第一个 `new Transition<T>()` 之前；`RegisterInterpolator` 是**末位胜出**，谁先谁后直接改解析结果 | 只在静态构造里集中登记 | Core `Transition.cs` 的字段初始化器 `protected TInterpolatorCore interpolator = new();`；`Src/Core/VeloxDev.Core.Test/TransitionSystem/InterpolatorCoreTests.cs` 的 `RegisterInterpolator_OverwritesExisting` |
| 5 | 照 WPF 的样子「登记基类型接住整族」，于是给 `TranslateTransform` / `RotateTransform` / `TransformGroup` 各登记一条 | 这家的姿态是**只登记基类型**（`typeof(Transform)`）＋ 精确类型的少数几条；多余登记会被精确命中抢先，让本该走基类回溯的路径换成另一条实例 | 只登记 `Transform` 与 `Media3D.Transform3D` 两条 | `PlatformAdapters/Interpolator.cs:22-23` |
| 6 | 只设 `ViewPool.ItemsSource`（或只设 `TemplateSelector`）就以为视图会出来 | 两个 DP 共用 `OnChanged`，只有**两者都非空**才建 manager；否则走 `else` 分支把 manager `Detach()` | 两个一起给 | `Attached/Workflow/ViewPool.cs:19`/`:25`/`:58-73` |
| 7 | 把 `Property(…, ICollection<Transform>)` 的「单个直接赋值」分支改成统一 `TransformGroup` | 会改运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径 | 保持 `Count == 1` 直赋、其余包组 | `PlatformAdapters/Transition.cs:48-66`（注释 `:51-53`） |
| 8 | 把 `.Property(...)` 的某个重载「补进 Core」 | Core 的 `TransitionCore<...>` 里**一个 `Property` 都没有**，13/28/30/17 个重载分别写在各家适配器里 —— 补进 Core 等于改七家的形状 | 重载留在本家 `PlatformAdapters/Transition.cs` | `Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs` 全文无 `Property` 方法；四家的条数见 `architecture.md` §2.3 |
| 9 | 让 `Attached/` 里的东西直接驱动一个 `Transition<T>`（或反过来） | 两条轴在程序集内互不引用是既成事实；跨一条就再也拆不开 | 想跨轴就在宿主侧接线 | `architecture.md` §一 |
| 10 | 按别家的条数猜这一家有几种采样器 | 条数本来就不等（Jalium 9；WPF 12、Avalonia 14、WinUI 10、WinForms 1、Razor 1 —— 见 `memory/modules/TransitionSystem/adapters/jalium.md`） | 按 `Interpolator.cs:14-23` 现读 | 同左 |

---

## 三、步骤清单

### A. 让一个新的 Jalium 类型可动画（加一个采样器）—— 最常见的路径

1. **建文件** `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 **`VeloxDev.Adapters.NativeSamplers`**（七家共用这个名字，撞名时这里是唯一能编得过去的位置），实现 `ISampler`。端点约定：`t == 0` / `t == 1` 原样交出调用方给的起点/终点实例（范式见 `PlatformAdapters/Samplers/TransformSampler.cs:22-23`，它还带一条「草稿实例类型与起点不同就重建」的守卫）。
2. **登记**：`PlatformAdapters/Interpolator.cs:14-23` 加一行 `RegisterInterpolator(typeof(X), new XxxSampler());`。要登记的是**基类型**还是**精确类型**，照 §二·5 的姿态选。
3. **补验证表**（**最容易漏的一步**）：`Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs` 加 ① 私有 `Target` 类上的一条属性（`:42-53`），② 一条 `Entry("XxxSampler", SamplerRule.…, start, end, x => x.属性, t => 闭式解)`，放进 `All`（`:183-257`）。漏了不会在库里报错，但 `EveryShippedSampler_IsAccountedFor` 会红。
4. **可选**：给 `.Property(...)` 加一个对应重载（§三·D），否则调用方只能走泛型 `Property<TValue>`。
5. **同步 `memory/modules/TransitionSystem/adapters/jalium.md`** 的条数与清单。

> 判断「我登记对了没」：`RegisterInterpolator` 返回什么也不告诉你，**没有「列出全部登记项」的公开 API**。最快的自查是跑一次 `Examples/Transition/Jalium/Demo/`。

### B. 给 Jalium 表面加 / 换一个视图角色

**这一家没有「往适配器加一个附着行为」这条路** —— 六个角色行为与自装配外壳已删除（§一）。宿主表面是模板产物 `workflow-tree-view` 的 `TemplateClass : Canvas`（或 demo 里同名的手写表面），它自绘、自命中、自管视口与虚拟化：

1. **改表面本体**：`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs`，或 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs`（两者同源）。节点的位置 / 尺寸 / 自盒化由 `NodeView` / `LinkView` 自己算。
2. **要挂 pool 的对象**（如新写一个 `Canvas` 子类）必须自己按代码装配：`ViewPool.SetItemsSource(this, …)` + `ViewPool.SetTemplateSelector(this, …)` 两个一起给（§二·6），并保证 `TemplateSelector` 在 `ItemsSource` 之前或同时就位（`ViewManager.AddItem` 在 `_selector is null` 时**静默返回**，`ViewManager.cs:131`）。
3. **没有 XAML 名字作用域可借**：表面要 `FindName` 找部件就得自己 `NameScope.SetNameScope` + `RegisterName`（模板产物里是范本）。
4. **联动清单见 §四·4.2**。

### C. 让宿主接上适配器剩下那三类

Jalium 适配器现在只对外提供三样东西：池化（`ViewPool`/`ViewManager`/`IWorkflowTemplateSelector`）、小地图（`WorkflowMinimapOverlay`）、过渡轴的 `Transition<T>`。宿主接线：

1. **池化**：`ViewPool.SetItemsSource(surface, tree.Helper.VisibleItems)` + `ViewPool.SetTemplateSelector(surface, selector)`，两个一起（§二·6）。参见 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs:114-115`。
2. **小地图**：`new` 一个继承 `WorkflowMinimapOverlay` 的子类，**自己赋 `ScrollViewer`**（`MainWindow.cs:64`）—— 适配器里没有任何东西替它赋，不赋就是「只看不动」（`architecture.md` §3.2）。
3. **缩放与视口全在窗口侧**：demo 的 `MainWindow.cs:146-155`（窗口级 Ctrl+wheel）→ 自己的 `ZoomBy`（`:165-229`）→ `surface.NotifyZoomCommitted(...)`。适配器不参与。

### D. 给 `.Property(...)` 加一个重载

1. `PlatformAdapters/Transition.cs` 内，形状一律是：`public Transition<T> Property(Expression<Func<T, TXxx>> propertyLambda, TXxx newValue, object? interpolationOptions = null)`，体三行 —— `state.SetValue(...)` → 可选 `state.SetOptions(...)` → `return this;`。
2. 新重载**必须**与 `Interpolator` 的登记项对应；不对应的重载是死代码（能编译、能用，但那个类型的动画走的是泛型 `Property<TValue>` 或直接不生效）。
3. **不要**把重载搬到 Core：见 §二·8。

### E. 改 TFM / 引用方式

1. `VeloxDev.Jalium.csproj:7` 是**单 TFM、不带平台后缀**的 `net10.0`，`:4-6` 的注释写明了理由（只用跨平台核心，不用 `Jalium.UI.Desktop`）。改成 `net10.0-windows` 会让这个包**不再能服务 Linux / Android**，那是比「多一个 TFM」大得多的改动。
2. `:27` / `:28` 是一对**互斥**的双轨（Debug `ProjectReference` / 非 Debug `PackageReference`），改一条要同时看另一条 —— 两者同时生效会报重复成员。
3. `Jalium.UI.Controls`（适配器，`:32`）与 `Jalium.UI.Desktop`（消费 demo）是**两个包、两个版本位**；升其中一个不必同步另一个，但 `Jalium.UI.Controls` 是「能编过 `Canvas`/`DrawingContext` 的最低包」，降级会缺绘制面。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一个平台采样器

- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Samplers/XxxSampler.cs`（命名空间 `VeloxDev.Adapters.NativeSamplers`）
- [ ] `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Interpolator.cs:14-23` 登记（**不登记 = 永不运行**）
- [ ] `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`：`Target` 属性 + 一条 `Entry`（**漏了直接红**）
- [ ] 要进流的 `.Property(...)` 重载：`Src/Adapters/VeloxDev.Jalium/PlatformAdapters/Transition.cs`
- [ ] 与 Core / 别家撞名时，命名空间留在 `VeloxDev.Adapters.NativeSamplers`（同时引用两家的工程必然 CS0433，绕法是 `JaliumEntries.cs:38-39` 那条按程序集限定名的反射）
- [ ] `memory/modules/TransitionSystem/adapters/jalium.md`（条数与清单）

> **这一家没有 `UnreachableSamplers` 条目**（`UnreachableSamplers.cs` 的 6 条全是 WinUI / MAUI），所以**每一个** Jalium 采样器都必须在 `JaliumEntries.All` 里被闭式解验 —— 没有「先挂个理由放着」这条退路。这条退路本身也是可证伪的：`EveryUnreachableSampler_NeedsAValueThatCannotBeBuiltHere` 会真的去构造那个端点值（`SamplerCoverageTests.cs:89-100`）。

### 4.2 加 / 换一个工作流视图角色（现在落在模板/demo，不在适配器）

- [ ] **表面本体**：`Src/Templates/VeloxDev.Jalium.Templates/working/content/workflow-tree-view/TemplateClass.cs` 与 demo `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/TreeView.cs`（同源，五类机械改动的源文件）
- [ ] `Src/Templates/VeloxDev.Jalium.Templates/working/content/` 下 7 个条目里对应的那个（`workflow-tree-view` / `-node-view` / `-slot-view` / `-link-view` / `-grid-decorator` / `-minimap-overlay` / `-template-selector`）—— 注意**这一家三个条目产出的是 `static class`**，形状差异见 `memory/modules/Templates/adapters/jalium.md` §一
- [ ] 若新表面要池化：`ViewPool.SetItemsSource` / `SetTemplateSelector` 两个一起给（`ViewManager.cs:131` 静默返回）
- [ ] 两套 demo 都要过：`Examples/Workflow/Jalium/Demo/`（自己写的 `NodeEditorSurface : Canvas`）与 `Examples/Workflow/Jalium Trimmed/Demo/`（模板那条）
- [ ] `skills/veloxdev-create-workflow/references/gui/jalium.md`
- [ ] `memory/modules/WorkflowSystem/adapters/jalium.md`
- [ ] `memory/modules/Templates/adapters/jalium.md`（模板侧形状变了才动）
- [ ] `VeloxDev.slnx` **只在新增项目时**才动（适配器本身早就在里面）

---

## 五、几个「以为能改、其实不该改」的地方

1. **`TransitionEffects` 的三个时长**（`PlatformAdapters/TransitionEffects.cs:5/9/13`：`Empty` 0s、`Theme` 0.46s、`Hover` 0.32s）**六家逐字相同** —— 改这里等于改所有平台的默认观感，不是 Jalium 一家的事。
2. **`Interpolator.cs:21` 那条看似冗余的 `SolidColorBrush` 登记不要顺手删。** 它确实被 `typeof(Brush)`（`:20`）的基类回溯覆盖，删了多半不报错也不改行为 —— 但它防的是「属性声明成 `SolidColorBrush`」这一类，代价为零。**要删就先跑一遍 `Examples/Transition/Jalium/Demo/`。**
3. **`_zoomPin` / `NotifyZoomCommitted` 只属于表面，不要往适配器里搬。** 这套守卫的成立前提是「表面自己调 `Virtualize`」；适配器现在没有表面，搬进去就是给一个不存在的时序做守卫。要的是「缩放后立刻重算可见集」，那是表面的事（对照 `Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:350-399` 的做法）。
4. **七家的采样器类名不要「统一化」**（如 `PointSampler` → `JaliumPointSampler`）：命名空间的跨家重名是既成事实，改名只会让两处字符串表（`JaliumEntries.cs:38-39`、以及别家同名反射）同时错位。
