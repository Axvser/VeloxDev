# TransitionSystem — WinForms

> **读法**：契约（八个类各自要提供什么）、注册位置、`NonPriority` 的选型通则都在
> `memory/modules/TransitionSystem/extension.md`，本文不重复；
> 人面向的「怎么写一个适配器」在 `skills/veloxdev-create-animation/references/adapter.md`，本文只指路不抄。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。
> 下文 `Xxx.cs:NN` 一律相对 `Src/Adapters/VeloxDev.WinForms/PlatformAdapters/`，Core 侧写全路径。

---

## 一、这家要写什么，为什么是这些

八个类一个不少（表见 `extension.md` §三·C），但**这家有内容的类只有三个**：

| 类 | 这家的内容 |
|---|---|
| `Interpolator` | 一行 `RegisterInterpolator` + `CreateScheduler`（`Interpolator.cs:9`、`:12-15`） |
| `UIThreadInspector` | **五个覆写全都要**：`IsAlive`/`ThreadFor`/`IsCurrentFor`/`IsCurrentThread`/`PostCore`（`UIThreadInspector.cs:51-84`）—— 这家是七家里唯一覆写 `IsCurrentFor` 的 |
| `TransitionInterpreter` | 一个 `CreateFramePacer`（`TransitionInterpreter.cs:20-21`）+ 私有的 `PostedFramePacer`（`:23-94`，这家唯一一个不能照抄别家的 pacer，见 §2.2） |
| `State` / `TransitionScheduler` / `TransitionEffect` / `TransitionEffects` / 非泛型 `Transition` | 空壳（`State.cs:3`、`TransitionScheduler.cs:3-9`、`TransitionEffect.cs:3`、`TransitionEffects.cs:3-17` 只给三个样本、`Transition.cs:5-8` 连体都没有）——**不要以为漏写了什么** |

`TPriorityCore` 填 `NonPriority`（`Transition.cs:17`）。这一个决定牵动两处：`Transition<T>` 的第七个型参，与 `CreateScheduler` 里的 cast（`Interpolator.cs:13`）。第二处写错 ⇒ 主题切换静默变瞬切（`extension.md` §二·3）。

**`Transition<T>` 有 1 个泛型 `Property` + 16 个手写重载**（泛型 `Transition.cs:30`，手写 `:37-135`）。与 WPF 不同 —— 那家手写重载里有泛型表达不了的语义 —— 这 16 个在 WinForms 上**功能上是冗余的**：参数表被泛型版完全覆盖，方法体逐字相同（对照 `:30-35` 与 `:37-42`）。逐类型手写**必须**的只有 Razor（它没有泛型版，`extension.md` §三·A·3）。⇒ 改这些重载不会改变任何行为，**除了 `decimal` 那一个**（见 §4.1）。

### 为什么这家只有一个采样器（与 Razor 并列最少）

**一句话：采样器条数不度量实现完整度，它度量「这家框架自带的、Core 还没覆盖的可动画值类型有几个」。而 WinForms 的那一族正好被 Core 全包了。**

