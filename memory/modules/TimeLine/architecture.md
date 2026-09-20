# TimeLine 架构

> 代码：`Src/Core/VeloxDev.Core/TimeLine/`（6 个 .cs）+ 契约 `Src/Core/VeloxDev.Core/Interfaces/MonoBehaviour/`。
> 依赖：`Timing/`（总线与采样器）、`TransitionSystem/`（`TransitionEventArgs` 的消费方）。
> 采样循环与帧节奏归 `TransitionSystem`（`FramePacerCore`）；本文写的是**时间轴与事件参数本身**。

本文只写「读完这 6 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个目录其实是两个不相干的东西

`TimeLine/` 装了两样东西，唯一的交集是 `FrameEventArgs : TimeLineEventArgs` 这一条继承：

| | 是什么 | 谁在用 | 依据 |
|---|---|---|---|
| **A. 事件参数族** | 四个 `EventArgs` 类型，跨模块共用的「回调参数」形状 | `TransitionSystem`（`TransitionEventArgs`）与 `MonoBehaviourManager`（`FrameEventArgs`） | `TransitionEventArgs.cs`、`TransitionDiagnostics.cs:20,35` |
| **B. MonoBehaviour 式帧循环** | 命名 channel、两条后台泵、生命周期钩子 | Unity 式宿主；Core 内唯一消费者是 `WorkflowSystem/Templates/Helpers/TreeHelper.cs` | `MonoBehaviourManager.cs`、`TreeHelper.cs:30,56-64` |

**不解决什么：**

| 你以为在这里 | 其实在哪 |
|---|---|
| 七家 GUI 适配器的帧循环 | 不在这里。七家的定时器由各自的 `TransitionInterpreter.CreateFramePacer` 建立（`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`） |
| 动画的采样节奏 / FPS 上限 | `TransitionSystem/FramePacerCore.cs` + `TransitionInterpreter.RunPassAsync` |
| 时间本身（暂停/倍速/锚点/epoch） | `Timing/TimeSourceCore.cs`。本模块只是**持有**一条总线并用它 |
| UI 线程编组 | `Threading/IThreadDispatcher` + 各家 `UIThreadInspector`。这里的线程是**自己起的后台线程** |
| 序列化 / 场景图 | 没有这个概念。行为靠 `RuntimeHelpers.GetHashCode` 在字典里认（`MonoBehaviourManager.cs:796`） |

---

## 二、事件参数族：四个类型，各有各的读者

| 类型 | 谁产生 | 谁读 | 状态 |
|---|---|---|---|
| `TimeLineEventArgs`（`TimeLineEventArgs.cs`） | 抽象基类 | — | `virtual bool Handled`，**全仓库零 `override`**（grep `override bool Handled` 在 `Src/`+`Examples/` 下无命中） |
| `FrameEventArgs`（`FrameEventArgs.cs`） | `MonoBehaviourManager.CreateFrameEventArgs`（`:825`） | 五个 `partial void` 钩子 | 四个字段全 `internal set`，外部只能读 |
| `ThreadSafeFrameEventArgs`（`ThreadSafeFrameEventArgs.cs`） | **没有人** | **没有人** | 见 §九·5 |
| `TransitionEventArgs`（`TransitionEventArgs.cs`） | `TransitionDiagnostics`（`:20`、`:35`）；另有一处 `Transition.cs:340` | 用户挂在 effect 上的 `Error`/`Warn` 处理器 | `sealed`，`Stage`/`Message`/`Exception` 全 `init` |

**`FrameEventArgs` 的四个字段各有来源，都不是随手填的：**

