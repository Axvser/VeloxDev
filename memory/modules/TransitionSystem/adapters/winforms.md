# TransitionSystem — WinForms

> **读法**：契约（每个类各自要提供什么）、注册位置、`NonPriority` 的选型通则都在
> `memory/modules/TransitionSystem/extension.md`；
> 人面向的「怎么写一个适配器」在 `skills/veloxdev-create-animation/references/adapter.md`。
> 下文 `Xxx.cs` 一律相对 `Src/Adapters/VeloxDev.WinForms/TransitionSystem/`，Core 侧写全路径。

---

## 一、这家要写什么，为什么是这些

契约要求的类一个不少（表见 `extension.md` §三·C），但**这家只有少数几个类有内容**：

| 类 | 这家的内容 |
|---|---|
| `Interpolator` | 一行 `RegisterInterpolator` + `CreateScheduler`（`Interpolator.cs` 的静态构造、`CreateScheduler`） |
| `UIThreadInspector` | **覆写全都要**：`IsAlive`/`ThreadFor`/`IsCurrentFor`/`IsCurrentThread`/`PostCore`（`UIThreadInspector.cs` 的覆写）—— 这家是七家里唯一覆写 `IsCurrentFor` 的 |
| `TransitionInterpreter` | 一个 `CreateFramePacer`（`TransitionInterpreter.cs` 的 `CreateFramePacer`）+ 私有的 `PostedFramePacer`（这家唯一一个不能照抄别家的 pacer，见 §2.2） |
| `State` / `TransitionScheduler` / `TransitionEffect` / `TransitionEffects` / 非泛型 `Transition` | 空壳（`State`、`TransitionScheduler`、`TransitionEffect` 是空壳、`TransitionEffects` 只给少量样本、非泛型 `Transition` 连体都没有） |

`TPriorityCore` 填 `NonPriority`（`Transition.cs` 的 `NonPriority` 型参）。这一个决定牵动两处：`Transition<T>` 的第七个型参，与 `CreateScheduler` 里的 cast（`Interpolator.cs` 的 `CreateScheduler` 里那个 cast）。第二处写错 ⇒ 主题切换静默变瞬切（`extension.md` §二·3）。

**`Transition<T>` 有一个泛型 `Property` + 一批手写重载**（泛型 `Transition.cs` 的 `Property<TValue>`，其余是手写重载）。与 WPF 不同 —— 那家手写重载里有泛型表达不了的语义 —— 这批手写重载在 WinForms 上**功能上是冗余的**：参数表被泛型版完全覆盖，方法体逐字相同（对照泛型版与第一个手写重载）。逐类型手写**必须**的只有 Razor（它没有泛型版，`extension.md` §三·A·3）。⇒ 改这些重载不会改变任何行为，**除了 `decimal` 那一个**（见 §4.1）。

### 这家有哪些采样器

**采样器条数度量「这家框架自带的、Core 还没覆盖的可动画值类型有几个」，不度量实现完整度。**

- WinForms 的属性面**就是 `System.Drawing` 那一族**：`Control.Location`=Point、`Control.Size`=Size、`BackColor`/`ForeColor`=Color、`Bounds`/`ClientRectangle`=Rectangle/`RectangleF`、`Font` 是引用类型。Core 的静态构造（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的静态构造）已注册数值类型 + `System.Drawing` 的几何/颜色类型 + `#if !NETSTANDARD2_0` 的 `Vector2/3/4`/`Quaternion`。
- 排掉这些后，WinForms 专有（或 XAML 层新引入）的可动画类型是 `System.Windows.Forms.Padding`、XAML `Thickness` 和 XAML `Brush`。注册表在 `Interpolator.cs` 静态构造里各有一行；实现分别在 `Samplers/PaddingSampler.cs`、`ThicknessSampler.cs`、`BrushSampler.cs`。
- `BrushSampler` 把两个端点压到 `ApproximateColor` 后逐通道插值，写回 `SolidColorBrush`；渐变会被拍平成代表色。`Transform`/`CornerRadius`/`GridLength`/`DropShadow`/`Projection`/`Point3D` 仍没有等价物；坐标变换是 GDI+ 的 `Graphics.Transform` 而不是视图模型上的属性。
- **副作用（容易手贱的一点）**：`Point`/`Size`/`Color` 在 Core 里已经注册过，**不要**在这家再注册一遍。`RegisterInterpolator` 是进程级 last-writer-wins 的原子 `AddOrUpdate`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的 `RegisterInterpolator`），而 `Interpolator` 与 `InterpolatorCore` 两个静态构造谁先跑不确定；重注册会无声地把 Core 的实现换掉。WPF/Avalonia 那种「同名不同型的 `Point`」在这家**不存在**（它俩用的就是同一个 `System.Drawing.Point`），所以那条理由不适用。

