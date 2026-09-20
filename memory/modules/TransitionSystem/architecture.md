# TransitionSystem — 架构

> 代码：`Src/Core/VeloxDev.Core/TransitionSystem/`（38 个 .cs）、契约在 `Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/`；
> 七家平台实现在 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`。
> 依赖的子系统：`Timing/`（时钟）、`Threading/`（线程归属与编组）、`TimeLine/`（事件参数）、`WeakTypes/`、`Lifetime/`。

本文只写「读完 38 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 把「某个 target 对象上的若干属性路径」从声明时的当前值插值到声明的新值，沿一条**绝对时间轴**推进，并把该 target 的控制面（暂停 / 调速 / 快进 / 退出 / 查询）暴露成按 target 寻址的静态 API。

一句更短的：**路径 + 端点 → 帧**，外加**一个 target 一份控制面**。

**不解决什么（这些边界常常被误以为在模块内）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 时间本身（暂停/倍速/锚点/epoch） | `Timing/TimeSourceCore`（`Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs`）。TransitionSystem 只是**读**它、并在取消时 `Wake()` 它 |
| 目标是哪个线程、怎么把工作送过去 | `Threading/IThreadDispatcher` + `Threading/ThreadDispatcherBase`；平台侧是各家的 `UIThreadInspector` |
| 帧何时到来 | `FramePacerCore`（本模块，但只有「等在哪」这一件事）+ 平台定时器；**不是**时间权威（`TransitionInterpreter.cs:144` 明说 `Task.Delay` 从不是 timing source） |
| 呈现 / 重绘 | 宿主。WPF 不因 gradient stop 写入而重绘，所以 WPF 的声明要自己挂 `LateUpdate += InvalidateVisual`（见 `skills/veloxdev-create-animation/SKILL.md:224`） |
| 任何 GUI 类型 | Core 零 GUI 引用。`InterpolatorCore`、`StateCore`、`TransitionHostBase`、`TransitionInterpreterCore` 都是**抽象基类**，平台差异全在适配器 |
| 反向播放 | 时间只向前。`SetRate` 拒绝负数（`TimeSourceCore.cs:287`），要回退用 `Seek` |
| 「Execute 返回即完成」 | 不成立。`CoreExecute` 是 `async void`（`Transition.cs:329`），返回时什么都没跑完 |
| 死锁/异常传播到调用方 | 不成立。回调、sampler、宿主抛出的异常都被 `TransitionDiagnostics` 收口，走 `Error` 事件 + 正常取消路径（`TransitionInterpreter.cs:210-219`） |

---

## 二、分层与依赖方向

```
用户代码 / Skills
   │  只触碰  VeloxDev.TransitionSystem.Transition<T>   ← 每家适配器各有一个
   ▼
适配器 (Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/)
   │  继承 Abstractions 的基类，实现契约
   ▼
VeloxDev.Core / TransitionSystem  (Abstractions)
   │  依赖 ↓，不依赖 ↑
   ▼