- `DeltaTime` / `TotalTime` 都来自一个 `TimeSample`（`MonoBehaviourManager.cs:538`）。注释写死了这条（`:819-823`）：速率是**时钟**施加的，不是这里事后缩放的；所以 `TotalTime` 不含停摆期。
- `TargetFPS` 是**配置值**（`:831` 读 `_targetFPS`），`CurrentFPS` 是**实测值**（`:830` 读 `_currentFPS`，由 `UpdatePerformanceStats` 用墙钟每秒更新一次，`:915-927`）。
- 所以「TargetFPS 和 CurrentFPS 不一样」是正常的，不是 bug。

**`TransitionEventArgs.Stage` 的取值是自由文本，不是一个枚举。** 代码里出现过的字面量（全部在 `Src/Core/VeloxDev.Core/TransitionSystem/` 下）：

| Stage | 出处 |
|---|---|
| `"Start"` / `"Completed"` / `"Canceled"` / `"Finally"` | `TransitionInterpreter.cs:189,208,212,227` |
| `"Update"` / `"LateUpdate"` | `TransitionInterpreter.cs:359,361`（`EmitFrame` 的每帧三连） |
| `"Run"` | `TransitionInterpreter.cs:217`（收口）+ `Transition.cs:342`（`async void` 的 catch） |
| `"Marshaling"` | `TransitionInterpreter.cs:260` |
| `"Prepare"` | `TransitionScheduler.cs:160` |
| `"Dropped"` | `TransitionScheduler.cs:148` 与 `SamplerSet.cs:117`（**同一个名字，两个不同的事**：Awake 投递被拒 / 帧被宿主拒收） |
| `"Sampling"` | `SamplerSet.cs:139` |
| `"Unreadable"` | `Interpolator.cs:152` |

消费者按 `Stage` 分发时要自己对齐字符串，编译器不帮你。`TransitionEventArgs.cs:5` 的 XML 注释里只举了 `"Update"`、`"Finally"`、`"Sampling"` 三个，别把它当成全集。

---

## 三、`Handled`：一个属性名，两条互不相干的链路

这是本模块**最承重**的一点：`Handled` 是一个跨模块 ABI，同一个属性名在两个子系统里含义不同，且两边的读写者完全不同。

### 链路 A —— MonoBehaviourManager：终止本帧这个阶段剩下的行为

- **写**：钩子自己 `e.Handled = true`。
- **读**：三个 `ExecuteBehaviors*Sync` 的循环体**第一句**：`if (frameArgs.Handled || token.IsCancellationRequested) break;`（`MonoBehaviourManager.cs:695`、`:711`、`:727`）。
- **含义**：本帧、本阶段、**剩余**的行为都不跑。已经跑过的不回滚。
- **重置点**：只在 `CreateFrameEventArgs` 里（`:832`），即**每帧一次**。

### 链路 B —— TransitionSystem：取消这一趟动画

- **写**：`TransitionDiagnostics.Raise`（`TransitionDiagnostics.cs:44`）：`if (args.Handled && run is not null) run.Handled = true;` —— 即 effect 的 `Error`/`Warn` 处理器把 `TransitionEventArgs.Handled` 置上，落到 `run.Handled`。
- **读**：`TransitionInterpreter` 三处：趟循环起点（`:199`）、自动反向的第二趟之前（`:203`）、**每帧**（`:289`）。任一为真就 `throw new OperationCanceledException()`，走正常取消路径。
- **`Args` 的生命周期**：每个解释器一个（`TransitionInterpreter.cs:58`），每个解释器只服务一段的一趟，所以 **`Handled` 不跨段**。

### 两条链路的三条实用结论