---

## 二、平台硬限制，与由此产生的做法

### 2.1 没有 dispatcher、也没有可问的存活源 ⇒ 只能自己记

`_isAppAlive` 是静态字段，捕获成功时订阅一次 `ApplicationExit` 置 false（`UIThreadInspector.cs` 的 `SubscribeExit`/`OnApplicationExit`），`IsAlive => _isAppAlive`。同一个 UI 线程在新消息循环里再次被捕获时（同一线程、上下文变了或 `Application.MessageLoop` 已起）会把它复活。`PostCore` 在 `_isAppAlive == false` 时直接拒收，避免把动作排进一个不会再被泵的队列。对照有 dispatcher 的家：WPF/Jalium 直接问 `dispatcher.HasShutdownStarted`、WinUI 问 `queue.TryEnqueue` 的返回值（见 `wpf.md` §2.1）。**没有东西可问，所以必须自己记**。同一形状的还有 MAUI 与 Razor。

### 2.2 帧源不能是 WM_TIMER ⇒ 这家的 pacer 是「线程池定时器 + 投递到控件」

**`WM_TIMER` 是这个平台上唯一不能用来当帧时钟的机制。** Windows 只在消息队列空无一物时才合成它，而那一刻只有一个「空闲时刻」——哪个到期定时器先被问到就归谁（这家进程里每个 `Transition` 都有一条自己的定时器）。队列里持续有输入/重绘消息时它会饿死。视觉表面的重绘是异步 `VisualHost.InvalidateSurface()`，不再有逐笔 `WM_MOUSEMOVE` 的同步 `Update()`，但 pacer 仍不能退回 `WM_TIMER`：定时器到期时队列是否空由平台决定，不由本框架决定。

所以 `CreateFramePacer` 是 `affinity.IsCurrent(target) ? new PostedFramePacer(UIThreadInspector.DispatcherFor(target)) : null`（`TransitionInterpreter.cs` 的 `CreateFramePacer`）：续体由 `System.Threading.Timer` 到期后经 `Control.BeginInvoke` 投回目标窗口（`PostedFramePacer.OnDue`）。**目标不是 `Control` 时（POCO 时钟、`VisualElement`）借 `DispatcherFor` 找它的 `VisualHost` 或 `Application.OpenForms` 里仍活着的窗体**，否则 pacer 拿到 null、按默认线程池路径放行。投递出去的消息按 FIFO 送达，不等队列空；续体仍在控件线程上恢复 ⇒ 属性写入照旧直写、`Update`/`LateUpdate` 仍在该线程（pacer 存在的理由不变）。

