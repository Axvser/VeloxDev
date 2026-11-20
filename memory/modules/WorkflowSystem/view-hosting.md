# 可见集 → 视图：Core 视图系统

> 把「虚拟化产出的 `VisibleItems` 如何变成 UI 视图」按 TransitionSystem 的分层方式组织：**接口定层次、抽象类/泛型类跑通用流程、适配器逐层实现、最后一个泛型类型组合成系统**。
> WPF / Avalonia / WinUI / MAUI / WinForms / Jalium 都用 `ViewPool` 门面接入 `ViewSourcePipelineCore`；Razor 是 renderer-native 例外（§七）。所有七家的 XAML/组件语法保持 `ViewPool.*` 不变。

---

## 一、目录与角色

### 契约（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/ViewHosting/`）

| 文件 | 类型 | 职责 |
|---|---|---|
| `IViewFactory.cs` | `IViewFactory<TItem,TView>` | `WantsItem` / `CreateView`：item → view，模板解析归这一层 |
| `IViewContainer.cs` | `IViewContainer<THost,TView,TItem>` | `Attach` / `Detach` / `Activate` / `Deactivate` / `SetZOrder` / `Destroy`：视觉树成员、绑定态、层级与丢弃 |
| `IViewArranger.cs` | `IViewArranger<TItem,TView>` | `Arrange`：几何写入；绑定驱动的平台为 no-op |
| `IViewScheduler.cs` | `IViewScheduler<TPriorityCore>` | `BatchPriority` / `Schedule`：把一批视图物化送到宿主线程 |
| `IViewDiagnostics.cs` | `IViewDiagnostics<TItem>` | `Failed(item, stage, exception)`：单项失败上报 |
| `IViewPoolPolicy.cs` | `IViewPoolPolicy` | `BatchSize` / `FrameBudgetMilliseconds` / `MaxRecycledViewsPerType`：批上限、每批时间预算与回收上限 |
| `IViewBatchFeedback.cs` | `IViewBatchFeedback` | 每批结束回报 `processed / elapsedTicks / pending`，供自适应策略闭环调参 |
| `IViewLayerPolicy.cs` | `IViewLayerPolicy<TItem>` | `GetZOrder`：z 序策略 |
| `IViewLinkLayer.cs` | `IViewLinkLayer<THost,TItem>` | `IsActive` / `Handles` / `Render`：平台自己画连线（overlay/画布）时接管 |
| `IViewMeasureHost.cs` | `IViewMeasureHost<THost,TView,TItem>` | `Measure`：arrange 之后的可选测量钩子 |
| `IViewHost.cs` | `IViewHost<THost,TView,TItem,TPriorityCore>` | 组合以上九项，给池一个对象可问 |
| `../Enums/ViewStage.cs` | `ViewStage` | `Schedule` / `Link` / `Create` / `Activate` / `Attach` / `Arrange` / `Layer` / `Measure` / `Deactivate` / `Detach` / `Destroy` |

### 通用流程（`Src/Core/VeloxDev.Core/WorkflowSystem/ViewHosting/`）

| 文件 | 类型 | 职责 |
|---|---|---|
| `ViewHostCore.cs` | `ViewHostCore<THost,TView,TItem,TPriorityCore>`（abstract） | 只组合九项、提供默认策略（3/批、不限回收、z=0、无测量、主池管连线、无诊断）；平台差异全在层里 |
| `ViewPool.cs` | `ViewPoolCore<...>`（abstract）+ `ViewPool<...>`（sealed） | 池、active、pending、批循环、Reset/Replace、unsupported 类型缓存、失败隔离 |
| `ViewSourcePipelineCore.cs` | `ViewSourcePipelineCore<THost,TView,TItem,TPriorityCore,TFactory,TContainer,TArranger,TScheduler,TDiagnostics>` | **泛型组合根**：闭合全部层与策略实例，创建 pool；适配器继承它给出自己的 `ViewSourcePipeline` |
| `AdaptiveViewPoolPolicy.cs` | `AdaptiveViewPoolPolicy`（实现 `IViewPoolPolicy` + `IViewBatchFeedback`） | 用平滑吞吐估算每批能物化多少；pending>1024/256 时目标预算 16/12ms，否则 8ms；批大小有 1.25× / 0.8× 迟滞；参数来自 `Options`，`FromOptions` 可替换整套策略 |
| `ViewPoolOptions.cs` | `ViewPoolOptions`（`[Serializable]`） | 描述/存储/替换自适应策略：初始批大小与预算、上下限、吞吐初值、pending 两档阈值与三档预算、平滑权重、增长/收缩迟滞、回收上限；`Normalize()` 夹取非法值 |