- Core 的 `InterpolatorCore` 静态构造（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:12-28`）注册的是：4 个数值类型（`double`/`float`/`int`/`long`）+ `System.Drawing` 的 `Point`/`PointF`/`Size`/`SizeF`/`Color`/`Rectangle`/`RectangleF` + `#if !NETSTANDARD2_0` 的 `Vector2/3/4`/`Quaternion`。**`System.Drawing.Primitives` 是无 GUI 依赖的程序集，所以 Core 引用得起它** —— 这是这家的采样器表看起来「空」的根本原因。
- WinForms 的属性面**就是 `System.Drawing` 那一族**：`Control.Location`=Point、`Control.Size`=Size、`BackColor`/`ForeColor`=Color、`Bounds`/`ClientRectangle`=Rectangle/`RectangleF`、`Font` 是引用类型。
- 排掉这些**只剩 `System.Windows.Forms.Padding`** —— 它是 `System.Drawing` 之外唯一一个 WinForms 专有可动画值类型，也就是 WPF/Avalonia/WinUI/MAUI/Jalium 五家 `Thickness` 的对等物。所以注册表里只有一行（`Interpolator.cs:9`）。
- **这不是「还没补」**：上一条的推理已经把「可动画的专有类型只剩 `Padding`」用属性面推完了；树内可核的现状是 `PlatformAdapters/Samplers/PaddingSampler.cs` 一个文件、`Interpolator.cs:9` 一条注册。**「以前是不是更少」只存在于提交历史、代码里复核不到**，所以别拿数少去推断这里缺了实现。
- 这家**没有** `Brush`/`Transform`/`CornerRadius`/`GridLength`/`DropShadow`/`Projection`/`Point3D` 的等价物：画刷按需 `new SolidBrush(color)` 现造（`ThemeValueConverters.cs:433`），坐标变换是 GDI+ 的 `Graphics.Transform` 而不是视图模型上的属性 —— WorkflowSystem 那半边也印证：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowCanvasTransformBehavior.cs` 存的是一个 `Offset` 结构，不是变换对象。
- 校准别家的数（免得把「少」当缺陷；**数的是采样器类数 / `RegisterInterpolator` 条数**，两者仅在 Jalium 上不同 —— 它的 `BrushSampler` 同时注册 `Brush` 与 `SolidColorBrush`）：Avalonia 14/14、WPF 12/12、MAUI 12/12、WinUI 10/10、Jalium 9/10、**WinForms 1/1、Razor 1/1**。
- **副作用（容易手贱的一点）**：`Point`/`Size`/`Color` 在 Core 里已经注册过，**不要**在这家再注册一遍。`RegisterInterpolator` 是进程级 last-writer-wins 的原子 `AddOrUpdate`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:89-95`），而 `Interpolator` 与 `InterpolatorCore` 两个静态构造谁先跑不确定；重注册会无声地把 Core 的实现换掉。WPF/Avalonia 那种「同名不同型的 `Point`」在这家**不存在**（它俩用的就是同一个 `System.Drawing.Point`），所以那条理由不适用。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 没有 dispatcher、也没有可问的存活源 ⇒ 只能自己记

`_isAppAlive` 是静态字段，只在捕获成功那一刻挂上 `Application.ApplicationExit` 去关它（`UIThreadInspector.cs:40`，初值 `:10`），`IsAlive => _isAppAlive`（`:51`）。对照有 dispatcher 的家：WPF/Jalium 直接问 `dispatcher.HasShutdownStarted`、WinUI 问 `queue.TryEnqueue` 的返回值（见 `wpf.md` §2.1）。**没有东西可问，所以必须自己记** —— 这不是风格选择。同一形状的还有 MAUI 与 Razor。

### 2.2 帧源不能是 WM_TIMER ⇒ 这家的 pacer 是「线程池定时器 + 投递到控件」

**`WM_TIMER` 是这个平台上唯一不能用来当帧时钟的机制。** Windows 只在消息队列空无一物时才合成它，而那一刻只有一个「空闲时刻」——哪个到期定时器先被问到就归谁（这家进程里每个 `Transition` 都有一条自己的定时器）。两处实测（workflow demo，`Examples/Workflow/WinForms/Demo/`，16 ms 帧间隔）：

- **拖画布期间 0 帧**：拖拽的每个 `WM_MOUSEMOVE` 都被回以一次同步重画（`WorkflowSurfaceBehavior.Refresh` 的 `host.Capture → Update()`、`WorkflowNodeDragBehavior.cs:222-229`），队列里永远躺着下一条鼠标消息 ⇒ 3 秒拖拽里 **0 次 WM_TIMER、流光 0 次写入**，而同一期间的 BeginInvoke 投递保持 11–15 次/200 ms（≈64/s，与空闲时相同）—— 队列不忙，只有 WM_TIMER 被饿死。
- **空闲时同一条规则把帧率压到 1/4**：该进程 ~16 条定时器（画布 1 + 每个 `SlotView` 波纹 1）分 ~57 次 WM_TIMER/200 ms ⇒ 画布流光的写入从 60/s 掉到 ~15/s。

所以 `CreateFramePacer` 是 `affinity.IsCurrent(target) ? new PostedFramePacer(target as Control) : null`（`TransitionInterpreter.cs:20-21`）：续体由 `System.Threading.Timer` 到期后经 `Control.BeginInvoke` 投回目标窗口（`:63-81`）。投递出去的消息按 FIFO 送达，不等队列空；续体仍在控件线程上恢复 ⇒ 属性写入照旧直写、`Update`/`LateUpdate` 仍在该线程（pacer 存在的理由不变）。