- 对照：WPF / Avalonia / WinUI / MAUI / Jalium 五家**一律**从 `affinity.ThreadFor(target)` 派生（WPF 是 `affinity.ThreadFor(target).TryGet<Dispatcher>(out var d) ? new DispatcherFramePacer(d) : null`，`Src/Adapters/VeloxDev.WPF/TransitionSystem/TransitionInterpreter.cs` 的 `CreateFramePacer`；其余四家同形，只换 `TryGet` 的类型）——它们的 `DispatcherTimer` 是**按优先级排队的队列项**，不受这条规则影响；Razor 干脆不覆写，注释写着「Blazor 没有在渲染器自己线程上触发的定时器」（`Src/Adapters/VeloxDev.Razor/TransitionSystem/TransitionInterpreter.cs` 的类型注释）。
- **仍然「有条件地」取**（七家里唯一，见 §三·4）：判据是「续体要投到哪个控件的窗口上，只有已经在它线程上时才认这个前提」。
- `IsCurrent(target)` 走基类的 `IsCurrentFor(target, ThreadFor(target))`（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs` 的 `IsCurrent`），而这家覆写了 `IsCurrentFor`（见 §三·1）。
- **后果（要记住）**：从非 UI 线程**首次**发起一段动画 ⇒ 没有 pacer ⇒ 回落到 `ArmNextFrame` 的默认实现（`Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionInterpreter.cs` 的 `ArmNextFrame`），而 `FrameWait` **不还原 `SynchronizationContext`**（`extension.md` §F 已写）⇒ `Update`/`LateUpdate` 会漂到线程池线程上跑。所以「确保第一次触碰发生在 UI 线程」在这家不是优化，是前提。
- **代价（换来的东西不是白给的）**：每帧多一次 `BeginInvoke`（一次小对象分配 + 一条投递消息）。空闲实测：16 条动画 60 fps 下 ~1300 次投递/秒，同时队列里还有 ~770 次 WM_PAINT/200 ms，画布每次重画 ~4 ms —— 都在余量内。CPU 全忙时帧会排队变慢而不是停止：`SamplerSet.Apply` 在执行时读的是最新时刻，所以积压的帧落地时画的是当前位置。

### 2.3 目标无句柄 / 非控件时的退路

`PostedFramePacer.OnDue` 在目标不是 `Control`、或 `IsHandleCreated == false` 时就地 `Fire()`（`TransitionInterpreter.cs` 的 `PostedFramePacer.OnDue`）——**即退回到默认的线程池等待语义**：续体在工作线程上恢复，`Update`/`LateUpdate` 也就漂到那里，属性写入各自编组（`Post` → `PostCore` → `BeginInvoke`）。这不是缺陷而是刻意的：pacer 只在「有窗口可投」时才值得存在，其余情况用它只会多一层等待。句柄在检查与投递之间消失（`BeginInvoke` 抛 `InvalidOperationException`，已释放控件抛其派生类 `ObjectDisposedException`）走同一条退路。

### 2.4 平台定时器是 `IDisposable`，而基类不管释放

`Dispose` 必须 `_disposed = true; timer = _timer; _timer = null; base.Dispose(); timer?.Dispose();`（`TransitionInterpreter.cs` 的 `PostedFramePacer.Dispose`）：基类先停表并放行挂着的续体，之后这块表才轮到被释放。WPF 那家的 `Dispose` **只停表、不释放**（见 `wpf.md` §2.2）——WPF 的 `DispatcherTimer` 生命周期归 dispatcher 管，这家的 `System.Threading.Timer` 必须自己 dispose，否则它自己的线程池回调（以及那条对已死窗口的投递尝试）会一直活下去。

`Arm` 在已释放时必须 `Fire()` 放行而不是直接 return（`PostedFramePacer.Arm` 的 `_disposed` 分支）：基类 `Dispose` 就是靠「唤醒挂着的续体」让采样循环收尾的，丢掉它等于把循环永久停在一次等不到的唤醒上（宿主侧表现为「不报错、也再没有帧」）。

### 2.5 捕获是惰性的，而且「按类型名认亲」

- 捕获发生在首次从 UI 线程碰到这个类时（`EnsureCaptured`，`UIThreadInspector.cs`），判据是 `SynchronizationContext.Current?.GetType().Name != "WindowsFormsSynchronizationContext"`，并且**跳过线程池线程**（`Thread.CurrentThread.IsThreadPoolThread`）——池线程可能从 ExecutionContext 流回同一个上下文，引用相等不代表线程相同。同一 UI 线程换了新上下文（新的消息循环）会改绑；另一条还活着的 UI 线程不改绑；字符串比较仍是为了不引用那个类型。
- 唯一的显式入口是 `CaptureUIThread()`（`UIThreadInspector.CaptureUIThread`），且必须在 `Application.Run` 之前调。
- `IsCurrentThread` 同时要求上下文引用相等**和**当前托管线程 id 等于捕获到的 UI 线程 id；`PostCore` 对 `Control.BeginInvoke`/`SynchronizationContext.Post` 的 `InvalidOperationException`、`ObjectDisposedException`、`InvalidAsynchronousStateException` 一律返回 false，绝不把异常抛给异步续体（线程池上的未处理异常会终止进程）。`PostToContext` 用 `PostState.Invoked` 区分「上下文拒绝投递」与「动作自己抛异常」——后者照常外传。

---

## 三、与其它六家的刻意背离

| # | 这里的做法和其他家不一样，因为… | 依据 |
|---|---|---|
| 1 | **`IsCurrentFor` 被覆写（七家里唯一）**：先问目标 `Control.InvokeRequired`，拿不到控件才回落到基类。WinForms 没有任何 API 能「查一个 `Control` 属于哪个线程」，但 `Control` 自己答得出「调用方在不在我的线程上」——**同一个问题的对偶问法**。这不只是查询优化：`ThreadDispatcherBase.Post`（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs` 的 `Post`）在 `IsCurrentFor` 为真时直接 `RunInline`，所以覆写它等于让 UI 线程上的写完全绕开消息泵。 | `UIThreadInspector.cs` 的 `IsCurrentFor` |
| 2 | **`PostCore` 优先走目标 `Control.BeginInvoke`（七家里唯一）**：别家一律从 `thread` 里 `TryGet<TDispatcher/TSyncContext>`。这家先看目标本身是不是一个已建句柄的 `Control`（`ControlDispatcher`），是就直接投。所以**即使从未捕获过 UI 上下文、即使首次调用来自后台线程**，只要 `target` 是 `Control`，写也落对线程（`ControlDispatcher` 的 XML 注释明写这一点）。基类专门声明这是被允许的自由（`ThreadDispatcherBase` 的 `PostCore` remarks：需要 target 而不是 thread 的宿主可以忽略 `thread`）。 | `UIThreadInspector.cs` 的 `PostCore` |
| 3 | **`ThreadFor` 返回「已捕获的上下文」，不为调用方造一个**：`ThreadRef.From(_uiSyncContext)`（`ThreadFor`）。没捕获过就是 `From(null)`。Razor 那家给的是「当下看到的」`SynchronizationContext.Current ?? 捕获值`——**两家在「调用方不在 UI 线程时给什么」上是相反的选择**。基类契约禁止「造一个」（`extension.md` §二·6），这家与 Razor 的分歧在于「拿什么当兜底」。 | `UIThreadInspector.cs` 的 `ThreadFor` |
| 4 | **pacer 在七家里唯一「有条件地」取得**（见 §2.2）：别家要么给、要么不给，这家先问线程再决定；而且它是七家里唯一**不拿平台定时器当帧源**的 pacer（线程池定时器 + `Control.BeginInvoke`，因为 WM_TIMER 会被输入饿死）。非控件目标通过 `UIThreadInspector.DispatcherFor` 借活窗体。 | `TransitionInterpreter.cs` 的 `CreateFramePacer`、`PostedFramePacer.OnDue` |
| 5 | **`TransitionEffect` 连 `Priority` 默认值都不用给**：`TransitionEffect : TransitionEffectCore` 是纯空壳（`TransitionEffect.cs` 的空壳），因为 `NonPriority` 是空结构体，`default!` 就是全部答案（`ThreadDispatcherBase` 的 `InternalPriority` remarks）。WPF 那家必须在这里给一个 `DispatcherPriority`。 | `TransitionEffect.cs` |