### Avalonia 适配（`Src/Adapters/VeloxDev.Avalonia/WorkflowSystem/ViewHosting/`）

| 文件 | 类型 | 对应层 |
|---|---|---|
| `ViewFactory.cs` | `ViewFactory` | 选择器 → 面板 `DataTemplates` → 父链 → `Application.DataTemplates`，按类型缓存 |
| `ViewContainer.cs` | `ViewContainer` | 视图留在 `Children`；`Activate` 设 `DataContext` + 可见，`Deactivate` 置空 + 隐藏 |
| `ViewArranger.cs` | `ViewArranger` | no-op：几何由模板绑定 |
| `ViewScheduler.cs` | `ViewScheduler` | `DispatcherPriority.Background` + `Dispatcher.UIThread.Post` |
| `ViewDiagnostics.cs` | `ViewDiagnostics` | `Debug.WriteLine` |
| `ViewPoolPolicy.cs` | `ViewPoolPolicy` | 固定策略的适配器示例（`BatchSize=1024`、每批 16ms，回收不限量）；`ViewPool` 默认用 Core 的 `AdaptiveViewPoolPolicy` |
| `ViewLayerPolicy.cs` | `ViewLayerPolicy` | `GetZOrder` 恒 0（模板绑 z） |
| `ViewLinkLayer.cs` | `ViewLinkLayer` | `IsActive=false`，连线仍由主池物化 |
| `ViewMeasureHost.cs` | `ViewMeasureHost` | no-op（布局 pass 自己量） |
| `ViewSourcePipeline.cs` | `ViewSourcePipeline` | 继承 `ViewSourcePipelineCore<Panel,Control,IWorkflowViewModel,DispatcherPriority,...>`，组合全部层 |

入口仍是 `WorkflowSystem/AttachedBehaviors/ViewPool.cs` 的附着属性 `ViewPool.ItemsSource` / `ViewPool.TemplateSelector`；它只创建/销毁 `ViewSourcePipeline`。

**其余五家**（路径均为 `Src/Adapters/VeloxDev.<GUI>/WorkflowSystem/ViewHosting/`，同样是 `ViewFactory` / `ViewContainer` / `ViewArranger` / `ViewScheduler` / `ViewDiagnostics` / `ViewLayerPolicy` / `ViewLinkLayer` / `ViewMeasureHost` / `ViewSourcePipeline`，默认 `AdaptiveViewPoolPolicy`）：

| 平台 | `THost` / `TView` | 优先级 | 几何 | 连线 | 备注 |
|---|---|---|---|---|---|
| WPF | `Panel` / `FrameworkElement` | `DispatcherPriority.Background` | 绑定，`ViewArranger` no-op | 逐条池化 | 模板选择器 + 资源回退 |
| WinUI | `Panel` / `FrameworkElement` | `DispatcherQueuePriority.Low` | 命令式 Canvas，`ViewLayoutTracker` 订阅属性变化 | 逐条池化 | `TryEnqueue` 拒绝时返回 false |
| MAUI | `Layout` / `View` | `NonPriority` + 16ms 一次性 timer | `AbsoluteLayout` + 节点 Anchor/Size 订阅 | `ViewLinkLayer.IsActive=true`，overlay 继续画 | 池不物化 link |
| WinForms | `Control` / `Control` | `NonPriority` + `Control.BeginInvoke` | Attachment 摆位 | `ViewLinkLayer.IsActive=true`，`WorkflowLinkLayer` 在画布 Paint 里一趟画完 | `TemplateSelector`（`DataTemplate` 回调）；Attach 时节点 `BringToFront`（不再有连线视图，`SendToBack` 路径空转） |
| Jalium | `Panel` / `FrameworkElement` | `DispatcherPriority.Background` | 绑定，`ViewArranger` no-op | 逐条池化；`WorkflowLinkBounds` 自盒化不动 | 模板选择器 + 资源回退 |

**适配器类都是 `public sealed`**，不是 `internal` —— 它们是本家对外的开放面；类名也不带平台前缀（`ViewFactory` / `ViewContainer` / `ViewSourcePipeline` …）。七家不能同时安装，同名类型正是这套设计的前提。

---

## 二、执行顺序