Timing (ITimeSource/ITimeSourceControl) · Threading (IThreadDispatcher/ThreadRef)
TimeLine (TransitionEventArgs) · WeakTypes (WeakDelegate) · Lifetime (ApplicationState)
```

**方向不可逆的理由（代码里写明的）：**

- `TransitionInterpreter.cs:142-151` 注明：**等待的精度不是正确性的一部分**——每趟都锚在时间轴的绝对位置上，唤醒早了晚了都画对帧。所以「把 loop 钉在 UI 线程上」属于拥有 loop 的子系统（`FramePacerCore`），不属于 `Timing`（`TimerCore.cs:21-25` 明说）。
- `InterpolatorCore.CreateScheduler`（`Interpolator.cs:120`）返回 `null` 是**合法答案**，含义是「这个平台没接入」或「这个 effect 不属于我」。Core 无法从 `object target` 推出用哪家的 host/priority —— 这正是这条缝存在的唯一理由。

**用户可见面与内部面。** `VeloxDev.TransitionSystem` 命名空间（`Transition<T>`、`TransitionEffect`、`State`、`Interpolator`、`TransitionScheduler`、`Eases`、`PathIndex`、`RotationDirection`、`ISampler`、`ISampleable`、`IEaseCalculator`、`ITransitionProperty`、`IFrameState`、`TransitionCoreEx` 等）是给用户的；`VeloxDev.TransitionSystem.Abstractions`（`TransitionCore`、`InterpolatorCore`、`StateCore`、`TransitionSchedulerCore`、`TransitionInterpreterCore`、`TransitionHostBase`、`SamplerSet`、`TransitionRun`、`TransitionDiagnostics`）是给适配器的。

用户命名空间里没有 scheduler 的内部件：`TransitionScheduler`、`Transition<T>`、`TransitionEffect`、`State`、`Interpolator`、`UIThreadInspector` 都是**每家适配器自己**在 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/` 下定义的（Core 里没有同名的具体类型）。Core 侧用户命名空间里能直接用的只有 `TransitionCoreEx`（`Then`/`Await`/`AwaitThen`/`Repeat`/`Interpolator` 这几个链式扩展方法，`TransitionEx.cs:6`）与各接口/枚举/`PathIndex`/`Eases`。

---

## 三、一次动画的完整流向

有**两个入口**，它们最终汇到同一个地方。

### 入口 A：声明式 —— `Transition<T>.Execute(target)`

`StateSnapshotCore.Execute`（`StateSnapshot.cs:17`）→ `CoreValidate()` → `CoreExecute()`。

`CoreValidate` 在 `CoreExecute` **之外**调用是刻意的（`StateSnapshot.cs:19`）：`CoreExecute` 是 `async void`，从它里面抛出去会逃到 `SynchronizationContext` 而不是调用方。校验只做一件事——`TransitionCore.RejectUnsampleablePaths`（`Transition.cs:205`）**逐段走链**，把「叶子是引用类型且没有 sampler」的路径判为永不可动画，抛 `TransitionPathUnsampleableException`。

`ExecuteCoreAsync`（`Transition.cs:354`）顺序：

1. `root ??= this`；`target is T` 检查。
2. 取 **target 锁** `TransitionSchedulerCore.GetTargetLock(target)`（`TransitionScheduler.cs:235`，`ConditionalWeakTable<object, SemaphoreSlim>`）。
3. `FindOrCreate(target, CanMutualTask)`（`TransitionScheduler.cs:189`）。
4. **互斥**时 `DrainActive()` 掉该 scheduler 上已有的一切（`superseded`），在锁内、在注册自己之前 —— 所以它不会取消自己。
5. `new TransitionRun(timeline ?? TimerCore.CreateTimeSource<ITimeSourceControl>())`；`cts = run.Cts`。
6. `Track(run)` —— **注册覆盖整条链**，不只是当前段。
7. 非互斥时 `AddNoMutual(target, [scheduler])`。
8. 释放锁，`CancelDrained(superseded)`（锁外，见 §五）。
9. 把链拍平成 `segments[]`（interpolator / delay / `effect.Clone()` / **state 不克隆**）与 `repeats[]`（`Transition.cs:408-419`）。
10. `RunRangeAsync(1, n)` → `RunBodyAsync` → `RunSegmentAsync`（`Transition.cs:427-473`）：先 `DelayWhilePausedAsync(delay)`，再
    - 该段**首趟**：`ExecuteCapturing(...)`，把备好的 `SamplerSet` 存进 `prepared[i]`；
    - 该段**之后每一趟**：`Replay(prepared[i], effect, cts)` —— 不重读 target、不重发 `Awake`（`TransitionScheduler.cs:50-58`）。
11. `finally`：`Untrack(run)` + 非互斥 `RemoveNoMutual` + `run.Dispose()`。这三件事**只在整条链结束时**做（`Transition.cs:479-492`）。

### 入口 B：命令式 —— 主题切换