1. **`Handled` 在 Update 与 LateUpdate 之间不重置。** 同一个 `frameArgs` 对象先传给 `ExecuteBehaviorsUpdateSync` 再传给 `ExecuteBehaviorsLateUpdateSync`（`MonoBehaviourManager.cs:540-541`），中间没有清位。**在 Update 里置上 `Handled`，本帧的整个 LateUpdate 被跳掉**。WPF demo 把这个做成了一颗按钮并直接计数（`Examples/MonoBehaviour/WPF/Demo/MainWindow.Hooks.cs:99-104`，读数在 `MainWindow.xaml.cs:279`）。
2. **`FixedUpdate` 每步自建参数**（`MonoBehaviourManager.cs:486-493`、异步版 `:592-599` 各自 `CreateFrameEventArgs`），所以 `Handled` **不跨步**，也不与 Update 共享。demo 的状态行明说了这条（`MainWindow.xaml.cs:318-320`）。
3. **不是每个 `TransitionEventArgs` 都能否决。** `TransitionDiagnostics` 造的那份带 `run`，置 `Handled` 才落下去；而 `Transition.cs:340-345` 在 `async void CoreExecute` 的 catch 里造的那份**没有 run**——在那个处理器里置 `Handled` 什么都不发生（那时这一趟已经结束了）。

---

## 四、MonoBehaviour 式帧循环：一个 channel 是什么

**一个 channel = 一条时间总线 + 两个采样器 + 两条后台泵。** 没有全局单例状态，channel 之间完全隔离。

```
MonoBehaviourManager.<方法名>(channel)      ← 静态面，全部转发
        │  _channels.GetOrAdd(name, ...)   MonoBehaviourManager.cs:1020
        ▼
LoopChannel
   ├ _bus = TimerCore.CreateTimeSource<ITimeSourceControl>()   :117
   ├ _updateSampler : IUncompensatedTimeSampler                :164
   ├ _fixedSampler  : ICompensatingTimeSampler（步长显式 16ms） :165,167
   ├ UpdateLoop      → _updateSampler.Sample()      :510-555
   └ FixedUpdateLoop → _fixedSampler.Advance()      :447-508
```

**总线是 channel 的对外接口，不是内部字段。** `LoopChannel.Bus` 是 public（`:189`），静态面 `MonoBehaviourManager.Bus(channel)` 也是（`:1145`）——它的注释写死了用途：把它传给 `Transition<T>.Execute(target, timeline)` 就能让**动画与帧回调共享同一条时间轴**，一次 `Pause()` 同时冻住两者，rate 同时乘两者。这也是本模块与 `Timing` 的接缝所在。

**所以 `Pause`/`Resume`/`SetTimeScale` 都不在 channel 里实现：**

| 调用 | 实现 | 依据 |
|---|---|---|
| `Pause()` | `_bus.Pause()`，前置守卫 `!_isRunning || _bus.IsPaused` | `:385-390` |
| `Resume()` | `_bus.Resume()`，同一守卫的镜像 | `:398-403` |
| `SetTimeScale(x)` | `_bus.SetRate(x)` **逐字转发**（`=>` 表达式体） | `:239` |
| `TogglePause()` | 看 `_bus.IsPaused` 选边 | `:441` |

**两条泵的形状（同步版；`UseAsyncLoop` 有同构的 async 版）：**

| | Update 泵（`:510-555`） | FixedUpdate 泵（`:447-508`） |
|---|---|---|
| 停摆时 | `WaitWhileStalledAsync(token).GetAwaiter().GetResult()`，`continue`（`:469-473`） | 同左（`:521-525`） |
| 采样 | `_updateSampler.Sample()`，`Delta == TimeSpan.Zero` 就睡 1ms 重来（`:531-536`） | `_fixedSampler.Advance(out sample)`，一次可能欠多步，**循环推完**（`:477-495`） |
| 每步的 `TotalTime` | 采样器给的 | `(firstStep + i) * stepTicks`——**每步各自的步序号乘步长**，不是把最后一次读数重复 N 遍（`:480-488`） |
| 参数对象 | 一个 `frameArgs`，Update 与 LateUpdate 共用，用完还池（`:538-543`） | 每步一个，`ExecuteBehaviorsFixedUpdateSync` 后**立刻**还池（`:493`） |
| 尾睡 | `FrameRateControlSync`（墙钟量，`:837-848`） | `_fixedSampler.TimeToNextStep`（`:498-500`） |