（**不改的**：`State : StateCore` 空壳、`TransitionScheduler` 非泛型空壳（非泛型 `TransitionScheduler`）、`ThemeValueConverters.cs` —— 六家都有这一份，Jalium 才没有，所以它不构成背离。）

---

## 四、坑（带依据）

### 4.1 `decimal` 重载能声明、能编译、动画静默不动

`Transition<T>.Property(Expression<Func<T, decimal>>, decimal, ...)` 存在（`Transition.cs` 的 `decimal` 重载），但**全仓库没有任何地方为 `decimal` 注册采样器**：Core 的注册表里没有（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的静态构造），本家的注册表里只有 `Padding`（`Interpolator.cs` 静态构造）。全仓库 `typeof(decimal)` 的命中全部在 AI 工具面（`Src/Core/VeloxDev.Core.Extension/Agent/...`），与动画无关。

运行后果**不是抛异常**：`InterpolatorCore.Prepare` 走到最后一步找不到采样器，只发一次 `Warn(WarnStage.Unsampled, …)` 然后 `continue`（`Src/Core/VeloxDev.Core/TransitionSystem/Sampling/Interpolator.cs` 的 `Prepare` 里 `sampler == null` 分支）。于是一段「只动 `decimal` 属性」的动画会不报错、不画、直接跑完。**要动画 `decimal` 必须自己写采样器并注册。**