```
可见集 Add/Remove/Reset
        ↓
ViewPoolCore（Core）
  Add    → LinkLayer.Handles ? 跳过 pending : pending 入队 → ScheduleNextBatch
  Remove → Container.Deactivate → Container.Detach → 回池；超过 PoolPolicy 上限时 Container.Destroy
  Reset  → 全部回收 → 按当前集合重建 pending
  LinkLayer.IsActive → 每次变化 Render(host, 当前被 Handles 的条目)  ← 空集合也会调，便于清除旧绘制
        ↓ ScheduleNextBatch
Scheduler.Schedule(batch, BatchPriority)     ← 必须排队，不能 inline
        ↓ 批次回调
  Factory.WantsItem → Factory.CreateView / 池里取
  Container.Activate(view, item)             ← DataContext / 可见
  Container.Attach(host, view)               ← Children.Add / DOM 注册
  Container.SetZOrder(view, LayerPolicy.GetZOrder(item))
  Arranger.Arrange(view, item)               ← Canvas / AbsoluteLayout / CSS / no-op
  MeasureHost.Measure(host, view, item)      ← 可选，平台自己决定
  active[item] = view
```

失败时 `Diagnostics.Failed(item, stage, exception)` 记录当前 stage，同批其余项继续；出错的视图会尽量 `Deactivate`/`Detach` 后回池。

---

## 三、不变量

1. **`IViewScheduler.Schedule` 必须异步**。实现只能 post（Dispatcher / DispatcherQueue / MainThread / 定时器 / renderer），不能同步调用 `batch`；否则批尾重排会递归。**不要直接拿 `IThreadDispatcher.Post` 当 `Schedule`**：后者在已在目标线程时会 inline。
2. **`Attach` 必须幂等**。回收的视图可能仍在视觉树里，复用时池仍会调 `Attach`；实现自己 `Children.Contains` 去重。
3. **`Deactivate` 是回收的解绑口**；`Detach` 只负责隐藏/摘除，不负责清 `DataContext`。
4. **`Factory.CreateView` 返回 `null` = 该类型不支持，按运行时类型永久缓存**（直到 system 被销毁）。换模板必须重建 system —— `ViewPool` 的两个附着属性任一变化都会 `Cleanup` + 重建。
5. **`Factory.WantsItem=false` 不缓存**；每次入队都会再问。MAUI 的连线 overlay 应优先用 `LinkLayer.Handles`，它比 `WantsItem` 多一个“由谁画”的明确归属。
6. **集合通知与池的公开成员必须跑在宿主线程**。Core 不做编组；`Scheduler` 是唯一线程边界。
7. **`TItem` 必须是引用类型**。active 表按引用身份比较（`ViewReferenceComparer<object>`），不按 `Equals`。
8. **`ViewSourcePipeline` 是组合根，不是第二个引擎**。它只把全部层与策略实例交给 `ViewHostCore`，再创建 `ViewPool`；不要在这里加平台分支。
9. **被 `LinkLayer.Handles` 接管的条目永不进主池**。只有 `LinkLayer.IsActive=true` 时才会在每次可见集变化调用 `Render`（包括空集合）；实现不得修改可见集。`IsActive=false` 的平台完全不进这条路径，避免无谓的整集枚举。
10. **`PoolPolicy.BatchSize` 小于 1 当 1；`FrameBudgetMilliseconds` 小于等于 0 表示不限时；`MaxRecycledViewsPerType` 小于等于 0 表示不限**。一批在“数量到顶”或“16ms 到期”时停下，剩余项排下一批。超过回收上限的视图走 `Container.Destroy` 真正移除（Avalonia 是 `Children.Remove`），不能只丢引用 —— 隐藏的子元素仍被宿主根住。
11. **`LayerPolicy` 与 `MeasureHost` 都是可选钩子**。绑定驱动的平台让 `Container.SetZOrder` 与 `Measure` 空实现即可；需要时再在适配器里补。

---

## 四、Avalonia 平台面的现状

- 默认策略是 Core 的 `AdaptiveViewPoolPolicy`（初始 64/8ms，按平滑吞吐与 pending 大小收敛到 8/12/16ms 预算）；`ViewPoolPolicy` 是固定参数的对照实现。两者都回收不限量，`BatchPriority = DispatcherPriority.Background`。
- 几何仍是模板绑定：`ViewArranger.Arrange` 为空。
- 容器回收不摘树：`Deactivate` 清 `DataContext` 并 `IsVisible=false`；`Detach` 为空。
- 引擎处理 `Replace`；模板缺失时 `ViewFactory.CreateView` 返回 `null`，Core 按 ViewModel 类型缓存该类型。换模板必须重建 `ViewSourcePipeline` —— `ViewPool` 的两个附着属性任一变化都会重建。
- `ViewPool` 在 `DetachedFromVisualTree` 时 `Dispose` system；换源/换模板时先摘旧处理器再重建。

---

## 五、接一家新平台