**为什么「停摆时 park 而不是轮询」是一条被写死的设计决定**：专用线程不能 `await`，所以同步版直接 `GetAwaiter().GetResult()` 阻塞一条自有线程，换零唤醒；取消能穿透这个等待是因为总线会观察令牌（`:466-468` 的注释原文）。`Sleep` 之所以按 `MAX_SLEEP_CHUNK_MS = 50` 分块睡（`:863-871`），是为了「一次停止在 50ms 内被察觉，而不是等完整个间隔」——最低目标帧率下一帧预算有一秒长。

**注册与生命周期全走队列，在更新循环的帧体里结算**（顺序即 `ProcessMainThreadOperations` 的四步，`:754-767`）：

```
_frameStartTime = GetTimestamp()
ProcessMainThreadOperations()
  ├ ≤64 个 _mainThreadQueue 动作   :758-762
  ├ ProcessConfigChanges()          :769-784   目标帧率：_targetFPS 与缓存时长同时改
  ├ ProcessAddedBehaviors()         :786-802   取 wrapper → InvokeAwake → InvokeStart → 入字典
  └ ProcessRemovedBehaviors()       :804-817   出字典 → wrapper.Clear → 还池
sample = _updateSampler.Sample()
frameArgs = CreateFrameEventArgs(...)
ExecuteBehaviorsUpdateSync / LateUpdateSync
```

由此得到三条不变量：

- **`RegisterBehaviour` 只入队**（`:431-434`），所以调用它的顺序与生命周期顺序无关；`Awake`/`Start` **一定跑在第一帧体之前**（它们在 `Sample()` 的上游），且都跑在**更新线程**上。
- **`Awake`/`Start` 各自的异常被 `SafeExecute` 吞掉**（`:940-943`，只 `Debug.WriteLine`）。所以「钩子里抛异常」= 静默半死。
- **配置队列由更新泵排空**，而固定泵的步长**不走配置队列**（`_pendingFixedIntervalMs`，`:222-226` + `:459-464`）。理由是写死的：配置队列归更新泵，采样器归固定泵，从更新线程写 `Step` 会与 `Advance` 争它要重置的累加器。

**数字（保留实测到的常量）：**

| 常量 | 值 | 行 |
|---|---|---|
| `DEFAULT_CHANNEL` | `"default"` | `:34` |
| `MIN_FPS` / `MAX_FPS` / `DEFAULT_TARGET_FPS` | `1` / `1000` / `60` | `:19-21` |
| `DEFAULT_FIXED_UPDATE_INTERVAL_MS` | `16` | `:22` |
| `MIN_UPDATE_INTERVAL_MS` / `MAX_UPDATE_INTERVAL_MS` | `1` / `1000` | `:28-29` |
| `MIN_SLEEP_MS` | `1` | `:13` |
| `MAX_SLEEP_CHUNK_MS` | `50` | `:32` |
| `RESTART_SHUTDOWN_TIMEOUT_MS` | `1000` | `:15` |
| `RESTART_QUEUE_CLEAR_TIMEOUT_MS` | `500` | `:16` |
| `DEFAULT_RESTART_CHECK_INTERVAL_MS` | `5` | `:14` |
| `THREAD_INACTIVITY_TIMEOUT_MS` | `2000` | `:17` |
| `DEFAULT_OBJECT_POOL_SIZE`（三个池共用） | `50` | `:24` |
| `MAX_CONFIG_CACHE_DURATION_MS` | `1000` | `:25` |
| 每帧主线程动作上限 | `64`（字面量，非具名常量） | `:758` |

**线程约定：** 名字 `VeloxDev.Update[{Name}]` / `VeloxDev.FixedUpdate[{Name}]`，`IsBackground = true`，`Priority = AboveNormal`（`:309-321`）。**两条都是专起的后台线程，不是线程池、不是 UI 线程、也不是 `SynchronizationContext` 上的。** 异步版（`UseAsyncLoop`）把「线程」换成 `Task`，其余语义不变（`:558-683`）。