### 4.2 `Brush` 类型的主题属性会「全程保持旧值、最后跳变」

`ThemeValueConverters.cs` 的 `ObjectConverter` 会为目标类型是 `Brush` 的属性造 `SolidBrush`（`ObjectConverter` 里那条 `SolidBrush` 分支），但本家没有 `Brush` 采样器。于是 `Brush` 类型的主题属性走的是 `ThemeManager` 记录在案的降级路径：整场动画不写它，等整场结束后统一写终值（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs` 的 `RunSwitch` 里 `ApplyHeldValues(groups)` 前的注释 + `ApplyHeldValues`）。**这不是 bug**，但要做「刷子渐变」类的主题过渡就得自己补采样器。

### 4.3 `SetPlatformInterpolator` 从没为 WinForms 调用过 ⇒ `CreateScheduler` 在仓库内没有执行路径

`SetPlatformInterpolator(new Interpolator())` 只出现在 Avalonia / WPF 的 theme demo（`Examples/Theme/Avalonia/Demo/App.axaml.cs`、`Examples/Theme/WPF/Demo/App.xaml.cs` 等）——`Examples/Theme/` 下**没有** WinForms 目录。而 `CreateScheduler` 的唯一调用者是 `ThemeManager.RunSwitch` 里的 `interpolator.CreateScheduler(...)`。

⇒ 本家 `Interpolator.CreateScheduler`（`Interpolator.cs` 的 `CreateScheduler`）是**给下游消费者的接口，不是现成能力**：没有主题动画的 demo 覆盖它。要用主题动画，得自己在 `Application.Run` 之前调一次 `ThemeManager.SetPlatformInterpolator(new Interpolator())`。

### 4.4 `PaddingSampler` 用截断，Core 的同族用舍入

`(int)(t * delta)`（`Samplers/PaddingSampler.cs` 的 `InsertFrame`）是**截断**；Core 的 `SizeSampler`/`PointSampler` 用 `(int)Math.Round(...)`（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/SizeSampler.cs` 的 `InsertFrame`）。后果有限但确实有：每帧像素落点最多差 1px，且**不累积**（每帧都从 `start` 重算）。想让 `Padding` 与同屏的 `Size`/`Point` 落点一致，就改成 `Math.Round`。