- 对照：WPF / Avalonia / WinUI / MAUI / Jalium 五家**一律**从 `affinity.ThreadFor(target)` 派生（WPF 是 `affinity.ThreadFor(target).TryGet<Dispatcher>(out var d) ? new DispatcherFramePacer(d) : null`，`Src/Adapters/VeloxDev.WPF/PlatformAdapters/TransitionInterpreter.cs:8-11`；其余四家同形，只换 `TryGet` 的类型）——它们的 `DispatcherTimer` 是**按优先级排队的队列项**，不受这条规则影响；Razor 干脆不覆写，注释写着「Blazor 没有在渲染器自己线程上触发的定时器」（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/TransitionInterpreter.cs:4`）。
- **仍然「有条件地」取**（七家里唯一，见 §三·4），但判据的含义变了：不再是因为「Forms 定时器只能在自己线程上 tick」，而是「续体要投到哪个控件的窗口上，只有已经在它线程上时才认这个前提」。
- `IsCurrent(target)` 走基类的 `IsCurrentFor(target, ThreadFor(target))`（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:15`），而这家覆写了 `IsCurrentFor`（见 §三·1）。
- **后果（要记住）**：从非 UI 线程**首次**发起一段动画 ⇒ 没有 pacer ⇒ 回落到 `ArmNextFrame` 的默认实现（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:94`），而 `FrameWait` **不还原 `SynchronizationContext`**（`extension.md` §F 已写）⇒ `Update`/`LateUpdate` 会漂到线程池线程上跑。所以「确保第一次触碰发生在 UI 线程」在这家不是优化，是前提。
- **代价（换来的东西不是白给的）**：每帧多一次 `BeginInvoke`（一次小对象分配 + 一条投递消息）。空闲实测：16 条动画 60 fps 下 ~1300 次投递/秒，同时队列里还有 ~770 次 WM_PAINT/200 ms，画布每次重画 ~4 ms —— 都在余量内。CPU 全忙时帧会排队变慢而不是停止：`SamplerSet.Apply` 在执行时读的是最新时刻，所以积压的帧落地时画的是当前位置。

### 2.3 目标无句柄 / 非控件时的退路

`PostedFramePacer.OnDue` 在目标不是 `Control`、或 `IsHandleCreated == false` 时就地 `Fire()`（`TransitionInterpreter.cs:63-81`）——**即退回到默认的线程池等待语义**：续体在工作线程上恢复，`Update`/`LateUpdate` 也就漂到那里，属性写入各自编组（`Post` → `PostCore` → `BeginInvoke`）。这不是缺陷而是刻意的：pacer 只在「有窗口可投」时才值得存在，其余情况用它只会多一层等待。句柄在检查与投递之间消失（`BeginInvoke` 抛 `InvalidOperationException`，已释放控件抛其派生类 `ObjectDisposedException`）走同一条退路。

### 2.4 平台定时器是 `IDisposable`，而基类不管释放

`Dispose` 必须 `_disposed = true; timer = _timer; _timer = null; base.Dispose(); timer?.Dispose();`（`TransitionInterpreter.cs:84-93`）：基类先停表并放行挂着的续体，之后这块表才轮到被释放。WPF 那家的 `Dispose` **只停表、不释放**（见 `wpf.md` §2.2）——**别把两家互相照抄**：WPF 的 `DispatcherTimer` 生命周期归 dispatcher 管，这家的 `System.Threading.Timer` 必须自己 dispose，否则它自己的线程池回调（以及那条对已死窗口的投递尝试）会一直活下去。

`Arm` 在已释放时必须 `Fire()` 放行而不是直接 return（`:38-45`）：基类 `Dispose` 就是靠「唤醒挂着的续体」让采样循环收尾的，丢掉它等于把循环永久停在一次等不到的唤醒上（宿主侧表现为「不报错、也再没有帧」）。

### 2.5 捕获是惰性的，而且「按类型名认亲」

- 捕获发生在首次从 UI 线程碰到这个类时（`EnsureCaptured`，`UIThreadInspector.cs:29-41`），判据是 `SynchronizationContext.Current?.GetType().Name != "WindowsFormsSynchronizationContext"`（`:34`）—— **字符串比较，不是类型比较**。好处是不用引用那个类型；代价是任何自定义/包装过的同步上下文（名字对不上）都会被当成「不在 UI 线程」而**静默不捕获**（不抛、不告警）。
- 唯一的显式入口是 `CaptureUIThread()`（`:16-23`），且必须在 `Application.Run` 之前调。
- 存活探针 `_isAppAlive` 的初值是 `true`（`:10`），而订阅 `ApplicationExit` 只在捕获成功那一支里（`:40`）⇒ **没捕获过它就永远是 `true`**，`IsAlive` 不传达任何东西。危害有限（同时也没有 UI 上下文可投递），但别把 `IsAlive` 当存活保证用。

---

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **`IsCurrentFor` 被覆写（七家里唯一）**：先问目标 `Control.InvokeRequired`，拿不到控件才回落到基类。WinForms 没有任何 API 能「查一个 `Control` 属于哪个线程」，但 `Control` 自己答得出「调用方在不在我的线程上」——**同一个问题的对偶问法**。这不只是查询优化：`ThreadDispatcherBase.Post`（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:49-50`）在 `IsCurrentFor` 为真时直接 `RunInline`，所以覆写它等于让 UI 线程上的写完全绕开消息泵。 | `UIThreadInspector.cs:59-66` |
| 2 | **`PostCore` 优先走目标 `Control.BeginInvoke`（七家里唯一）**：别家一律从 `thread` 里 `TryGet<TDispatcher/TSyncContext>`。这家先看目标本身是不是一个已建句柄的 `Control`（`ControlDispatcher`，`:48-49`），是就直接投。所以**即使从未捕获过 UI 上下文、即使首次调用来自后台线程**，只要 `target` 是 `Control`，写也落对线程（`:43-47` 的注释明写这一点）。基类专门声明这是被允许的自由（`ThreadDispatcherBase.cs:33-36`：需要 target 而不是 thread 的宿主可以忽略 `thread`）。 | `UIThreadInspector.cs:72-84` |
| 3 | **`ThreadFor` 返回「已捕获的上下文」，不为调用方造一个**：`ThreadRef.From(_uiSyncContext)`（`:53-57`）。没捕获过就是 `From(null)`。Razor 那家给的是「当下看到的」`SynchronizationContext.Current ?? 捕获值`——**两家在「调用方不在 UI 线程时给什么」上是相反的选择**。基类契约禁止「造一个」（`extension.md` §二·6），这家与 Razor 的分歧在于「拿什么当兜底」。 | `UIThreadInspector.cs:53-57` |
| 4 | **pacer 在七家里唯一「有条件地」取得**（见 §2.2）：别家要么给、要么不给，这家先问线程再决定；而且它是七家里唯一**不拿平台定时器当帧源**的 pacer（线程池定时器 + `Control.BeginInvoke`，因为 WM_TIMER 会被输入饿死）。 | `TransitionInterpreter.cs:20-21`、`:63-81` |
| 5 | **`TransitionEffect` 连 `Priority` 默认值都不用给**：`TransitionEffect : TransitionEffectCore` 是纯空壳（`TransitionEffect.cs:3`），因为 `NonPriority` 是空结构体，`default!` 就是全部答案（`ThreadDispatcherBase.cs:39-44`）。WPF 那家必须在这里给一个 `DispatcherPriority`。 | `TransitionEffect.cs` |