`DynamicTheme/ThemeManager.RunSwitch`（`ThemeManager.cs:200`）不经过 `Transition<T>`：它自己 `interpolator.CreateScheduler(target, effect)` → `scheduler.Track(run)` → `scheduler.Execute(interpolator, state, effect, run.Cts)`，然后 `Task.WhenAll` 等所有目标。

它存在的意义是反面证据：**scheduler 的公开面足够驱动一次动画**，`Transition<T>` 只是它的语法糖 + 链编排。也说明「整场共用一条时间轴」是靠给所有 run 传同一个 `ITimeSourceControl` 实现的（`ThemeManager.cs:236`）。

### 调度器内部（每个段一次）

`TransitionSchedulerCore<...>.ExecuteCore`（`TransitionScheduler.cs:96`）：

1. `host.ThreadFor(target)` —— **只在这里解析一次**，仍同步跑在启动这趟的线程上。这是唯一拿得到正确答案的时刻：帧是从采样循环的线程投出去的，那里没有调用方上下文（Razor circuit 的 renderer 属于 circuit 而非进程）。
2. 取 scheduler 自己的 `_gate`（每 scheduler 一把，与 target 锁**不同**）。
3. `if (generation != Generation) return null` —— 排队期间 `Exit()` 跑过了，这段没开始就被取消。
4. `new TransitionDiagnostics(effect, target, newInterpreter.Args)`。
5. `await host.PostAsync(target, () => { if (cts.IsCancellationRequested) return; effect.InvokeAwake(target, Args); }, effect.Priority)` —— **必须 await**：`Awake` 可以靠 `Args.Handled` 否决，也可以把 target 摆成动画该开始的姿态，`Prepare` 在它跑完前不得读 target。`awoken == false` 表示宿主队列已消失 → 放弃（`TransitionScheduler.cs:146`）。
6. `producer.Prepare(target, state, effect, host)` → `SamplerSet`。抛异常只结束这一趟（`diagnostics.Error("Prepare", ...)`）。
7. `_activeRuns.TryGetValue(newCts, out run)` → `run.Thread = thread; frameSet.SetRun(run)`。找不到就保留 frameSet 自己的私有 run（测试直接驱动 scheduler 的情形）。
8. `newInterpreter.Execute(target, frameSet, effect, cts)`。

### 解释器内部（一次采样循环 = 一段的一趟）

`ExecuteSamplingLoopAsync`（`TransitionInterpreter.cs:157`）：

- `frameSet.SetCancellation(cts)`；`startCycle = run.Cycle`（**每段按自己的起点计**，否则链里第二段一上来计数就越过 `LoopTime`，报 Start/Completed 却一帧不写，见 `TransitionInterpreter.cs:170-172`）。
- `_pacer ??= CreateFramePacer(target, frameSet.Host)` —— 解析发生在**第一个 await 之前**，必须仍在启动线程上（Avalonia 的 `DispatcherTimer`、WinForms 的 `Timer` 只能在创建线程上 tick）。`_pacerResolved` 是必要的，因为 `null` 是有意义的答案（「问过，答案是不」）。
- `Start` → `while(true)`：`run.Cycle - startCycle > effect.LoopTime` 则 break → `RunPassAsync(forward: true)`，`IsAutoReverse` 再来一趟 `forward: false` → `run.NextCycle()`。
- `Completed`；`OperationCanceledException` → `Canceled`；其它异常 → `Error("Run")` + `Canceled`；`finally` → `Finally` + `ReleaseLoopResources()`（**嵌套 try**，回调抛异常不能带走 loop 自己的资源）。

`RunPassAsync`（`TransitionInterpreter.cs:270`）：

- `run.PassAnchor = timeline.Ticks` —— 锚点而非重置，所以多个动画能共用一条时间轴（`Transition.cs:104` 的 Seek 就是把 `PassAnchor` 换一个值）。
- `!timeline.IsAdvancing` → **先画一帧**再 `await timeline.WaitWhileStalledAsync(cts.Token)` 停车。先画是为了让「暂停中的 seek」可见。用 `IsAdvancing` 而不是 `IsPaused`：rate 为 0 会冻结时间轴但不是暂停。
- 否则 `elapsedTicks = timeline.Ticks - run.PassAnchor` → `EmitFrame` → 到达末端就 return。
- `await new FrameWait(this, 1000/FPS ms, cts.Token)` —— yield；**时间轴才是权威**，FPS 是采样率上限不是帧栅格（`TransitionInterpreter.cs:310-313`）。