1. 在 `Interfaces/WorkflowSystem/ViewHosting/` 的九个层/策略里，按平台能力实现窄层（不是重新实现池）：五个必选层 + 四个策略/服务层；需要闭环调参时让策略额外实现可选的 `IViewBatchFeedback`。
2. 在 `Src/Adapters/VeloxDev.<平台>/WorkflowSystem/ViewHosting/` 建对应内部类；模板/几何/线程/优先级/z 序/连线绘制/测量只在各自层里。
3. 写一个 `ViewSourcePipeline : ViewSourcePipelineCore<THost,TView,TItem,TPriorityCore,ViewFactory,ViewContainer,ViewArranger,ViewScheduler,ViewDiagnostics>` 闭合系统；池策略、层级策略、连线层、测量层从构造参数传入。适配器类名不带平台前缀 —— 七家不能同时安装，同名才有价值。
4. 附着属性（或静态挂点）只负责在源/模板变化时重建、卸载时 `Dispose`。
5. 用无头测试钉：可见项变成子视图；移除后视图仍保留但隐藏/可复用；无选择器走模板 fallback；换模板不重复挂 Detach 处理器。

MAUI 的 `LinkLayer.Handles`（连线 overlay）、WinUI 的 `DispatcherQueuePriority.Low` + 命令式 `Arrange`、WinForms 的 `Control.BeginInvoke` 异步 `Schedule` + `Control` 容器 + `BringToFront/SendToBack`，都应该落在这些层里，而不是回到 Core 加 `if`。

---

## 六、有头性能基准

- 共享核心：`Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.Headed`（`HeadedOptions` / `SyntheticWorkflow` / `HeadedReport` / `HeadedBenchmarkCore`），宿主工程只实现 `IHeadedBenchmarkHost`。
- 同进程控制台宿主：`Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks` 的 `headed` 模式（**不是 BenchmarkDotNet**），`--adapter avalonia|wpf|winforms|jalium`，入口 `Headed/HeadedBenchmarkRunner.cs`。
- 独立宿主：`Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.WinUI`（unpackaged WinUI 3，`--adapter winui`）与 `.MAUI`（Windows 目标，`--adapter maui`）。WinUI 宿主运行时用 Windows App SDK **1.8.251003001**：适配器编译在 1.6 上，但 1.6 运行时无法启动 .NET 10 进程（`0xC000027B`），不要把宿主降回 1.6。
- Razor 例外：`Src/Test/Benchmarks/VeloxDev.Adapters.Benchmarks.Razor` 是 **headless renderer benchmark**（`HtmlRenderer` 驱动真实 `ViewPool`），不是 `IHeadedBenchmarkHost`，不能与有头窗口数字直接横比。
- 每适配器接缝：`IHeadedBenchmarkHost`（`ResetScenarioAsync` / `MeasureInitialAsync` / `MeasureScrollAsync` / `MeasureFullAsync`）。
- 指标：墙钟（包含 Pool 批次排空 + 一次真实布局/渲染回合，不是 GPU 帧时）、`GC.GetTotalAllocatedBytes`、Gen0/1/2、池的 `Active/Pending/Recycled/Created/Reused/Destroyed`、`VisibleItems`；`ViewPoolCore` 为此公开了 `CreatedViewCount` / `ReusedViewCount` / `DestroyedViewCount`。
- 结果写各自工程下的 `BenchmarkDotNet.Artifacts/view-pipeline/*.md` 与 `*.json`；一轮先 warmup，再取中位数/P95，不是均值。运行命令与纪律见 `Src/Test/README.md`。
- 一次只跑一家，别并行驱动多个 UI 进程；WinUI / MAUI 独立宿主直接运行产物，不经过 `dotnet run`。
- Razor 的 2000 节点 / 3000 连线 / 20 步场景（`HtmlRenderer`，`InProcessEmitToolchain`，依赖图 `-p:Optimize=true`）：Initial 约 0.36 ms / 0.04 MB；每个视口步约 0.88 ms / 0.15 MB（合并集合通知前的旧值是 18.5 ms / 6.85 MB）。`ViewPool` 的 `OnCollectionChanged` 现在只排队一次 `Task.Yield` 后的快照 + `StateHasChanged`，不再为虚拟化发出的每个 Add/Remove 重渲染。它量的是 renderer 构造渲染树 + 静态 HTML，不含 Blazor 的 DOM patch/JS transport，也不是窗口布局；节点模板比 demo 轻，所以只能同 Razor 自身前后比。BDN optimizer validator 保持开启，依赖未优化时拒绝运行。`--probe` 是“renderer 确实重渲染了真实 `ViewPool`”的证据。