（**不改的**：`State : StateCore` 空壳、`TransitionScheduler` 非泛型空壳（`TransitionScheduler.cs:3-9`）、`ThemeValueConverters.cs` —— 六家都有这一份，Jalium 才没有，所以它不构成背离。）

---

## 四、坑（带依据）

### 4.1 `decimal` 重载能声明、能编译、动画静默不动

`Transition<T>.Property(Expression<Func<T, decimal>>, decimal, ...)` 存在（`Transition.cs:62-67`），但**全仓库没有任何地方为 `decimal` 注册采样器**：Core 的注册表里没有（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:12-27`），本家的注册表里只有 `Padding`（`Interpolator.cs:9`）。全仓库 `typeof(decimal)` 的命中全部在 AI 工具面（`Src/Core/VeloxDev.Core.Extension/Agent/...`），与动画无关。

运行后果**不是抛异常**：`InterpolatorCore.Prepare` 走到最后一步找不到采样器，只发一次 `Warn("Unsampled")` 然后 `continue`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:175-179`）。于是一段「只动 `decimal` 属性」的动画会不报错、不画、直接跑完。**要动画 `decimal` 必须自己写采样器并注册。**

这条同时是「16 个手写重载是照抄来的、不是从注册表推出来的」的证据：如果这 16 个重载是按采样器表写的，`decimal` 根本不会出现在里面。