`EmitFrame`（`TransitionInterpreter.cs:329`）：

- `elapsedMs` 下限 0；`rawT = durationMs <= 0 ? 1 : elapsedMs / durationMs`。
- `rawT >= 1` → `easedT = forward ? 1 : 0`，**精确端点，不依赖 `Ease(1) == 1`**。
- 否则 `easedT = effect.Ease.Ease(forward ? rawT : 1 - rawT)`，**刻意不 clamp**：Back/Elastic 靠离开 [0,1] 定义。
- `Update` → `apply(easedT)`（即 `frameSet.Apply`）→ `LateUpdate`，三者中任一抛异常 → 整帧作废 + 结束这一趟。

### 写路径（唯一允许的）

`SamplerSet.Apply(target, t, priority)`（`SamplerSet.cs:100`）：

1. `_cts.IsCancellationRequested` 或 `!CanSetValue()`（宿主已死）→ **直接返回**（stale-frame 守卫）。
2. 用 `_cachedTarget` 缓存一个 `Action`，时间经 `Interlocked` 字段传递 —— 每帧零闭包分配。
3. `thread = run.Thread`（调度器钉好的）→ 否则 `_host.ThreadFor(target)`（测试直接驱动解释器时）。
4. `_host.Post(target, thread, _cachedApply, priority)`；返回 false 只报一次 `Warn("Dropped")`。
5. 真正落地的 `ApplyCore` **再检查一次**取消（消息在 UI 线程被泵出时，动画可能已经被取消），然后逐条 `entry.Sampler.InsertFrame(target, property, ref entry.Working, Start, End, Options, t)`。
6. 任一 sampler 抛异常 → `diagnostics.Error("Sampling")` + `CancelQuietly()` + 结束这一趟。

读路径是另一条：`InterpolatorCore.Prepare` 用 `host.Run<object?>(target, () => bound.GetValue(target))`（`Interpolator.cs:147`）——**同步阻塞的编组读**；写路径是 fire-and-forget 的 `Post`。这个不对称是全模块最重要的一个设计点。

---

## 四、核心类型的职责边界