**Avalonia 基线**（16 核 Windows、.NET 10.0.12、2000 节点 / 3000 条本地连线、视口 1200×800、20 步、5 轮、自适应策略；节点=Border+Text，连线=1×1 占位，因此是**管线下限**，不是完整 demo 视图的性能）：Initial 中位 13.4 ms / P95 15.1 ms；Scroll step 11.7 / 13.6 ms；Full visibility 230 / 426 ms（约 0.046 ms/view，分配 61.6 MB）。正常视口约 180–280 个可见项；全量是一次性物化 5001 个视图的最坏情况。固定 1024/16 的对照是 Full 248 / 443 ms。

**六家完整矩阵**（同一台 16 核 Windows、.NET 10.0.12、2000/3000、1200×800、20 步、5 轮、自适应；中位 ms，Full 分配 MB）：

| 适配器 | Initial | Scroll step | Full | Full 分配 |
|---|---:|---:|---:|---:|
| Avalonia | 12.28 | 11.98 | 232.52 | 61.8 |
| WPF | 9.38 | 4.22 | 262.26 | 43.9 |
| WinForms | 60.0 | 23.0 | 4137.0 | 438.8 |
| Jalium | 3.38 | 1.00 | 302.70 | 66.3 |
| WinUI | 29.40 | 6.00 | 392.53 | 10.0 |
| MAUI | 286.46 | 37.54 | 5070.75 | 357.0 |

WinForms 的 Full 现在是 **2000 个节点窗口** 的成本：`ViewLinkLayer` 把 3000 条连线挡在主池外，`WorkflowLinkLayer` 在画布 `Paint` 里一趟画完。旧实现（每线一个窗口）Full 是 13.4–15.3 s；单层后约 4.14 s，Initial 60 ms、Scroll 23 ms。剩余成本是节点 HWND 本身（裸 WinForms 探针：2000 个含子控件的视图 `Controls.Add` ≈ 2.3 s）。MAUI 的 Active 也只到 2000/5001，同样是 overlay 把连线挡在主池外。两者的 Full 不能与另外四家按视图数直接横比；单层连线的分配更高（每次几何变脏重发采样曲线），正确性优先。

当前管线的关键成本控制：pending 用 `Queue + HashSet`（增删查 O(1)，移除后留下 tombstone 由出队时跳过）；Avalonia `ViewContainer` 用引用比较 `HashSet<Control>` 做挂载去重，不再 `Children.Contains`；`LinkLayer.IsActive=false` 时完全跳过整集枚举；每批受 `BatchSize=1024` 与 16ms 双重限制。

## 七、Razor/Blazor：renderer-native 例外

Razor 的 `ViewPool` 组件**不接** `ViewSourcePipelineCore`，这是刻意的，不是漏做：

- `IViewFactory` 的 `TView` 在 Blazor 里没有可创建的实例；组件/元素只由 renderer 在执行 `BuildRenderTree` 时产生，`RenderFragment` 不是有树成员与绑定态的视图。
- `IViewContainer` 的 `Attach/Detach/Activate/Deactivate` 假定适配器拥有视觉树与绑定态，而 Blazor renderer 拥有它们；Core 的“同一视图实例换绑到另一个 item”在 Blazor 里正是 `@key` 要避免的位置复用。
- `IViewScheduler` 要求“物化视图”的批次；Blazor 的 `StateHasChanged` 只重跑 `BuildRenderTree`，排一个 shadow list 并不产生视图。

当前 `ViewPool.razor(.cs)` 保持四个参数（`ItemsSource` / `ItemTemplate` / `EmptyContent` / `KeySelector`）与 `@key` diff，renderer 自己做实例化、diff 与按 key 保留状态。不要为了“结构一致”给它套一个 no-op `ViewSourcePipeline` —— 那会编译通过但不驱动渲染。

## 八、测试锚点

- `Src/Test/Unit/Core/VeloxDev.Core.Test/WorkflowSystem/ViewPoolTests.cs`：用 `FakeScene` 组合全部层，覆盖分批、按类型复用、Reset、缺模板缓存、单项失败隔离、Dispose、调度被拒与重试；另覆盖 `LinkLayer.IsActive` / `Handles` 跳过主池、`LayerPolicy`/`MeasureHost` 被调用、回收上限生效。
- `Src/Test/Headless/VeloxDev.Avalonia.Headless.Tests/Tests/ViewPoolHeadlessTests.cs`：`ViewPool` 把可见项变成子视图、移除后保留并隐藏；无选择器走面板 `DataTemplates`；换 TemplateSelector 不重复挂 Detach 处理器。