### 4.2 `Brush` 类型的主题属性会「全程保持旧值、最后跳变」

`ThemeValueConverters.cs` 的 `ObjectConverter` 会为目标类型是 `Brush` 的属性造 `SolidBrush`（`:427-433`），但本家没有 `Brush` 采样器。于是 `Brush` 类型的主题属性走的是 `ThemeManager` 记录在案的降级路径：整场动画不写它，等整场结束后统一写终值（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs:296-297` 的注释 + `ApplyHeldValues` `:390-407`）。**这不是 bug**，但要做「刷子渐变」类的主题过渡就得自己补采样器。

### 4.3 `SetPlatformInterpolator` 从没为 WinForms 调用过 ⇒ `CreateScheduler` 在仓库内没有执行路径

全仓库只有 4 处调用，全在 Avalonia / WPF 的 theme demo（`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`、`Examples/Theme/Avalonia Trimmed/Demo/Views/MainWindow.axaml.cs:46`、`Examples/Theme/WPF/Demo/App.xaml.cs:16`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:47`）——`Examples/Theme/` 下**没有** WinForms 目录（只有 Avalonia、Avalonia Trimmed、WPF、WPF Trimmed）。而 `CreateScheduler` 的唯一调用者是 `ThemeManager.cs:219`。

⇒ 本家 `Interpolator.CreateScheduler`（`Interpolator.cs:12-15`）是**给下游消费者的接口，不是现成能力**：没有主题动画的 demo 覆盖它。要用主题动画，得自己在 `Application.Run` 之前调一次 `ThemeManager.SetPlatformInterpolator(new Interpolator())`。

### 4.4 `PaddingSampler` 用截断，Core 的同族用舍入

`(int)(t * delta)`（`Samplers/PaddingSampler.cs:13-17`）是**截断**；Core 的 `SizeSampler`/`PointSampler` 用 `(int)Math.Round(...)`（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/SizeSampler.cs:24-25`）。后果有限但确实有：每帧像素落点最多差 1px，且**不累积**（每帧都从 `start` 重算）。想让 `Padding` 与同屏的 `Size`/`Point` 落点一致，就改成 `Math.Round`。

另外两点（都是与 Core 同族的有意对照，不是缺陷）：
- `PaddingSampler` 忽略 `options`（签名收了 `object? options` 但体里没用，`:8`）⇒ `interpolationOptions` 对 `Padding` 无效。
- `SizeSampler` 特意让宽高共享一个 `BoundedProgress` 并在 0 处停住（`SizeSampler.cs:18-22` 的注释：负尺寸不可表示）；`Padding` 的四条边各算各的，因为四个 int 接受负值 —— **这就是验收表把这条判成 `SamplerRule.Extrapolate` 而不是饱和的原因**（`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs:14-28`，写明了「收缩的一侧在 t > 1 处会算出负的 Padding，而 Padding 只是四个 int，接受负值」）。

### 4.5 静态构造的触发时机：这家的注册只有一个，漏掉等于「全部不动画」

`Interpolator` 的静态构造（`Interpolator.cs:7-10`）只在 `Interpolator` 这个类型被第一次触碰时跑。`TransitionCore<...>` 有 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Transition.cs:290`），在 WinForms 上就是 `new Interpolator()` ⇒ 只要 `new Transition<T>()` 过一次，注册就发生了，**不需要任何显式注册调用**。反过来：绕开 `Transition<T>` 直接调 `InterpolatorCore.Prepare` 时 `Padding` 没注册。别家漏一个注册只是少一个类型，这家漏掉就是全部（表里只有一项）。

---

## 五、这份文件没写的东西

- **契约与注册位置**（八个类各自必须提供什么、`NonPriority` 怎么选）：`memory/modules/TransitionSystem/extension.md` §三·C。
- **人面向的「怎么写」**：`skills/veloxdev-create-animation/references/adapter.md`。
- **验收套件与联动清单**：`extension.md` §四·4.1/4.3。这里只记这家的一条事实：WinForms 的验收表**只有一条 entry**（`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs:30-40`），而 `Examples/Transition/AUTO TEST/Samplers/SamplerCoverageTests.cs:23-33` 的 `ExpectedAdapterAssemblies` 里那个 `"VeloxDev.WinForms"` 字符串是「这家还在名单上」的唯一依据 —— 改采样器时覆盖率测试会不会真的验到这家，全靠它。