| 类型 | 拥有什么状态 | 谁可以改 | 备注 |
|---|---|---|---|
| `TransitionCore<T,...>`（`Transition.cs:258`） | 声明期状态：`state` / `effect` / `interpolator` / `next` / `RepeatTime` / `delay` | 只在声明时（`Create()` 后的链式调用） | **一个声明可被多个 target 同时 Execute**，只读。`Root` 由 `AsRoot()` 在 `Create()` 里定 |
| `StateCore`（`State.cs`） | 三个字典：`Values` / `Interpolators` / `Options` | `Set*` 方法 | 路径冲突检查**只在 `SetValue`**（`State.cs:113`），`SetInterpolator`/`SetOptions` 不做；只覆盖**同一个** transition 内的路径 |
| `TransitionProperty`（`TransitionProperty.cs`） | 路径身份（`PathSegment[]`）+ 惰性编译的 getter/setter | 惰性、幂等 | **`Path` 文本不是身份，`Equals`/`GetHashCode` 才是**。`UnreadablePath` 哨兵（:95）区分「中间对象为 null」与「类型不符」 |
| `InterpolatorCore`（`Interpolator.cs`） | 进程级 **static** sampler 注册表 | `RegisterInterpolator` / `UnregisterInterpolator` | 静态构造函数装 Core 默认（double/float/int/long/Point/PointF/Size/SizeF/Color/Rectangle/RectangleF + `!NETSTANDARD2_0` 的 Vector2/3/4、Quaternion）。实例成员 `Prepare` / `CreateScheduler` 是**适配器接缝** |
| `SamplerSet<TPriorityCore>`（`SamplerSet.cs`） | 一段**首趟**备下的端点快照 + 每 property 的 `Working` scratch | `Add` 只在 Prepare 里 | 段的首趟建一次，之后每次迭代 `Replay` 复用**同一个实例** |
| `TransitionEffectCore`（`TransitionEffect.cs`） | 配置（FPS/Duration/IsAutoReverse/LoopTime/Ease/Priority）+ 9 个 `WeakDelegate` 事件 | 声明时 | **运行的不是你写的那个对象**：每段 `Clone()` 一份。`TransitionEffectCore<TPriorityCore>.Clone()` 返回**基类型**实例（见 §七陷阱） |
| `TransitionInterpreterCore`（`TransitionInterpreter.cs`） | 一次采样的循环 + `Args` + `_pacer` + `_wait` | 每段 / 每次重放 `new()` 一个 | `Args` 因此是**每段一个**，`Handled` 不跨段 |
| `TransitionSchedulerCore`（`TransitionScheduler.cs`） | `_activeRuns`（`ConcurrentDictionary<cts, run>`）、`_gate`、`_generation`、`targetref` | `Track`/`Untrack`/`DrainActive` 是仅有的写者 | 互斥 scheduler 每 target **缓存一生**；非互斥每次 `Execute` 新建 |
| `TransitionRun`（`TransitionRun.cs`，internal） | `Cts` + `Timeline` + `PassAnchor` + `Cycle` + `Thread` | anchor/cycle 由 loop 与 Seek 改；Thread 由调度器钉一次 | `IDisposable` 只释放 Cts，**由 `TransitionCore` 在链尾做** |
| `FramePacerCore`（`FramePacerCore.cs`） | `_pending` 一个续体 | `Schedule`/`Fire` | 基类用 `IDisposable` 而非接口，就是为了把「恰好回调一次」的簿记收在一处 |
| `TimeSourceCore`（Timing） | 时钟状态（anchor/speed/rate/paused/parkGate/epoch） | `Pause`/`Resume`/`SetRate`/`Seek`/`Wake` + 宿主的 `SetHostFeeding` | 写者由 `_writeGate` 串行；读者无锁（sequence counter） |
| `TransitionHostBase`/`ThreadDispatcherBase` | 线程归属 + 编组 + 存活 | 平台 | `Post` 是 inline 还是 queue 由 `IsCurrentFor` 决定，Core 不再问 |

**「唯一写者」清单（值得单独记住的）：**

- 属性值的唯一写者：`ISampler.InsertFrame`，且只经由 `SamplerSet.ApplyCore`。
- `SamplerSet.Run` 的唯一写者：调度器（`SetRun`）。
- `_activeRuns` 的唯一写者：`TransitionSchedulerCore.Track`/`Untrack`/`DrainActive`。
- `run.Cycle` 的写者：循环的 `NextCycle()` 与 `Transition.Seek(target, cycle, ...)`。
- `run.PassAnchor` 的写者：`RunPassAsync`（每趟起点）与 `Transition.Seek`。
- `run.Thread` 的写者：调度器（`ExecuteCoreAsync` 里，一次）。
- 时间唯一权威：`run.Timeline`。**没有第二条计时路径**。

---

## 五、不变量

**A. 顺序类**

1. `Track` 必须早于 `Execute`。调度器靠 `_activeRuns.TryGetValue(newCts)` 找回 run；找不到时 frameSet 会拿一条**没人控制得住的私有时间轴**（`SamplerSet.cs:78` 惰性 `new TransitionRun(...)`）。`ThemeManager.cs:248-251` 的注释把这条写死了。
2. `SetRun`/`SetCancellation` 必须早于第一次 `Apply`，否则 stale-frame 守卫与线程钉定都失效。
3. `Awake` → `Prepare` 顺序不可换，且 `Awake` 必须 **await**（`PostAsync` 只在动作被真正接受时才等，`ThreadDispatcherBase.cs:64`）。
4. `Start` → 帧 → `Completed`/`Canceled` → `Finally`。`Finally` 与 `ReleaseLoopResources` 在嵌套 `finally` 里，回调抛异常也要放资源。
5. 末帧必须是精确端点：正向 `1`、反向 `0`，由 `EmitFrame` 直接给，不经 `Ease`。