---

## 五、边界：这不是七家 GUI 适配器的东西

**`Src/Adapters/VeloxDev.*/` 下没有任何一行引用 `MonoBehaviourManager`**，只有两处**注释**在解释「为什么这段代码会被非 UI 线程碰到」：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowMinimapOverlay.cs:381` 与 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowMinimapOverlay.cs:310`（后者点明了 `(via BroadcastVisibleItemLayout)`）。七家的帧循环各自由 `TransitionInterpreter.CreateFramePacer` 建立。

它给的是 **Unity 式宿主** —— 游戏循环、MonoBehaviour 集成、或任何想跑一条**独立于 UI 的帧循环**的非 UI 代码。判据很简单：你要的是「一条与渲染无关、能自己调速、暂停时不占 CPU 的帧泵」，就用它；你要的是「让动画在 UI 线程上出帧」，那是 `TransitionSystem` + 适配器的事。

**但 Core 内部有一个真实消费者**，容易被忽略：

`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/TreeHelper.cs` 的 `[MonoBehaviour(channel: nameof(TreeHelper), fps: 10)]`（`:30`），channel 在带 `cellSize` 的构造里 `Start`（`:43-46`），`Install` 里 `InitializeMonoBehaviour()`（`:123`），`Uninstall` 里 `CloseMonoBehaviour()`（`:140`），`partial void Update`（`:56-64`）做 `Virtualize` + `BroadcastVisibleItemLayout`。

**这条链的后果值得单独记住：虚拟化跑在一条 10fps 的后台线程上，而 `BroadcastVisibleItemLayout` 从那条线程对每个可见节点发 `OnPropertyChanged(nameof(Anchor))` / `(nameof(Size))`（`TreeHelper.cs:66-76`）。** 绑定层是否接受跨线程的属性变更由各 GUI 决定，本模块不做任何编组（对照：`TransitionSystem` 有一条完整的 `IThreadDispatcher` 通路）。也注意 `[MonoBehaviour]` 只装在泛型类 `TreeHelper<T>` 上（`:31`），而 `nameof(TreeHelper)` 在泛型内解析为不带参数个数的 `"TreeHelper"`。

---

## 六、不变量

**A. 顺序类**

1. **`InitializeMonoBehaviour()` 必须由用户代码自己调。** 生成器只**实现**它，不调它（`Src/Generators/VeloxDev.Core.Generator/Writers/MonoWriter.cs:81-84`）。只贴 `[MonoBehaviour]` 而不调 = 什么都没发生，且不报错。参照 `TreeHelper.cs:123` 与 `Examples/MonoBehaviour/WPF/Demo/MainWindow.xaml.cs:57`。
2. **`Awake` → `Start` → 第一帧体**：由 `ProcessAddedBehaviors`（`:786-802`）在 `Sample()` 上游保证，不需要用户排序。
3. **`Update` 一定早于 `LateUpdate`**，两者在同一次循环迭代里连续调用（`:540-541`）。demo 用一个必须为 0 的计数器守这条（`MainWindow.Hooks.cs:117-125`）。
4. **`SetUseAsyncLoop` / `ClearUseAsyncLoopOverride` 只能在 channel 未运行时调**，运行中抛 `InvalidOperationException`（`:250-252`、`:263-265`）。
5. **`CreateFrameEventArgs` 每次必须把 `Handled` 清回 false**（`:832`）。池化对象带着上一帧的值，漏清这一句就是「每帧都被 Handled」。

**B. 成对类**

