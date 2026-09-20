# TransitionSystem — 扩展

> 代码：`Src/Core/VeloxDev.Core/TransitionSystem/`，契约在 `Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/`，
> 时钟在 `Src/Core/VeloxDev.Core/Timing/`，编组在 `Src/Core/VeloxDev.Core/Threading/`。
> 七家平台实现在 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`。
>
> **本文只写共性契约**：扩展点在哪、官方做法、联动清单。
> **一家的平台硬限制、刻意背离、该家特有的坑一律不写在这里** —— 那些在 `memory/modules/TransitionSystem/adapters/<平台>.md`。

---

## 一、扩展点地图

| 我要扩展… | 扩展点 | 具体成员 / 位置 |
|---|---|---|
| 让某个类型可以动画 | `ISampler` | `Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ISampler.cs:11`（三个成员）+ `InterpolatorCore.RegisterInterpolator`（`TransitionSystem/Interpolator.cs:89`） |
| 让一个**值类型**整体动画（不写新采样器） | `ISampleable` | `Interfaces/TransitionSystem/ISampleable.cs:14`；由 `StructAssembler.Create` 在 `Interpolator.cs:172` 处装配 |
| 只给某条路径换采样器（不动全局注册表） | `State.SetInterpolator` | `TransitionSystem/State.cs:29` / `:84`；`Prepare` 里 override 优先于注册表（`Interpolator.cs:160`） |
| 缓动曲线 | `IEaseCalculator` | `Interfaces/TransitionSystem/IEaseCalculator.cs:3`（`double Ease(double t)`）+ `TransitionSystem/Eases.cs` 的静态成员 + `TransitionEffect.Ease` |
| 帧的等待方式（宿主定时器/中央帧循环） | `TransitionInterpreterCore.CreateFramePacer` | `TransitionSystem/TransitionInterpreter.cs:79` + `FramePacerCore` 的 `Arm`/`Disarm`（`TransitionSystem/FramePacerCore.cs:66`/`:75`） |
| 帧唤醒的兜底路径（不用 pacer） | `TransitionInterpreterCore.ArmNextFrame` | `TransitionInterpreter.cs:94` |
| 线程归属 / 编组 / 存活 | `TransitionHostBase<TPriorityCore>` | `TransitionSystem/TransitionHostBase.cs`；三个成员见 §三·C |
| 平台接缝：给一个只有 `object` 的 target 找到 scheduler | `InterpolatorCore.CreateScheduler` | `Interpolator.cs:120` |
| 时钟（宿主拥有时间） | `TimerCore.RegisterTimeSource<TContract>` | `Timing/TimerCore.cs:62`；`TimeSourceCore` 的 protected 构造 `Timing/TimeSourceCore.cs:168` + `SetHostFeeding`（`:368`） |
| 链式动词（`Then`/`Await`/`Repeat`/…） | `StateSnapshotCore.Core*` + `TransitionCoreEx` | `TransitionSystem/StateSnapshot.cs:100-115`、`TransitionSystem/TransitionEx.cs:6` |
| 效果事件 | 9 个 `WeakDelegate` 事件 | `TransitionEffect.cs:45-53`；参数 `TimeLine/TransitionEventArgs.cs:3` |
| 索引实参的求值时机 | `PathIndex.Frozen<T>` | `TransitionSystem/PathIndex.cs:24`；身份在 `PathSegment.cs:339` |
| 新平台适配器 | 见 §三·C | 八个类 + 联动清单 §四 |

**本模块只有一个枚举要联动**：`RotationDirection`（详见 §4.6）。除此之外，事件 stage 与诊断 stage 都是**自由字符串**（`TransitionEventArgs.Stage` 是 `string?`，`TimeLine/TransitionEventArgs.cs:6`；诊断 stage 是 `"Prepare"`/`"Unsampled"`/`"Dropped"`/`"Sampling"`/`"Run"`…），新增一个 stage 不需要改任何 `switch`，也**不会有编译器提醒你漏了哪处** —— 这是本模块最容易漏的一项的形状。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. `ISampler` 注册与实现

**官方**：采样器是**无状态单例**；每动画的临时值只能放在 `ref object? working` 里，由采样器在第一个中间帧惰性创建、之后复用。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 把「本次动画」的状态放进采样器字段 | 注册表是**进程级**的（`ConcurrentDictionary<Type, ISampler>`，`Interpolator.cs:39`），同一个实例服务所有 target、所有动画，直接串台 | `ISampler.cs:31-36` 明写「per-animation reusable scratch」 |
| `InsertFrame` 里就地改 `start`/`end`（例如拿 start 当 scratch） | 它们与快照**共享**，改了污染快照，下一次迭代/另一个目标读到被改过的端点 | `ISampler.cs:6-9` 明写 |
| `t <= 0` / `t >= 1` 返回 scratch，而不是调用方给的 start/end 本身 | 嵌套路径（`((TranslateTransform)x.RenderTransform).X`）依赖端点声明时的**运行时类型**，scratch 会把类型换掉 | `skills/veloxdev-create-animation/references/adapter.md:75` |
| 只注册具体类型，却希望基类型属性也走它 | 查找**只向上走**：精确 → 基类由近及远 → 接口按名字序。注册 `SolidColorBrush` 不会命中 `LinearGradientBrush`；而**声明类型本身就是抽象基类**时更彻底 —— 一条键都没有，`Prepare` 报一次 `Unsampled` 把该属性整个跳过。WPF 走过这个形状（注册 `DropShadowEffect`，而它自己的 `UIElement.Effect` DP 声明成 `Effect`），2026-09-20 改成注册基类型（见 `adapters/wpf.md` 坑 7） | `Interpolator.cs:50-87`、`Interpolator.cs:175-179` |
| 注册了基类型却只处理具体类型 | 反过来同样成立：注册 `Brush` 就承诺处理整个家族，梯度也会进来。**改注成基类型是这条义务的开始，不是结束** —— WPF 2026-09-20 改注 `Effect` 的同一次改动里，兜底分支就从「凭空造一个 `DropShadowEffect` 顶替」改成了如实交出端点（否则 `BlurEffect` 会被静默画成阴影） | `adapter.md:153`、`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/DropShadowEffectSampler.cs:44` |
| 用 `UnregisterInterpolator` 做「临时替换，用完恢复」 | 安装是 **last-writer-wins 的原子 AddOrUpdate**（`Interpolator.cs:93`），并发下先后顺序不由你定，恢复可能盖掉别人的注册 | `Interpolator.cs:89-95` |
| 在 `Prepare` 之后才注册，以为能生效 | 采样器**每个 property 每趟动画只解析一次**，且在首趟的 `Prepare` 里 | `Interpolator.cs:164` |

### 2. `ISampleable`（值类型整体动画）

**官方**：`readonly struct` + `GetAnimatableMembers()` 用 `TransitionProperty.ReadableMembers<T>(...)` 声明 + `CreateFrameValue` 按同一顺序在**构造函数**里重建（零反射）。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用 `Members<T>(...)` 而不是 `ReadableMembers<T>(...)` | `Members<T>` **要求成员可写**，值类型的只读属性会抛；`ReadableMembers<T>` 不要求 | `TransitionProperty.cs:624` / `:645` |
| 以为引用类型也能走这条路 | 引用类型**不**装配；整个对象路径必须逐成员表达或注册专用 `ISampler` | `ISampleable.cs:9-13`、`TransitionPathUnsampleableException.cs:7-10` |
| 成员里有一个没有采样器的类型 | `StructAssembler.Create` 返回 null，整个属性**静默跳过**（只报一次 `Warn("Unsampled")`），不抛 | `StructAssembler.cs:24-27`、`Interpolator.cs:175-179` |
| 想给**成员**配自定义插座（`State.SetInterpolator`） | 装配器解析成员**只查注册表**，不查 per-path override —— 成员的自定义插座无效 | `StructAssembler.cs:24` |
| `CreateFrameValue` 顺序与 `GetAnimatableMembers` 不一致 | 值会装到别的成员上，且**不报错** | `ISampleable.cs:23-28`（顺序是契约） |
| 让 `GetAnimatableMembers()` 返回空 | 同样静默跳过 | `StructAssembler.cs:17` |

### 2b. 值类型没有采样器时：`RejectUnsampleablePaths` **不会拦住你**

**官方**：让一个值类型可动画，只有两条路 —— 注册一个 `ISampler`，或让这个 struct 实现 `ISampleable`。**两条都不走时，这条路径是静默失效的**，不是编译错误、不是异常。

`RejectUnsampleablePaths`（`Transition.cs:205`）只拒绝**引用类型**：`if (property.PropertyType.IsValueType) continue;`。它豁免值类型的理由是 `ISampleable` 那条路 —— 但那条路只有 `ISampleable` 的实现者走得通。于是**「是值类型」但「既没注册采样器、又没实现 `ISampleable`」的类型从这道门底下漏了过去**，落到 `Prepare` 的 `sampler == null` 分支：`Warn("Unsampled")` + `continue`（`Interpolator.cs:162-179`）。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用 `Property(x => x.SomeDecimal, 1.5m)` 期望它动起来 | **六家的 `decimal` 重载都是空头支票**：全仓库没有任何 `DecimalSampler`，Core 也不注册 `decimal`（`Interpolator.cs:12-28`）⇒ 该属性每趟只报一次 `Warn("Unsampled")` 然后被跳过，**什么都不动**。`decimal` 是值类型，所以连声明期的 `TransitionPathUnsampleableException` 都不会抛 | `Interpolator.cs:162-179`、`Transition.cs:205`；`decimal` 重载位置：Avalonia `Transition.cs:160`、MAUI `:143`、Razor `:55`、WPF `:148`、WinForms `:62`、WinUI `:138`（**只有 Jalium 没有 `decimal` 重载**，在它那里是编译错误 —— 反而更安全） |
| 以为「用了平台自带的值类型就没问题」 | 平台类型同样受这条支配：Avalonia 有 `Avalonia.Rect` / `Avalonia.Vector` 却没注册，于是这两个类型的属性动画一律静默跳过（WPF/WinUI/MAUI 三家都注册了 `Rect`，所以这是 Avalonia 的缺口而非平台限制） | `Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/Interpolator.cs` 的静态构造；见 `adapters/avalonia.md` |
| 想靠「值类型成员可写」绕过去 | 值类型的中间态是**装箱副本**，逐成员写回不到原属性 —— 这正是 `StructAssembler` 要用 `CaptureProperty` 假路径收集成员的原因（`StructAssembler.cs:92-100`）。没实现 `ISampleable` 就没有这个装配器可用 | `StructAssembler.cs:92-100` |

**样本的判据是「这条路径动了没有」，不是「有没有报错」** —— 排查「某条路径不生效」时，先确认它的类型在不在该家 `Interpolator` 静态构造的注册表里。

**顺带校准「采样器条数」这个数**（`adapters/` 下多份文件都引它）：它**度量的是「这家框架自带的、Core 未覆盖的可动画值类型有多少个」，不是实现完整度**。Core 已把无 GUI 依赖的一整族注册完（4 个基元 + `System.Drawing` 的 7 个 + `System.Numerics` 的 4 个，`Interpolator.cs:12-28`），所以 WinForms（值类型面就是 `System.Drawing`）只剩 `Padding` 一个、Razor（动画面是内联 CSS 字符串，无带类型值）只有 `string` 一个，而 Avalonia/WPF/MAUI 各自带一整族平台类型所以是两位数。**数类数与数注册条数仅在 Jalium 上不同**（9 类 / 10 条 —— 它的 `BrushSampler` 同时注册 `Brush` 与 `SolidColorBrush`）。

### 3. `CreateScheduler`（平台接缝）

**官方**：`effect is ITransitionEffect<MyPriority> ? (TransitionSchedulerCore)TransitionSchedulerCore<Host, Interpreter, MyPriority>.FindOrCreate(target) : null`。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 直接 `new TransitionSchedulerCore<...>()` | **编译通过、动画正常跑、永远不可控**：只有 `FindOrCreate` 会把它登记到 target 名下，而登记正是 `Transition.Pause`/`Seek`/`Exit` 查找的依据 | `Interpolator.cs:116-118` 明写；`TransitionScheduler.cs:189` |
| 不按 effect 类型做判断，无脑返回一个 scheduler | 拿到一个跑起来**画不出任何东西**的 scheduler（scheduler 自己开头就会做同一个 cast 并静默 return） | `Interpolator.cs:110-112`、`TransitionScheduler.cs:23` |
| 干脆不写 `CreateScheduler`（基类默认返回 null） | 主题切换**仍然发生，但是瞬时的，什么都不记录** —— 与缺一个采样器同样的静默降级 | `Interpolator.cs:120`；`ThemeManager.cs:206-209`、`:227-230` |

### 4. 覆写 `InterpolatorCore.Prepare`

**官方**：必须**调用基类**。

- 捷径：覆写 `Prepare` 却不调 base → **冻结索引静默失效**。未绑定的属性自己也会解析实参，所以在一个含 `PathIndex.Frozen<T>` 的属性出现之前，行为**完全一样**，没有任何迹象（`TransitionProperty.cs:130-135` 明写）。
- 观察所得（不是注释说明的有意行为）：`Prepare` 里自己 new 的 `TransitionDiagnostics` **没有传 run**（`Interpolator.cs:140`），而 `TransitionDiagnostics.Raise` 只在 `run is not null` 时才把 `args.Handled` 落到 run 上（`TransitionDiagnostics.cs:44`）。所以**逐属性的 `Warn("Unsampled"/"Unreadable")` 里置 `Handled` 不会终止动画**，而 `Awake`/`Prepare`/`Dropped`（调度器创建的那份带了 run）里置会。想在 `Prepare` 阶段否决，走调度器那条路。

### 5. 帧 pacer

**官方**：`Arm` 每次挂上「一枚**可重复**的定时器 + 一个 pending 续体」，定时器 tick → `Disarm()` → `Fire()`；`Dispose` 里 `Disarm` 并**放行**续体。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用 `IsRepeating = false` 的定时器 | 只 tick 一次 → **动画恒为两帧**：值冻在起点、闭式解那半仍然通过、不抛任何异常 | `adapter.md:63` |
| `Arm` 不挂定时器 / 永不 `Fire` | loop **永久停车**：无异常、无帧，宿主侧看不到任何迹象 | `FramePacerCore.cs:21-25` |
| 一次 `Arm` 里 `Fire` 两次 | 双采样（同一帧写两次，FPS 上限失效） | 同上 |
| 从**平台**而不是 `affinity.ThreadFor(target)` 派生等待线程 | pacer 与写路径不一致 → **每帧一次 dispatch**，正是采样路径要避免的那件事 | `TransitionInterpreter.cs:64-67` |
| 覆写 `Dispose` 不调 `base.Dispose()` | 挂着的续体被搁死（loop 停在半路）；宿主定时器资源也不释放 | `FramePacerCore.cs:97-102` |
| 覆写 `ArmNextFrame` 时在取消后不调用续体 | loop 永久停车；默认实现特意在已取消时**立即放行**，不等一个间隔 | `TransitionInterpreter.cs:88-101` |

### 6. 宿主（`TransitionHostBase` / `IThreadDispatcher`）

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| `PostCore` 在队列已消失时乐观返回 `true` | `PostAsync` **只在动作被真正接受时才等**完成源 → 消费者挂到进程结束 | `adapter.md:37`、`ThreadDispatcherBase.cs:64` |
| `ThreadFor` 里为调用方**造**一个 dispatcher | 宿主契约明令不许：这会把「不知道」变成「假装知道」，让写落到错的线程 | `Threading/IThreadDispatcher.cs:14-17` |
| 在写路径再加一层 `catch { }` | 失败已被 `SamplerSet.ApplyCore` 收口并终止这一趟；第二层吞掉只会掩盖是哪一层看到的失败 | `adapter.md:53`、`SamplerSet.cs:136-142` |
| `IsCurrentThread` 用「当前线程 == 我记的那个」之外的花招 | 基类握的是不透明句柄、无法与调用线程比较，所以这是**基类无法提供**的那个谓词；默认从 target 推（任何 GUI 都成立：视图只能在 UI 线程创建） | `adapter.md:51` |
| **有优先级的宿主不覆写 `InternalPriority`**（默认 `default!`） | `Prepare` 读起点用的是**阻塞读** `Run<T>`（`Interpolator.cs:147`），跨线程时它按 `InternalPriority` 排队；`default(DispatcherPriority)` 是 `Inactive`，这条读要等消息泵**完全空闲**才跑 —— 表现为动画启动前的一次停顿，而读阻塞的是发起动画的那个线程 | `ThreadDispatcherBase.cs:39-44`、`:71-87` |

### 7. 写路径

**官方**：任何属性写入只能经 `SamplerSet.Apply`（`TransitionSystem/SamplerSet.cs:100`）。

- 捷径：自己 `Post` 或直写 target → 绕过**两道** stale-frame 守卫（排队前 `:102`、落地后 `:126`）与 `CanSetValue()`（宿主已死）：一个在动画被取消前就已排队的帧会覆盖 reset 的结果。
- 这条对**采样器**也成立：采样器通过 `InsertFrame` 收到的 `property` 写入，不要去拿 `property` 的 `SetValue` 之外的路径。

### 8. 时间源

**官方**：按**契约**注册 —— `TimerCore.RegisterTimeSource<ITimeSourceControl>(static () => new MySource());`

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 按**实现类型**注册 | 查找按精确契约、**无回退**：注册在别处，`CreateTimeSource` 直接抛 | `TimerCore.cs:11-15`、`:102-121` |
| 注册一个共享单例 | 一条通道暂停会暂停所有通道；每次查找本就该新建 | `TimerCore.cs:28-31` |
| 用渲染循环替换时钟 | 明令不是这个用途：帧率会变成时间权威，而采样路径建立在相反的前提上 | `TimerCore.cs:17-26` |
| 直接实现 `ITimeSourceControl` 从头写时钟 | 官方路径是继承 `TimeSourceCore` 的 protected 构造（`:168`）只管**喂时间**，用 `SetHostFeeding`（`:368`）报告是否还在喂；其余（pause/rate/seek/park）由基类负责 | `TimerCore.cs:17-21` |

### 9. 效果

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 给平台的 `TransitionEffect` 子类**加字段/状态** | 运行时跑的是每段 `Clone()` 出来的对象，而 `TransitionEffectCore<TPriorityCore>.Clone()` 构造的是**基类型**（且不是 `virtual`，是 `new`）→ 子类的任何额外状态**在运行体里不存在** | `TransitionEffect.cs:14-16`、`Transition.cs:415` |
| 往共享 `static readonly` 的 effect 上加 handler / 用 `.Effect(共享实例)` 直接赋值 | 运行体虽是克隆，但克隆自同一个实例；声明又是共享的 → 所有使用者共享处理器。第二个后果**方向与直觉相反**：`WeakDelegate` 的组合委托是 `volatile` **强**字段（`Src/Core/VeloxDev.Core/WeakTypes/WeakDelegate.cs:22`/`:88`，类型 remarks `:6-18` 明说 Handlers are kept alive），所以在共享 owner 上订阅者不会被 GC，而是**反过来被保活**（视图/局部对象回收不掉）；"订阅者可能被回收"不成立 | `Transition.cs:589`；`skills/veloxdev-create-animation/SKILL.md:224`（"...on a `static readonly` one it would keep the view alive"）；`memory/modules/WeakTypes/architecture.md` §二 |
| 指望 `Start`/`Update`/`LateUpdate` 里置 `Args.Handled` 之外的事件也能否决 | `Handled` 只在循环的两个检查点被读：每趟开头与每帧开头 | `TransitionInterpreter.cs:199`、`:289` |
| 在 `Awake` 之外的 stage 里做「把 target 摆成起点」 | 只有 `Awake` 在 `Prepare` **之前**、且被 `await`；`Prepare` 在那之前不得读 target | `TransitionScheduler.cs:122-134` |

### 10. 控制面

**官方**：`Transition.Exit/Pause/Resume/SetRate/Seek`，或自己做 `scheduler.Track(run)` → `scheduler.Execute(...)` → `Untrack` → `run.Dispose()`（`ThemeManager.cs:248-261` 是范本）。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 手动 `cts.Cancel()` 而不经 `Exit` | 暂停中的循环停在时间轴的 park gate 上，它**不知道** token；必须同时 `run.Timeline.Wake()` | `TransitionScheduler.cs:378-383` |
| 在 `Track`/`DrainActive` 时把 target 锁跨着动画体持有 | 在 UI 线程 `Exit` 上阻塞 dispatcher；回调重入 `Exit`/`Execute` 会死锁（`SemaphoreSlim` 不可重入） | `TransitionScheduler.cs:230-234`、`:339-346` |
| 每段结束后 `Untrack`（而不是整条链结束） | 段间 `Await` 间隙里 `Exit` 找不到它，剩下的段照跑 | `Transition.cs:481-483` |
| 自己 `new TransitionRun(...)` 却不 `Track` | `Execute` 靠 `_activeRuns[cts]` 找回 run；找不回则帧集拿到一条**没人控制得住的私有时间轴** | `SamplerSet.cs:72-78`、`ThemeManager.cs:249-250` |
| 在动画出口 `run.Cts.Cancel()` | 一个 cts 可以交给好几段（`Transition` 就是这样），在出口取消会让动画在第一段后就结束 | `TransitionInterpreter.cs:393-398` |

### 11. 链式动词

**官方**：`StateSnapshotCore` 加 `internal abstract CoreXxx`（`StateSnapshot.cs:100-115`）→ `TransitionCore<...>` 覆写（`Transition.cs:535-578`）→ `TransitionCoreEx` 加扩展方法（`TransitionEx.cs:6`）。

- 捷径：只加扩展方法不接 Core 抽象 → 编译不过（抽象成员没有实现）。
- 捷径：只加 Core 覆写不加扩展方法 → 那个动词**根本调不到**（`Transition<T>` 上没有它）。三处都必改。
- 语义约定（照 `CoreRepeat` 写）：声明配的是**它写在其后的那个节点**（`Transition.cs:558`），不是链上别的段。

---

## 三、步骤清单

### A. 新增一个采样器（让某个类型可动画）

1. **写采样器类**：`ISampler` 三成员，无状态。
   - 无 GUI 依赖的类型 → `Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/`（现有 15 个是范本）。
   - 平台类型 → `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/Samplers/`。
2. **注册**：在该家的 `Interpolator` 静态构造里 `RegisterInterpolator(typeof(X), new XSampler());`
   - Core 类型 → `TransitionSystem/Interpolator.cs:12-28`。
   - 平台类型 → `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/Interpolator.cs` 的静态构造。
3. **声明入口**（只有 Razor 一定需要）：六家的 `Transition<T>` 有泛型 `Property<TValue>`（Avalonia `Transition.cs:35`、WinForms `:30`、WinUI `:37`、MAUI `:31`、WPF `:35`、Jalium `:34`），**Razor 没有**，它是逐类型手写重载（`Razor/PlatformAdapters/Transition.cs:31-129`）——**Razor 上 Core 有采样器但没重载的类型就是声明不出来**（例如 `long`：`Interpolator.cs:15` 注册了 `LongSampler`，Razor 的重载表里没有 `long`）。在 Razor 上加一个类型必须顺手补重载。
4. **测试表**：`Examples/Transition/AUTO TEST/Samplers/` 下该家的 `<平台>Entries.cs` 加一条。
   - 端点值在这个纯数据进程里造不出来时（要真 XAML/MAUI 运行时）→ 改加进 `UnreachableSamplers.cs`，并在 `Examples/Transition/AUTO TEST/Conformance/*Conformance.cs` 里补同名条目（见 §四）。
5. **demo**：`Examples/Transition/<GUI>/Demo` 的 probe/subject 加一行（照着现有行抄）。
6. **跑测试**：`SamplerCoverageTests`（`Samplers/SamplerCoverageTests.cs`）。

### B. 新增一个 `ISampleable` 值类型

1. 类型声明为 `readonly struct` 并实现 `ISampleable`（`Interfaces/TransitionSystem/ISampleable.cs:14`）。
2. `GetAnimatableMembers()` 用 `TransitionProperty.ReadableMembers<T>(...)`（**不要** `Members<T>`），顺序就是 `CreateFrameValue` 的参数顺序。
3. `CreateFrameValue(memberValues)` 用构造函数按该顺序重建（零反射）。
4. **逐个确认成员类型都已经有采样器** —— 缺一个整值静默跳过。
5. 测试 entry：`Samplers/CoreSamplerEntries.cs:36` 的 `SampleablePair` 是最小范本（两个成员类型不同，正好覆盖「每个成员走自己的采样器」）。
6. 参考实现：`Src/Core/VeloxDev.Core/WorkflowSystem/GUI/GeometryModels/Viewport.cs`（仓库内已落地的第二个例子）。

### C. 新增一个平台适配器

**要写的八个类**（全部镜像七家现成的；放 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`）：

| 类 | 基类 | 必须提供 |
|---|---|---|
| `UIThreadInspector` | `TransitionHostBase<TPriorityCore>` | `ThreadFor(object)`（public abstract）、`IsCurrentThread(ThreadRef)`（protected abstract）、`PostCore(object, ThreadRef, Action, TPriorityCore)`（protected abstract）；有优先级的宿主**必须**覆写 `InternalPriority`，存活用 `Lifetime` 报告或覆写 `IsAlive`（`TransitionSystem/TransitionHostBase.cs:9-14`、`Threading/ThreadDispatcherBase.cs:8-44`） |
| `TransitionInterpreter` | `TransitionInterpreterCore<TEffect[, TPriorityCore]>` | 无（`CreateFramePacer` 可选覆写） |
| `TransitionScheduler` | `TransitionSchedulerCore<THost, TInterpreter, TPriorityCore>` | 无（构造即够） |
| `Transition<T>` / `Transition` | `TransitionCore<T, State, TransitionEffect, Interpolator, UIThreadInspector, TransitionInterpreter, TPriorityCore>`（**七型参**） | `Create()`、`Effect(...)`、逐类型或泛型 `Property(...)` |
| `TransitionEffect` | `TransitionEffectCore<TPriorityCore>` 或 `TransitionEffectCore` | `Priority` 默认值 |
| `State` | `StateCore` | 无 |
| `Interpolator` | `InterpolatorCore` | 静态构造里的采样器注册 + **`CreateScheduler` 覆写** |

**必须遵守的契约（违背即静默出问题，见 §二）：**

- `CreateScheduler` 一定要**走 `FindOrCreate`**，不要 `new`；返回 `null` 是合法答案（「这个 effect 不是我的」）。
- pacer 的等待线程必须与 `affinity.ThreadFor(target)` 一致；`Arm` 的定时器必须**可重复**；`Dispose` 要 `base.Dispose()` 并释放宿主定时器。
- `PostCore` 必须诚实报告是否入队。
- `ThreadFor` 不能为调用方造 dispatcher。
- 采样器一律无状态单例，端点不回写。

**`TPriorityCore` 选型**：框架有 dispatcher 优先级就用它，没有就用 `NonPriority`（`Threading/NonPriority.cs`）。当前七家：WPF/Avalonia/Jalium = `DispatcherPriority`，WinUI = `DispatcherQueuePriority`，MAUI/WinForms/Razor = `NonPriority`（`TransitionScheduler.cs:5` 等各家声明；`adapter.md:159-163`）。优先级出现在**两处**：`Transition<T>` 的第七个型参，与 `CreateScheduler` 里测试 effect 的那个 cast —— 第二处写错会让主题切换静默降级为瞬切。

**注册位置**：只在自己家注册采样器（`Interpolator` 静态构造）、只在自己家返回 scheduler（`CreateScheduler`）。**Core 里没有任何一处需要改** —— 这正是这套适配器模式的目的。Core 侧要改的只有测试与 demo 的清单，见 §四。

**本文件不写**这家的硬限制、与别家的刻意背离、该家特有的坑 —— 写 `adapters/<平台>.md`（判据与四件事的清单见 `memory/specifications/memory-maintenance-specifications.md` 第四节）。

### D. 新增一个时间源（宿主拥有时间：播放器循环、媒体位置、音频回调）

1. 继承 `TimeSourceCore`，用 protected 构造 `base(nowStamp, ticksPerSecond)`（`Timing/TimeSourceCore.cs:168`）。
2. 报告是否还在喂：`SetHostFeeding(bool)`（`:368`）。
3. 注册：`TimerCore.RegisterTimeSource<ITimeSourceControl>(static () => new MySource());`（`TimerCore.cs:62`）—— **按契约注册**。
4. 每次查找都要新建（每个消费者自己的 pause/rate）；不要注册单例。
5. **不要**为了框架渲染循环做这件事（`TimerCore.cs:21-25`）；那种「把 loop 钉在 UI 线程」的需求由 `FramePacerCore` 解决。

### E. 新增一条链式动词

三处都改，顺序即依赖顺序：

1. `StateSnapshot.cs` 加 `internal abstract T1 CoreXxx<...>(...)`（`StateSnapshot.cs:100-115` 是现有五个的形状）。
2. `Transition.cs:535-578` 在 `TransitionCore<T,...>` 里覆写（里面有统一的三段式：类型不匹配就抛 `InvalidOperationException`）。
3. `TransitionEx.cs:6` 加 `public static T Xxx<T>(this T snapshot, ...)` 扩展方法 —— 泛型约束写成 `where T : StateSnapshotCore, new()`，七个平台的 `Transition<T>` 都满足。
4. 语义写进 XML 注释：**配的是它写在其后的那个节点**（照 `CoreRepeat` 的措辞，`Transition.cs:558`）。

### F. 自定义帧等待（不写完整 pacer）

1. 覆写 `TransitionInterpreterCore.ArmNextFrame`（`TransitionInterpreter.cs:94`）：不得阻塞调用线程，且**必须恰好调用一次续体，包括被取消时**。
2. `FrameWait`（`:128`）是 `INotifyCompletion`、**刻意不是** `ICriticalNotifyCompletion`。注意它**不会**把 loop 拉回 UI 线程（还原 `SynchronizationContext` 是 `Task` 的事）——不提供 pacer 的宿主上，loop 在第一帧后就会漂到线程池线程，`Update`/`LateUpdate` 会在那里跑（`:116-127` 明写）。

---

## 四、联动清单

**读法**：加一件事时，下表每一行都是一处必须同步改的地方。漏掉通常**不报错**，而是静默少一个能力（或绿灯下少验一批）。

### 4.1 加一个**平台**采样器

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/Samplers/XSampler.cs` | —— |
| 2 | 该家 `PlatformAdapters/Interpolator.cs` 静态构造里的 `RegisterInterpolator` | 该类型全程不可动画，`Warn("Unsampled")` 每趟一次 |
| 3 | 该家 `PlatformAdapters/Transition.cs` 的 `Property` 重载 | 只有 Razor 会漏到「声明不出来」（它逐类型手写）；六家泛型 `Property<TValue>` 自动覆盖 |
| 4 | `Examples/Transition/AUTO TEST/Samplers/<平台>Entries.cs` | `SamplerCoverageTests.EveryShippedSampler_IsAccountedFor`（`SamplerCoverageTests.cs:65`）**红** |
| 5 | `Samplers/AdapterSamplerEntries.cs:9-18` 的聚合数组 | 只有新增**平台**时才要；漏了则该家整张表不进注册表 → 同 4 红 |
| 6 | 若端点值造不出来：`Samplers/UnreachableSamplers.cs` + `Examples/Transition/AUTO TEST/Conformance/<平台>Conformance.cs` | `EveryUnreachableSampler_IsCoveredByTheAcceptanceTables`（`SamplerCoverageTests.cs:162`）按**名字**对账，两边都漏就红；一边漏也红 |
| 7 | `Examples/Transition/<GUI>/Demo` 的 probe/subject 一行 | 无自动失败；人工验收时这个类型没被真正画出来过 |

### 4.2 加一个 **Core** 采样器（无 GUI 依赖）

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `TransitionSystem/NativeSamplers/XSampler.cs` | —— |
| 2 | `TransitionSystem/Interpolator.cs:12-28` 的静态构造 | 不可动画 |
| 3 | `Examples/.../Samplers/CoreSamplerEntries.cs` | 同 4.1-4 红 |
| 4 | Razor 的 `Property` 重载（若该类型在 Razor 的重载表里不存在） | Razor 上声明不出来（六家不用） |

### 4.3 加一个**平台适配器**

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | 八个类（§三·C 的表） | 编译不过或画不出来 |
| 2 | `Examples/.../Samplers/<平台>Entries.cs` | 覆盖校验少一批 |
| 3 | `Samplers/AdapterSamplerEntries.cs` 聚合 | 同上 |
| 4 | `Samplers/SamplerCoverageTests.cs:23-33` 的 `ExpectedAdapterAssemblies` | 程序集没被反射到 → 覆盖校验**在少验一批的情况下显示绿色**（该测试就是为此存在的） |
| 5 | `Examples/Transition/AUTO TEST/.../VeloxDev.SamplerTest.csproj` 的引用 | 程序集根本不加载 → 同 4 |
| 6 | `Examples/Transition/AUTO TEST/Drivers/DemoCatalog.cs`（一行） | 该家的 demo 不进验收套件 |
| 7 | `Examples/Transition/AUTO TEST/Suites/PlatformSuites.cs`（一个类，负责开/关 demo） | 同上 |
| 8 | 跑不起来的采样器：`UnreachableSamplers` + `Conformance/<平台>Conformance.cs` | 见 4.1-6 |

（6/7 的操作指南在 `Examples/Transition/AUTO TEST/AGENTS.md`，见 `adapter.md:181`。）

### 4.4 加一条链式动词

`StateSnapshot.cs` 抽象 + `Transition.cs` 覆写 + `TransitionEx.cs` 扩展方法 —— **三处，缺一处要么编译不过，要么动词调不到**。

### 4.5 加一条缓动

1. `IEaseCalculator` 实现类（`TransitionSystem/Eases.cs` 里现有 30 个是范本）。
2. `Eases` 的静态嵌套类加 `In`/`Out`/`InOut` 属性（`Eases.cs:7-12` 的形状）。
3. 注意 `Eases.Xxx.In` 这类 getter **每次访问都 new 一个**（`Eases.cs:5`）。一条缓动若内部要用另一条，**必须在类里持一个 `static readonly` 实例**而不是走 `Eases.Xxx.Out` —— 否则每帧分配一个对象，而这是采样循环的热路径（`EaseInBounce`/`EaseInOutBounce` 就是这么做的，理由写在 `Eases.cs:217-219` 与 `:238-239`）。

### 4.6 加一个 `RotationDirection` 方向值

`RotationDirection` 是**唯一一个跨 Core 与适配器都被消费的枚举**（`TransitionSystem/RotationDirection.cs:8`，`[Flags]`，作为 `Property(..., interpolationOptions)` 透传）。加一个值要同步的消费者：

| # | 位置 | 说明 |
|---|---|---|
| 1 | `RotationDirection.cs` | 加 `1 << n` 的位值 |
| 2 | `NativeSamplers/DoubleSampler.cs:15-19` | 2-D/无轴旋转的角度取近方向 |
| 3 | `NativeSamplers/QuaternionSampler.cs:22-36` | 3-D Slerp 的方向选择 |
| 4 | 各家的 transform/projection 采样器（WPF `TransformSampler.cs:216-221`、Avalonia `:238-254`、Jalium `:229-234`、WinUI `ProjectionSampler.cs:39-55`） | 逐轴方向 |

**没有任何地方会因为你漏了一处而报错** —— 未处理的方向会被当成 `Auto` 静默走过。

### 4.7 本模块**不需要**联动的东西（省掉无谓的搜索）

- **事件 stage / 诊断 stage 是自由字符串**（`TransitionEventArgs.Stage` 是 `string?`），没有枚举、没有 `switch`，新增一个 stage 不需要改任何地方，**也不会有编译器提醒**。
- **没有生成器**：`VeloxDev.Core.Generator` 不参与 TransitionSystem（对比 `WorkflowSystem` 有生成器联动）。
- **Core 不为新平台改任何一行**：采样器注册与 `CreateScheduler` 都在适配器侧。Core 侧要动的只有测试与 demo 清单（§4.3）。

---

## 五、死扩展点与已失效的钩子

- **`StateSnapshotCore.CoreRecordState()`（`StateSnapshot.cs:105`，实现于 `Transition.cs:324`）没有任何调用者**：`Src/`、`Examples/` 里搜不到。覆写它不会产生任何效果。
- **`TransitionSchedulerCore.RemoveMutualScheduler`（`TransitionScheduler.cs:254`）在 `Src/` 内零调用者**：互斥 scheduler 每 target 缓存一生。所以 `TryGetMutualScheduler(target)` 返回 `true` 只说明「这个 target 跑过互斥动画」，不说明现在有东西在跑；非互斥那张表在每条动画结束时确实会被清空（数组会空），所以判断「还在跑」要读**数组长度**，不要读那个 bool（`Transition.cs:231-255`、`Examples/Transition/Avalonia/Demo/Views/MainWindow.axaml.cs:798-805`）。