**B. 成对类**

6. `Track` ↔ `Untrack`：**整条链一对**，不是每段一对。按段 Untrack 会在动画还活在段间 `Await` 间隙时把它从注册表摘掉，`Exit()` 就再也找不到它（`Transition.cs:481-483`）。
7. `AddNoMutual` ↔ `RemoveNoMutual`：同上，只在链结束。
8. `DrainActive()`（记账，**必须在 target 锁内**）↔ `CancelDrained(...)`（真的 Cancel，**必须在锁外**）。不可合并：`CancellationTokenSource.Cancel()` 在调用线程上**同步**跑已注册回调，锁内做会在 UI 线程的 `Exit` 上阻塞 dispatcher，并且会死在「回调重入 `Exit`/`CoreExecute`」上（`SemaphoreSlim` 不可重入，`TransitionScheduler.cs:339-346`）。
9. `FramePacerCore.Schedule` ↔ 恰好一次续体回调。**恰好一次**：多一次则双采样，少一次则 loop 永久停车（无异常、无帧）。`Dispose` 也必须放行挂着的续体（`FramePacerCore.cs:102`）。
10. `_pacer` 的创建 ↔ `ReleaseLoopResources`（loop 结束时）与 `Dispose`（两端都要覆盖）。
11. 非互斥 scheduler 的注册 ↔ `NoMutualSchedulers` 里那条表项；**表项本身不随动画结束移除**（`Transition.cs:233-236` 明说这是为了不与并发 add 抢），所以 `TryGetNoMutualScheduler` 返回 `true` 但数组为空是正常的 —— 要读**数组长度**，不要读 bool（`Examples/Transition/Avalonia/Demo/Views/MainWindow.axaml.cs:803`）。

**C. 取消/生命周期类**

12. `Exit` 只在 token 上发信号 + `Wake()` 时间轴；**不**跳到终点。写回声明起点是调用方的事。
13. `CancelDrained` 里必须 `run.Timeline.Wake()`：暂停中的动画停在时间轴的 gate 上，它不知道 token；不唤醒的话 `Exit` 会在「它本该停掉的循环还在等」时返回（`TransitionScheduler.cs:378-383`）。
14. `_generation` 在每次 `Exit` 自增；在 `_gate` 上排队的动画对比自己捕获的 generation，变了就放弃 —— 否则它会在 `Exit` 之后启动且再也不能被停掉。
15. 解释器**不取消 token source**（`ReleaseLoopResources` 只放 pacer 和 wait）。一个调用方可以把同一个 cts 交给好几段（`Transition` 就是这样），在出口取消它会让动画在第一段后就结束（`TransitionInterpreter.cs:394-403`）。
16. `TransitionRun.Dispose` 在**最后一个读者之后**（链尾），因为释放后 `Cancel`/`Register` 会抛，只有 `IsCancellationRequested` 还回答 —— 这正是让迟到的 drain 安静、让迟到的帧仍看到「它是在哪个取消下被投出去的」。

**D. 值语义类**

17. `ISampler` 实现必须是**无状态单例**：注册表里的实例进程级共享；每动画的 scratch 只能放 `ref object? working`（`ISampler.cs:31-36`）。
18. `InsertFrame` 绝不能改 `start`/`end`：它们与快照共享，改了会污染快照（`ISampler.cs:6-10`）。
19. `Apply` 的 t 是**缓动后**的值（可能越界），sampler 自己决定外插（数值型）还是钉端点。**这个不一致项目自认未完成**，不要当成契约读（`skills/veloxdev-create-animation/SKILL.md:170`）。
20. 单条 transition 内，「一个对象只能由一条路径表达」：`StateCore.SetValue` 里 `IsDescendantOf` 双向检查，抛 `TransitionPathConflictException`（`State.cs:113`）。两条不同 transition 之间的冲突**不检测**。