6. `InitializeMonoBehaviour()` ↔ `CloseMonoBehaviour()`。`TreeHelper` 在 `Install`/`Uninstall` 里成对调（`:123`/`:140`），demo 用一对按钮演示（`MainWindow.xaml.cs:121-135`）。
7. `_frameEventArgsPool.Get()` ↔ `Return`，**三条路径都要还**：Update 循环（`:543`）、FixedUpdate 循环每一步（`:493`）、异步版同构（`:599`、`:655`）。漏还只是少一个复用对象，不报错。
8. `_isUpdateThreadActive = true` 在循环体首行 / `finally` 里置 false（`:512`/`:553`，固定泵 `:449`/`:506`），async 版同构（`:625`/`:681`）。
9. **`Start()` 与 `StopAsync()` 各自调 `_bus.Resume()`**（`:294`、`:340`）：启动要清掉上一个生命周期留下的暂停，否则「暂停中被停掉的 channel 重启后会立刻 park，再也跑不起来」（`:292-293`）；停止也不把暂停状态留给下一个生命周期（`:338-339`）。**注意这不解除 rate 为 0 的冻结**——那不是一个暂停。

**C. 反直觉/生命周期类**

10. **重新注册同一个行为会让 `Awake`/`Start` 再跑一遍。** `ProcessAddedBehaviors` 对新来的行为无条件 `InvokeAwake` + `InvokeStart`（`:797-798`），并 `_behaviors[hash] = wrapper` **覆盖**旧 wrapper（`:796`）——旧的那个还在泵的缓存数组里活着，直到下一次 `RebuildCachedWrappers`。所以「注册两次」= `Awake` 两次，而「停 channel 再启」**不重跑**（`:405-429` 只是 `StopAsync` → `Start`）。demo 专门做了这对按钮（`MainWindow.xaml.cs:111-135`）。
11. **`ExecutionOrder` 只是注册计数器。** `wrapper.Reset(behavior, Interlocked.Increment(ref _instanceCounter))`（`:794`），插入排序按它排（`:873-902`）。**用户无法指定行为执行顺序**，唯一的顺序就是注册顺序。
12. **`FrameEventArgs` 是池化对象，绝不能缓存跨帧使用。** 下一帧同一个对象会被改写成新值；FixedUpdate 的那份更激进，当场还池（`:493`）。
13. **`ExecuteOnMainThread` 的「Main」是 channel 自己的更新线程。** 它入 `_mainThreadQueue`（`:241`），由 `ProcessMainThreadOperations` 在更新泵里排空（`:758`）。名字容易读成 UI 线程 —— 不是。且仓库内**零调用者**（grep `Src/`+`Examples/` 只有定义与静态转发两处）。
14. **`Bus(channel)` 对从未创建的 channel 返回 `null`，不创建。** 注释写死：「查询不该有副作用地创建 channel」（`:1135-1144`）。而 `Start`/`Pause`/`SetTargetFPS` 等**全部走 `GetOrCreateChannel`**（`:1049-1102`）——所以「读状态」与「下命令」对 channel 存在性的影响不对称。
15. **`UseAsyncLoop` 的默认值按 TFM 分叉**：`NET5_0_OR_GREATER` 时是 `IsBrowser() || IsIOS()`，**否则是 `true`**（`:1007-1012`）。也就是 netstandard2.0 / netframework 目标**默认走异步模式**，桌面老框架上并没有原生线程泵。
16. **`IsUpdateThreadAlive` 把「停摆」也算活着**：`!_bus.IsAdvancing || IsRecentActivity(...)`（`:194-195`）。注释写死了理由——停摆中的循环 park 在总线上，「最近有没有活动」必然为假，不改会把一条好线程报成已死，`RestartAsync` 也会因此走 `ForceCleanup`（`:191-193`）。
17. **`SetTargetFPS` 越界静默丢弃**（`:206` 的 `if (fps < MIN_FPS || fps > MAX_FPS) return;`），不抛也不日志。合法值也只是**入队**，由更新泵在下一帧应用。

---

## 七、入口：我要改 X，先打开哪个文件