另外两点（都是与 Core 同族的有意对照，不是缺陷）：
- `PaddingSampler` 忽略 `options`（签名收了 `object? options` 但 `InsertFrame` 体里没用）⇒ `interpolationOptions` 对 `Padding` 无效。
- `SizeSampler` 特意让宽高共享一个 `BoundedProgress` 并在 0 处停住（`SizeSampler.cs` 里 `BoundedProgress` 那段注释：负尺寸不可表示）；`Padding` 的四条边各算各的，因为四个 int 接受负值 —— **这就是验收表把这条判成 `SamplerRule.Extrapolate` 而不是饱和的原因**（`Src/Test/AutoTest/Samplers/WinFormsEntries.cs` 的 `Expected` 上方注释与实现，写明了「收缩的一侧在 t > 1 处会算出负的 Padding，而 Padding 只是四个 int，接受负值」）。`ThicknessSampler` 与 `PaddingSampler` 同形；`BrushSampler` 逐通道 `Math.Round` 后夹到 0..255，所以判成 `Saturate`。

### 4.5 静态构造的触发时机：这家的注册只有一个，漏掉等于「全部不动画」

`Interpolator` 的静态构造（`Interpolator.cs`）只在 `Interpolator` 这个类型被第一次触碰时跑。`TransitionCore<...>` 有 `protected TInterpolatorCore interpolator = new();`（`Src/Core/VeloxDev.Core/TransitionSystem/Effects/Transition.cs` 的 `TransitionCore<...>` 里 `interpolator` 字段），在 WinForms 上就是 `new Interpolator()` ⇒ 只要 `new Transition<T>()` 过一次，注册就发生了，**不需要任何显式注册调用**。反过来：绕开 `Transition<T>` 直接调 `InterpolatorCore.Prepare` 时 `Padding` 没注册。别家漏一个注册只是少一个类型，这家漏掉就是全部（整张注册表就靠这一行）。

### 4.6 失效的 `WindowsFormsSynchronizationContext` 能把进程打死

旧的 `UIThreadInspector.PostCore` 直接 `control.BeginInvoke` / `context.Post`，没有兜底；`EnsureCaptured` 还会接受线程池线程从 ExecutionContext 流回来的 WinForms 上下文。于是 `Transition` 的 `await` 续体在池线程上把帧 Post 回一个编组窗口没有句柄的上下文时，`Control.MarshaledInvoke` 抛 `InvalidOperationException`（「在创建窗口句柄之前，不能在控件上调用 Invoke 或 BeginInvoke」）；`AwaitTaskContinuation` 在池线程上 `Task.ThrowAsync` 再投一次同一死上下文，第二次异常到达 `ThreadPoolWorkQueue.Dispatch` ⇒ 未处理异常终止进程（Windows 事件日志 .NET Runtime 1026 的栈可作为依据）。

现在的约束：`PostCore`/`PostToContext` 对 `InvalidOperationException`/`ObjectDisposedException`/`InvalidAsynchronousStateException` 一律返回 false；`ControlDispatcher` 优先目标的活 `Control`/`VisualHost`，非控件目标退到 `Application.OpenForms` 的活窗体；`EnsureCaptured` 跳过线程池线程、同一 UI 线程换上下文时改绑、退出后允许复活；`IsCurrentThread` 额外要求托管线程 id 等于捕获的 UI 线程 id；`CreateFramePacer` 对非控件目标用 `UIThreadInspector.DispatcherFor` 借活窗体，不再让采样停在池线程上。回归：`UIThreadInspectorTests` 的 `PostCore_ReturnsFalse_WhenThePinnedContextCannotPost`、`PostCore_UsesALiveOpenForm_ForATargetWithoutItsOwnWindow`、`PostCore_RefusesOnceTheApplicationHasExited`、`PostAsyncAndRun_CompleteWhenTheHostRefuses`。

---

## 五、验收覆盖面

- WinForms 的验收表（`Src/Test/AutoTest/Samplers/WinFormsEntries.cs` 的 `All`）覆盖 `PaddingSampler`、`ThicknessSampler`、`BrushSampler`；`BrushSampler`/`ThicknessSampler` 因与其它适配器同名，用反射从 WinForms 程序集取类型。`Src/Test/AutoTest/Samplers/SamplerCoverageTests.cs` 的 `ExpectedAdapterAssemblies` 里那个 `"VeloxDev.WinForms"` 字符串是「这家还在名单上」的唯一依据。