**E. 反直觉（实测/注释记载）**

21. **零时长 + 循环会死转。** `durationMs <= 0` 时 `rawT` 直接为 1，`RunPassAsync` 一次 `EmitFrame` 就 return，全程**没有 await**，`while(true)` 变成不可让出的紧循环；停在 UI 线程上就连 `Exit` 也调不进来（`skills/veloxdev-create-animation/SKILL.md:220`；代码依据 `TransitionInterpreter.cs:340`、`:197`）。
22. **`EmitFrame` 里 `Update` 抛异常 = 整帧作废**（后面的 `apply` 与 `LateUpdate` 都不跑），且结束这一趟 —— 「半坏的动画不该继续以帧率出错」。
23. **`TransitionDiagnostics` 每个 stage 每个实例最多报一次**（`TransitionDiagnostics.cs:7`）——它们大多是每帧都会发生的事。
24. **`Prepare` 阶段逐属性的 `Warn` 不能否决动画。** `InterpolatorCore.Prepare` 自己 new 的 diagnostics 没传 `run` 参数（`Interpolator.cs:140`），而 `TransitionDiagnostics.Raise` 只在 `run is not null` 时才把 `Handled` 落下去（`TransitionDiagnostics.cs:44`）。而 `Awake`/`Prepare`/`Dropped` 用的那份（调度器创建的）传了，所以在那些 stage 里 `Handled` 是否决。**同一件事在两个 stage 行为不同**，这是观察所得，没有注释说明是有意的。
25. **互斥 scheduler 一生不释放。** `MutualSchedulers` 是 `ConditionalWeakTable`，`RemoveMutualScheduler` 在 `Src/` 里**没有任何调用者**。所以「这目标跑过互斥动画」是永真的。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 采样的时间语义（何时算一帧、缓动怎么施加、端点是否精确、循环/自动反向怎么推进） | `TransitionSystem/TransitionInterpreter.cs` |
| 控制面（暂停/调速/Seek/查询/退出语义、target 锁、generation） | `TransitionSystem/Transition.cs`（`TransitionCore` 静态方法）+ `TransitionSystem/TransitionScheduler.cs`（`_activeRuns`/`DrainActive`） |
| 链与段（`Then`/`Await`/`AwaitThen`/`Repeat` 的语义、段间等待、帧集的重用与重放） | `TransitionSystem/Transition.cs:408-533`（`ExecuteCoreAsync` 的后半）与 `Transition.cs:559`（`CoreRepeat`） |
| 端点怎么被读出来、路径怎么被解析/编译、哪些路径要拒绝 | `TransitionSystem/Interpolator.cs`（`Prepare`）、`TransitionSystem/TransitionProperty.cs`、`TransitionSystem/PathSegment.cs` |
| 每帧写出去了什么、marshaling、stale-frame 守卫 | `TransitionSystem/SamplerSet.cs` |
| 一个具体类型的插值数学 | 对应 `ISampler`：Core 的在 `TransitionSystem/NativeSamplers/`，平台的在各家 `PlatformAdapters/Samplers/` |
| 颜色的边界/溢出去哪了 | `TransitionSystem/BoundedProgress.cs` |
| 缓动曲线 | `TransitionSystem/Eases.cs` |
| 「下一帧什么时候来」/宿主定时器 | `TransitionSystem/FramePacerCore.cs` + 各家的 `TransitionInterpreter.CreateFramePacer` |
| 时钟（暂停/倍速/锚点/epoch/park 信号） | `Timing/TimeSourceCore.cs` |
| 线程归属与编组 | `Threading/ThreadDispatcherBase.cs` + 各家的 `UIThreadInspector` |
| 错误的可见性（哪些 stage、是否已报过） | `TransitionSystem/TransitionDiagnostics.cs` |
| 事件参数（`Handled`/`Stage`/`Message`） | `TimeLine/TimeLineEventArgs.cs`、`TimeLine/TransitionEventArgs.cs` |
| 「主题切换为什么在这个平台不animate」 | `DynamicTheme/ThemeManager.cs:200` + 该家的 `Interpolator.CreateScheduler` |