| 我想改 | 打开 |
|---|---|
| 帧体顺序 / 生命周期落点 | `TimeLine/MonoBehaviourManager.cs:754-767`（`ProcessMainThreadOperations`）与 `:510-555`（`UpdateLoop`） |
| Update 与 LateUpdate 的调用点、`Handled` 的检查 | `MonoBehaviourManager.cs:690-735`（三个 `ExecuteBehaviors*Sync`） |
| 「本帧传出去什么」 | `MonoBehaviourManager.cs:825-834`（`CreateFrameEventArgs`） |
| 目标帧率怎么生效 | `MonoBehaviourManager.cs:769-784`（`ProcessConfigChanges`）+ `:837-848`（`FrameRateControlSync`，墙钟） |
| FixedUpdate 的步长与补步 | `MonoBehaviourManager.cs:447-508` + `Timing/CompensatingTimeSampler.cs` |
| 暂停/倍速（channel 级） | `MonoBehaviourManager.cs:385-403`、`:239`，实现全在 `Timing/TimeSourceCore.cs` |
| 线程名字/优先级/是否用线程 | `MonoBehaviourManager.cs:302-325`、`:1007-1012` |
| 启动/停止/重启语义 | `MonoBehaviourManager.cs:275-328`、`:330-375`、`:405-429` |
| 注册为什么没生效 | `MonoBehaviourManager.cs:431-434`（只入队）→ `:786-802`（帧体里结算） |
| 钩子怎么被声明出来 | 契约 `Interfaces/MonoBehaviour/IMonoBehaviour\342\200\213.cs`（**文件名含零宽空格**）+ 生成器 `Src/Generators/VeloxDev.Core.Generator/Writers/MonoWriter.cs` |
| `Handled` 在动画一侧的含义 | `TransitionSystem/TransitionDiagnostics.cs:44` + `TransitionSystem/TransitionInterpreter.cs:199,203,289` |
| 跨模块共用的参数形状 | `TimeLine/TimeLineEventArgs.cs`、`TimeLine/TransitionEventArgs.cs` |

---

## 八、陷阱（带依据）