---

## 七、陷阱（带依据）

1. **`effect.Clone()` 落到基类型。** `TransitionEffectCore<TPriorityCore>.Clone()` 里 `new TransitionEffectCore<TPriorityCore>()`（`TransitionEffect.cs:16`），不是适配器的 `TransitionEffect`。它**不是 virtual**（是 `new`），所以平台**无法**把子类新增的状态带进运行——因为运行的是克隆体。结论：给 `TransitionEffect` 子类加字段是无效的。目前七家的 `TransitionEffect` 都只覆写 `Priority`，所以没暴露问题。
2. **共享静态 effect 会被直接赋值。** `.Effect(TransitionEffects.Theme)`（各家 `PlatformAdapters/TransitionEffects.cs` 提供的现成实例，`skills/veloxdev-switch-themes/SKILL.md:104-108`）走的是 `Transition.cs:579-591` 那个**直接赋值**分支（`this.effect = convertedEffect`，:589），而运行体是每段克隆 —— 往共享 effect 上加 handler 会作用于它的**所有**使用者，且每帧每目标都触发。想挂平台帧钩子要用 `.Effect(e => {...})` 那个 setter 重载：它在 `Transition.cs:598` 处 `new` 一个实例。
3. **`state` 跨段与跨迭代共享**（`Transition.cs:414-415` 只克隆 effect），所以「一段的首趟备下的帧集被后续迭代复用」才成立；也意味着运行期改 `state` 是竞态。
4. **`Execute` 返回时什么都没跑完**，且 `Transition<T>.Execute` 是 `async void` —— 想等它，只能读 target 的真实值或用 `AwaitThen` 串接。
5. **`Transition.Exit` 默认只停互斥动画**（`IncludeMutual: true, IncludeNoMutual: false`，`Transition.cs:30`）。`CanMutualTask: false` 启动的那些不会被默认的 `Exit` 碰到。
6. **`ISampler` 的注册表查找会向上走**（精确类型 → 基类由近及远 → 接口按名字序，`Interpolator.cs:50-87`）。所以注册 `Brush` 意味着你的 sampler 会收到 `LinearGradientBrush`；「注册了基类型」等于「承诺处理整个家族」。
7. **`TransitionProperty.UnreadablePath` 是「路径不适用」而非「值为 null」**。`GetValue` 返回它时 `Prepare` 会跳过该属性并 `Warn("Unreadable")`；把它当 null 参与插值会扭曲结果（`TransitionProperty.cs:88-95`、`Interpolator.cs:150`）。
8. **索引越界是静默的**：`GuardIndexExceptions` 把 `IndexOutOfRangeException`/`ArgumentOutOfRangeException`/`KeyNotFoundException` 都吞成哨兵（`TransitionProperty.cs:454`）。写入不解析就什么都不做。
9. **可访问性不参与判定**：`private set` / `internal` 成员照常动画（`TransitionProperty.cs:18-22`）。所以「加一条路径却什么都没发生」通常不是可见性问题，而是没有 sampler / 索引失效 / 被 `Repeat` 的语义绕过了。**注意「没有 sampler」在值类型上是完全不报错的**：`RejectUnsampleablePaths` 豁免一切值类型，而 `ISampleable` 只覆盖实现了它的 struct —— `decimal`（六家的重载表都有）、未注册的平台 struct（如 `Avalonia.Rect`）都从这道门漏过去，静默跳过。见 `extension.md` §二·2b。
10. **`Repeat` 的环包到写它的那一段**，且按段嵌套，`RepeatTime` 是**额外**次数（`Transition.cs:276-287`）。只有最后一段上的计数等于「整条链重复」。
11. **`FramePacerCore` 的时间间隔是要重新读的**（`effect.FPS` 可以在动画中途改，`RunPassAsync` 每帧重算 `1000.0 / Max(1, FPS)`，`TransitionInterpreter.cs:313`）。