1. **`IMonoBehaviour` 的标识符里带零宽空格（U+200B）。** 文件名 `IMonoBehaviour\342\200\213.cs`，类型名 `IMonoBehaviour`，成员 `InitializeMonoBehaviour()` **都带**；唯独 `CloseMonoBehaviour()` **不带**。**这不影响编译**：C# 忽略 Cf 格式字符，元数据里也被剥掉（实测 `typeof(IFoo​).Name` 长度为 4；`GetMethod("Bar")` 找得到，`GetMethod("Bar​")` 返回 `null`）。所以生成器发的是**不带** ZWSP 的普通名字（`MonoWriter.cs:66,81,86`），链接正常。但**按字符串找这个名字的地方必须用不带 ZWSP 的拼写**。
2. **只贴 `[MonoBehaviour]` 什么都不会发生。** 特性只被**生成器**读（`MonoWriter.cs:19-50`），运行时的 `MonoBehaviourManager` **从不反射**这个特性。要真正跑起来，必须调生成的 `InitializeMonoBehaviour()`。仓库内唯一的运行期读法是**没有**——对照组：`Analizer.cs:90` 的 `TriggerAttributes` 里那一项只决定生成器是否介入。
3. **`[MonoBehaviour]` 上的 `fps` 只在第一次注册时入队一次。** 生成器把它展开成 `MonoBehaviourManager.SetTargetFPS({fps}, "{Channel}")` **放在 `RegisterBehaviour` 之前**（`MonoWriter.cs:76-84`），`fps >= 1` 时才发这句。而 `fps = -1`（默认）时**整句不生成**。所以「特性里写了 fps 却没生效」先看这个值是不是 `-1`；`TreeHelper` 用的是 `fps: 10`（`TreeHelper.cs:30`）。
4. **对一条从未被创建（或从未被启动）的 channel 调 `SetTargetFPS` 是静默无效的**：它会**创建** channel（`GetOrCreateChannel`）并把请求**入队**，但队列只有更新泵会排空，没启动就没有读者。WPF demo 的旧版正是踩了这个——三个组件注册在 default channel，却调 `SetTargetFPS(30, "game")`（`Examples/MonoBehaviour/WPF/Demo/SimState.cs:15-20` 的原注释）。
5. **`ThreadSafeFrameEventArgs` 是死的，而且它的遮蔽是危险的。** 全仓库零生产者、零消费者（grep 只命中定义与 `Src/Core/VeloxDev.Core.Test/TimeLine/TimeLineEventArgsTests.cs`）。它的 `public new bool Handled`（`ThreadSafeFrameEventArgs.cs:8`）**遮蔽**基类的 `virtual bool Handled`——若真有代码按 `TimeLineEventArgs` 引用同一个对象去读 `Handled`，读到的是基类字段，永远是 `false`。要用它，先把它接进泵（`CreateFrameEventArgs` 只造 `FrameEventArgs`，`:827`），否则它只是一份带锁的摆设。
6. **`Handled` 在 Update 里置上 = 本帧没有 LateUpdate。** 见 §三·1。这不是推断，是 `:711` 的循环体第一句 + 同一个 `frameArgs` 对象造成的确定结果。
7. **`SafeExecute` 吞掉钩子异常**（`:940-943`，只写 `Debug.WriteLine`）。在钩子里做 `Dispatcher.Invoke` 之类的跨线程动作一旦抛，症状是「循环还在跑但界面停止更新，且哪儿都没有日志」。WPF demo 的窗口侧注释把这条写成了它的整体设计理由（`Examples/MonoBehaviour/WPF/Demo/MainWindow.xaml.cs:12-19`）。
8. **`GetCachedWrappers` 有最长 1000ms 的陈旧窗口。** `_wrappersNeedSort` 为假且距上次检查不到 `MAX_CONFIG_CACHE_DURATION_MS` 时直接用旧数组（`:742-752`）。注册与注销都会置脏（`:801`、`:816`），所以正常路径上是即时的；但如果有人在别处直接改了 `_behaviors`，最坏要等 1 秒。
9. **池是有上限的。** `ObjectPool.Return` 在 `Interlocked.Increment(ref _count) > maxSize` 时不入栈（`MonoBehaviourManager.cs:86-93`）。上限 `DEFAULT_OBJECT_POOL_SIZE = 50`（`:24`）。所以「池」不是无限缓存 —— 超出就丢给 GC。
10. **`StopAsync` 的关闭预算只有 1000ms，超时后不报错。** 线程用 `Join(1000)`（`:358-359`），异步路径用 `Task.WhenAny(..., Task.Delay(1000))`（`:352`），两条都在 `catch (Exception) { }` 里（`:363`）——**放弃等待后照常置空线程字段、清统计、清队列，并发 `Stopped` 事件**（`:366-374`）。所以 `Stopped` 的意思是「我停止等了」，不是「它们都停了」。
11. **`RestartAsync` 里有两级超时，且第一级失败会 `ForceCleanup`。** 等关闭确认 1000ms（`:409-419`），失败就 `ForceCleanup()` —— 它会 `Dispose` 掉 `_cts` 并**新建一个**（`:968-983`）。第二级等队列排空 500ms（`:421-426`），失败**不**报错，直接 `Start()`。
12. **`_channels` 是进程级静态字典，从不移除条目。** `GetOrCreateChannel` 只有 `GetOrAdd`（`:1020-1031`），没有任何移除路径；`ChannelNames` 直接把键暴露出去（`:1034`）。所以「这个 channel 曾经存在过」是永真的，`UnregisterBehaviour` 之后 channel 仍在。
