# TimeLine 架构

> 代码：`Src/Core/VeloxDev.Core/TimeLine/`（4 个 .cs）+ 契约 `Src/Core/VeloxDev.Core/Interfaces/Tickable/`。
> 依赖：`Timing/`（总线与采样器）。反向：`TransitionSystem/` 的 `TransitionEventArgs : TimeLineEventArgs` 继承本模块的基类（该类型 2026-10-04 已移出本模块，见 §二）。
> 采样循环与帧节奏归 `TransitionSystem`（`FramePacerCore`）；本文写的是**时间轴与事件参数本身**。

本文只写「读完这 4 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个目录其实是两个不相干的东西

`TimeLine/` 装了两样东西，唯一的交集是 `FrameEventArgs : TimeLineEventArgs` 这一条继承：

| | 是什么 | 谁在用 | 依据 |
|---|---|---|---|
| **A. 事件参数族** | 两个 `EventArgs` 类型（抽象基类 `TimeLineEventArgs` + `FrameEventArgs`），跨模块共用的「回调参数」形状 | `TickManager`（`FrameEventArgs`）；`TransitionSystem` 的 `TransitionEventArgs` 继承本模块基类 | `TimeLineEventArgs.cs`、`FrameEventArgs.cs`、`TransitionSystem/Events/TransitionEventArgs.cs` 的 `TransitionEventArgs : TimeLineEventArgs` |
| **B. Tickable 式帧循环** | 命名 channel、两条后台泵、生命周期钩子 | Unity 式宿主；Core 内唯一消费者是 `WorkflowSystem/Templates/Helpers/TreeHelper.cs` | `TickManager.cs`、`TreeHelper.cs` 的 `[Tickable(channel: nameof(TreeHelper), fps: 10)]` 与 `partial void Update` |

**不解决什么：**

| 你以为在这里 | 其实在哪 |
|---|---|
| 七家 GUI 适配器的帧循环 | 不在这里。七家的定时器由各自的 `TransitionInterpreter.CreateFramePacer` 建立（`Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/`） |
| 动画的采样节奏 / FPS 上限 | `TransitionSystem/Runtime/FramePacerCore.cs` + `TransitionInterpreter.RunPassAsync` |
| 时间本身（暂停/倍速/锚点/epoch） | `Timing/TimeSourceCore.cs`。本模块只是**持有**一条总线并用它 |
| UI 线程编组 | `Threading/IThreadDispatcher` + 各家 `UIThreadInspector`。这里的线程是**自己起的后台线程** |
| 序列化 / 场景图 | 没有这个概念。行为靠 `RuntimeHelpers.GetHashCode` 在字典里认（`TickManager.cs` 的 `ProcessAddedBehaviors` 里 `_behaviors[hash] = wrapper`） |

---

## 二、事件参数族：两个类型，外加一个跨模块基类

| 类型 | 谁产生 | 谁读 | 状态 |
|---|---|---|---|
| `TimeLineEventArgs`（`TimeLineEventArgs.cs`） | 抽象基类 | — | `virtual bool Handled`（**全仓库零 `override`**；唯一那个用 `new` 遮蔽它的类型已于 2026-10-04 删除，见 §八·5）+ `DeltaTime`/`TotalTime`（`TimeSpan`，`internal set`）—— 基类给两条链路一份共享的时钟读数 |
| `FrameEventArgs`（`FrameEventArgs.cs`） | `TickManager.CreateFrameEventArgs` | 五个 `partial void` 钩子 | 只剩 `CurrentFPS`/`TargetFPS` 两个自有属性（全 `internal set`）；两个时钟读数已上移到基类 |
| `TransitionEventArgs`（**已移出本模块**：`TransitionSystem/Events/TransitionEventArgs.cs` 的 `TransitionEventArgs : TimeLineEventArgs`） | `TransitionInterpreter` 的 `Args`、`TransitionDiagnostics` 的 `Error`/`Warn`（各自 `new` 一份带载荷 args）、`Effects/Transition.cs` 的 `CoreExecute` catch | 用户挂在 effect 上的七个无载荷事件处理器 | `TransitionEventArgs : TimeLineEventArgs`；自有 `Loop`（int，本段第几趟，0 起）/ `Cycle`（long，这个 target 累计走了几趟），都 `internal set`。带载荷的 `Warn`/`Error` 改用派生泛型，见本节末 |

**四个读数（`FrameEventArgs` 的两个自有属性 + 基类的两个时钟）各有来源，都不是随手填的：**

- `DeltaTime` / `TotalTime` 都来自一个 `TimeSample`（`TickManager` 的 `UpdateLoop` 里 `_updateSampler.Sample()`）。注释写死了这条（`CreateFrameEventArgs` 的 XML 注释）：速率是**时钟**施加的，不是这里事后缩放的；所以 `TotalTime` 不含停摆期。
- `TargetFPS` 是**配置值**（`CreateFrameEventArgs` 读 `_targetFPS`），`CurrentFPS` 是**实测值**（同方法读 `_currentFPS`，由 `UpdatePerformanceStats` 用墙钟每秒更新一次）。
- 所以「TargetFPS 和 CurrentFPS 不一样」是正常的，不是 bug。

**stage 现在是一个枚举，不再是自由文本**（2026-10-04 起，定义在 `Interfaces/TransitionSystem/Enums/`）。诊断走两条带载荷的通道，各配一个枚举，值只能取成员 —— 写一个不存在的 stage 名不再编译得过：

| 枚举 | 成员 | 从哪里发出 |
|---|---|---|
| `WarnStage`（3 值，`Interfaces/TransitionSystem/Enums/WarnStage.cs` 的 `WarnStage`） | `Unreadable` | `Sampling/Interpolator.cs` 的 `Prepare` 里 `WarnStage.Unreadable` |
| | `Unsampled` | `Sampling/Interpolator.cs` 的 `Prepare` 里 `WarnStage.Unsampled` |
| | `Dropped` | `Runtime/TransitionScheduler.cs` 的 `if (!awoken)` 分支（Awake 投递被拒）与 `Sampling/SamplerSet.cs` 的 `Apply`（帧被宿主拒收）——**同一个枚举成员，两个不同的事** |
| `ErrorStage`（11 值，`Interfaces/TransitionSystem/Enums/ErrorStage.cs` 的 `ErrorStage`） | `Sampling` / `Run` / `Marshaling` | `Sampling/SamplerSet.cs` 的 `ApplyCore` / `Runtime/TransitionInterpreter.cs` 的 `Execute` + `Effects/Transition.cs` 的 `InvokeError` / `Runtime/TransitionInterpreter.cs` 的 `ReportMarshaling`（`ErrorStage.Marshaling`） |
| | `Awake` / `Prepare` | `Runtime/TransitionScheduler.cs` 的 `ErrorStage.Awake` / `ErrorStage.Prepare` |
| | `Start` / `Update` / `LateUpdate` / `Completed` / `Canceled` / `Finally` | `Runtime/TransitionInterpreter.cs` 的 `Report` 调用点（对应各 `ErrorStage`）/ `EmitFrame` / `Execute` 的 catch —— 这六个表示「**那个事件的某个订阅者抛了**」，不是引擎自己的步骤 |

枚举是封闭集合：消费方 `switch` stage 时，新增一个成员会让未覆盖的分支被编译器指出（`switch` 表达式无 `default` 时 CS8509；无 `default` 的 `switch` 语句不报）。引擎侧没有「必须处理每个 stage」的中央 `switch`，所以给枚举加成员不必改引擎代码，但也没有哪个调用点会替你保证新成员被用上。带载荷的 `Warn`/`Error` 把枚举装在 `TransitionEventArgs<WarnStage, string>` / `<ErrorStage, Exception>` 上（`Events/TransitionEventArgs{TStage,TValue}.cs` 的 `Stage` 属性）；七个无载荷事件用非泛型的 `TransitionEventArgs`（`Events/TransitionEventArgs.cs`），它只有 `Loop`/`Cycle`，没有 `Stage`。

---

## 三、`Handled`：一个属性名，两条互不相干的链路

这是本模块**最承重**的一点：`Handled` 是一个跨模块 ABI，同一个属性名在两个子系统里含义不同，且两边的读写者完全不同。

### 链路 A —— TickManager：终止本帧这个阶段剩下的行为

- **写**：钩子自己 `e.Handled = true`。
- **读**：三个 `ExecuteBehaviors*Sync` 的循环体**第一句**：`if (frameArgs.Handled || token.IsCancellationRequested) break;`（`TickManager.cs` 的 `ExecuteBehaviorsUpdateSync` / `ExecuteBehaviorsLateUpdateSync` / `ExecuteBehaviorsFixedUpdateSync`）。
- **含义**：本帧、本阶段、**剩余**的行为都不跑。已经跑过的不回滚。
- **重置点**：只在 `CreateFrameEventArgs` 里（`frameArgs.Handled = false;`），即**每帧一次**。

### 链路 B —— TransitionSystem：取消这一趟动画

- **写**：`TransitionDiagnostics.Raise`（`Runtime/TransitionDiagnostics.cs` 的 `Raise`）：`if (args.Handled && run is not null) run.Handled = true;` —— 即 effect 的带载荷 `Warn`/`Error` 处理器把 `TransitionEventArgs.Handled` 置上，落到 `run.Handled`。
- **读**：`TransitionInterpreter` 三处：趟循环起点、自动反向的第二趟之前、**每帧**（三处 `Args.Handled` 检查）。任一为真就 `throw new OperationCanceledException()`，走正常取消路径。
- **`Args` 的生命周期**：每个解释器一个（`Runtime/TransitionInterpreter.cs` 的 `Args` 属性），每个解释器只服务一段的一趟，所以 **`Handled` 不跨段**。

### 两条链路的三条实用结论

1. **`Handled` 在 Update 与 LateUpdate 之间不重置。** 同一个 `frameArgs` 对象先传给 `ExecuteBehaviorsUpdateSync` 再传给 `ExecuteBehaviorsLateUpdateSync`（`TickManager` 的 `UpdateLoop` 里连续两次调用），中间没有清位。**在 Update 里置上 `Handled`，本帧的整个 LateUpdate 被跳掉**。WPF demo 把这个做成了一颗按钮并直接计数（`Examples/Tickable/WPF/Demo/MainWindow.Hooks.cs` 的 `Update` 里 `e.Handled = true` 与 `Interlocked.Increment(ref _state.SkippedLateUpdates)`，读数在 `MainWindow.xaml.cs` 的 `Ordering.Text`）。
2. **`FixedUpdate` 每步自建参数**（`TickManager` 的 `FixedUpdateLoop`、异步版 `FixedUpdateLoopAsync` 各自 `CreateFrameEventArgs`），所以 `Handled` **不跨步**，也不与 Update 共享。demo 的状态行明说了这条（`MainWindow.xaml.cs` 的 `FixedPump.Text` / `HandledLine.Text`）。
3. **不是每个 args 都能否决。** `TransitionDiagnostics` 造的那份带 `run`（构造参数，`Runtime/TransitionDiagnostics.cs` 的 `run` 参数），置 `Handled` 才落下去；而 `Effects/Transition.cs` 的 `async void CoreExecute` catch 里直接 new 的 `TransitionEventArgs<ErrorStage, Exception>` **没有 run**——在那个处理器里置 `Handled` 什么都不发生（那时这一趟已经结束了）。

---

## 四、Tickable 式帧循环：一个 channel 是什么

**一个 channel = 一条时间总线 + 两个采样器 + 两条后台泵。** 没有全局单例状态，channel 之间完全隔离。

```
TickManager.<方法名>(channel)      ← 静态面，全部转发
        │  _channels.GetOrAdd(name, ...)   TickManager.GetOrCreateChannel
        ▼
LoopChannel
   ├ _bus = TimerCore.CreateTimeSource<ITimeSourceControl>()
   ├ _updateSampler : IUncompensatedTimeSampler
   ├ _fixedSampler  : ICompensatingTimeSampler（步长显式 16ms）
   ├ UpdateLoop      → _updateSampler.Sample()
   └ FixedUpdateLoop → _fixedSampler.Advance()
```

**总线是 channel 的对外接口，不是内部字段。** `LoopChannel.Bus` 是 public，静态面 `TickManager.Bus(channel)` 也是——它的注释写死了用途：把它传给 `Transition<T>.Execute(target, timeline)` 就能让**动画与帧回调共享同一条时间轴**，一次 `Pause()` 同时冻住两者，rate 同时乘两者。这也是本模块与 `Timing` 的接缝所在。

**所以 `Pause`/`Resume`/`SetTimeScale` 都不在 channel 里实现：**

| 调用 | 实现 | 依据 |
|---|---|---|
| `Pause()` | `_bus.Pause()`，前置守卫 `!_isRunning || _bus.IsPaused` | `LoopChannel.Pause` |
| `Resume()` | `_bus.Resume()`，同一守卫的镜像 | `LoopChannel.Resume` |
| `SetTimeScale(x)` | `_bus.SetRate(x)` **逐字转发**（`=>` 表达式体） | `LoopChannel.SetTimeScale` |
| `TogglePause()` | 看 `_bus.IsPaused` 选边 | `LoopChannel.TogglePause` |

**两条泵的形状（同步版；`UseAsyncLoop` 有同构的 async 版）：**

| | Update 泵（`UpdateLoop`） | FixedUpdate 泵（`FixedUpdateLoop`） |
|---|---|---|
| 停摆时 | `WaitWhileStalledAsync(token).GetAwaiter().GetResult()`，`continue`（`UpdateLoop` 的 `if (!_bus.IsAdvancing)` 分支） | 同左（`FixedUpdateLoop` 的同一分支） |
| 采样 | `_updateSampler.Sample()`，`Delta == TimeSpan.Zero` 就睡 1ms 重来 | `_fixedSampler.Advance(out sample)`，一次可能欠多步，**循环推完** |
| 每步的 `TotalTime` | 采样器给的 | `(firstStep + i) * stepTicks`——**每步各自的步序号乘步长**，不是把最后一次读数重复 N 遍 |
| 参数对象 | 一个 `frameArgs`，Update 与 LateUpdate 共用，用完还池 | 每步一个，`ExecuteBehaviorsFixedUpdateSync` 后**立刻**还池 |
| 尾睡 | `FrameRateControlSync`（墙钟量） | `_fixedSampler.TimeToNextStep` |

**为什么「停摆时 park 而不是轮询」是一条被写死的设计决定**：专用线程不能 `await`，所以同步版直接 `GetAwaiter().GetResult()` 阻塞一条自有线程，换零唤醒；取消能穿透这个等待是因为总线会观察令牌（`FixedUpdateLoop` 里 `if (!_bus.IsAdvancing)` 上方的注释原文）。`Sleep` 之所以按 `MAX_SLEEP_CHUNK_MS = 50` 分块睡（`TickManager.Sleep` 的分块循环），是为了「一次停止在 50ms 内被察觉，而不是等完整个间隔」——最低目标帧率下一帧预算有一秒长。

**注册与生命周期全走队列，在更新循环的帧体里结算**（顺序即 `ProcessMainThreadOperations` 的四步）：

```
_frameStartTime = GetTimestamp()
ProcessMainThreadOperations()
  ├ ≤64 个 _mainThreadQueue 动作
  ├ ProcessConfigChanges()          目标帧率：_targetFPS 与缓存时长同时改
  ├ ProcessAddedBehaviors()         取 wrapper → InvokeAwake → InvokeStart → 入字典
  └ ProcessRemovedBehaviors()       出字典 → wrapper.Clear → 还池
sample = _updateSampler.Sample()
frameArgs = CreateFrameEventArgs(...)
ExecuteBehaviorsUpdateSync / LateUpdateSync
```

由此得到三条不变量：

- **`RegisterBehaviour` 只入队**（`LoopChannel.RegisterBehaviour`），所以调用它的顺序与生命周期顺序无关；`Awake`/`Start` **一定跑在第一帧体之前**（它们在 `Sample()` 的上游），且都跑在**更新线程**上。
- **`Awake`/`Start` 各自的异常被 `SafeExecute` 吞掉**（`TickManager.SafeExecute`，只 `Debug.WriteLine`）。所以「钩子里抛异常」= 静默半死。
- **配置队列由更新泵排空**，而固定泵的步长**不走配置队列**（`_pendingFixedIntervalMs`：`SetFixedUpdateInterval` 写入，`FixedUpdateLoop` 在自己的线程上取走）。理由是写死的：配置队列归更新泵，采样器归固定泵，从更新线程写 `Step` 会与 `Advance` 争它要重置的累加器。

**数字（保留实测到的常量）：**

| 常量 | 值 |
|---|---|
| `DEFAULT_CHANNEL` | `"default"` |
| `MIN_FPS` / `MAX_FPS` / `DEFAULT_TARGET_FPS` | `1` / `1000` / `60` |
| `DEFAULT_FIXED_UPDATE_INTERVAL_MS` | `16` |
| `MIN_UPDATE_INTERVAL_MS` / `MAX_UPDATE_INTERVAL_MS` | `1` / `1000` |
| `MIN_SLEEP_MS` | `1` |
| `MAX_SLEEP_CHUNK_MS` | `50` |
| `RESTART_SHUTDOWN_TIMEOUT_MS` | `1000` |
| `RESTART_QUEUE_CLEAR_TIMEOUT_MS` | `500` |
| `DEFAULT_RESTART_CHECK_INTERVAL_MS` | `5` |
| `THREAD_INACTIVITY_TIMEOUT_MS` | `2000` |
| `DEFAULT_OBJECT_POOL_SIZE`（三个池共用） | `50` |
| `MAX_CONFIG_CACHE_DURATION_MS` | `1000` |
| 每帧主线程动作上限（`ProcessMainThreadOperations`） | `64`（字面量，非具名常量） |

**线程约定：** 名字 `VeloxDev.Update[{Name}]` / `VeloxDev.FixedUpdate[{Name}]`，`IsBackground = true`，`Priority = AboveNormal`（`LoopChannel.Start` 里 `new Thread` 的那段）。**两条都是专起的后台线程，不是线程池、不是 UI 线程、也不是 `SynchronizationContext` 上的。** 异步版（`UseAsyncLoop`）把「线程」换成 `Task`，其余语义不变（`FixedUpdateLoopAsync` / `UpdateLoopAsync`）。⇒ **`Update` / `FixedUpdate` 里要碰 UI 绑定的状态，必须自己编组回宿主上下文**：`ExecuteOnMainThread` 不行（它排的还是这条线程，见下）。目前做这件事的是 `TreeHelper` 的虚拟化趟（装 helper 时记下 `SynchronizationContext.Current` 再 post 回去，见 [`WorkflowSystem` 记忆 §3.5](../WorkflowSystem/architecture.md)）。

---

## 五、边界：这不是七家 GUI 适配器的东西

**`Src/Adapters/VeloxDev.*/` 下没有任何一行引用 `TickManager`**，只有两处**注释**在解释「为什么这段代码会被非 UI 线程碰到」：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowMinimapOverlay.cs` 与 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowMinimapOverlay.cs` 中的相关注释（后者点明了「经 `BroadcastVisibleItemLayout`」）。七家的帧循环各自由 `TransitionInterpreter.CreateFramePacer` 建立。

它给的是 **Unity 式宿主** —— 游戏循环、Unity 的 MonoBehaviour 集成、或任何想跑一条**独立于 UI 的帧循环**的非 UI 代码。判据很简单：你要的是「一条与渲染无关、能自己调速、暂停时不占 CPU 的帧泵」，就用它；你要的是「让动画在 UI 线程上出帧」，那是 `TransitionSystem` + 适配器的事。

**但 Core 内部有一个真实消费者**，容易被忽略：

`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/Helpers/TreeHelper.cs` 的 `[Tickable(channel: nameof(TreeHelper), fps: 10)]`，channel 在带 `cellSize` 的构造里 `TickManager.Start`，`Install` 里 `InitializeTickable()`，`Uninstall` 里 `CloseTickable()`，`partial void Update` 判脏后执行 `Virtualize` + `BroadcastVisibleItemLayout`。

**这条链的后果值得单独记住：tick 线程只判脏，不碰可见集。** 装了虚拟化的 helper 在 `Install` 时记下 `SynchronizationContext.Current`，`Update` 把 `Virtualize` + `BroadcastVisibleItemLayout` 整趟 post 回该上下文（`TreeHelper.cs` 的 `partial void Update`）；没有上下文时退回老的同线程执行。编组是消费方自己的责任，本模块不提供（对照：`TransitionSystem` 有一条完整的 `IThreadDispatcher` 通路）。下游曾因这一趟在 `VeloxDev.Update[TreeHelper]` 上改 UI 绑定集合而随机丢连线。也注意 `[Tickable]` 只装在泛型类 `TreeHelper<T>` 上（`TreeHelper.cs` 的 `[Tickable(channel: nameof(TreeHelper), fps: 10)]`），而 `nameof(TreeHelper)` 在泛型内解析为不带参数个数的 `"TreeHelper"`。

---

## 六、不变量

**A. 顺序类**

1. **`InitializeTickable()` 必须由用户代码自己调。** 生成器只**实现**它，不调它（`Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` 的 `GenerateBody`）。只贴 `[Tickable]` 而不调 = 什么都没发生，且不报错。参照 `TreeHelper` 的 `Install` 与 `Examples/Tickable/WPF/Demo/MainWindow.xaml.cs` 的 `Loaded` 处理器。
2. **`Awake` → `Start` → 第一帧体**：由 `ProcessAddedBehaviors` 在 `Sample()` 上游保证，不需要用户排序。
3. **`Update` 一定早于 `LateUpdate`**，两者在同一次循环迭代里连续调用（`UpdateLoop` 里连续两次 `ExecuteBehaviors*Sync`）。demo 用一个必须为 0 的计数器守这条（`MainWindow.Hooks.cs` 的 `LateUpdate` 里 `OrderViolations` 计数）。
4. **`SetUseAsyncLoop` / `ClearUseAsyncLoopOverride` 只能在 channel 未运行时调**，运行中抛 `InvalidOperationException`（两者的 `if (_isRunning) throw`）。
5. **`CreateFrameEventArgs` 每次必须把 `Handled` 清回 false**（`CreateFrameEventArgs` 的 `frameArgs.Handled = false;`）。池化对象带着上一帧的值，漏清这一句就是「每帧都被 Handled」。

**B. 成对类**

6. `InitializeTickable()` ↔ `CloseTickable()`。`TreeHelper` 在 `Install`/`Uninstall` 里成对调，demo 用一对按钮演示（`MainWindow.xaml.cs` 的重新注册/停止重启一对）。
7. `_frameEventArgsPool.Get()` ↔ `Return`，**三条路径都要还**：Update 循环、FixedUpdate 循环每一步、异步版同构（各自的 `_frameEventArgsPool.Return`）。漏还只是少一个复用对象，不报错。
8. `_isUpdateThreadActive = true` 在循环体首行 / `finally` 里置 false（`UpdateLoop` 与固定泵 `FixedUpdateLoop` 各自的置位与 `finally`），async 版同构（`UpdateLoopAsync` / `FixedUpdateLoopAsync`）。
9. **`Start()` 与 `StopAsync()` 各自调 `_bus.Resume()`**（两个方法里的 `_bus.Resume()`）：启动要清掉上一个生命周期留下的暂停，否则「暂停中被停掉的 channel 重启后会立刻 park，再也跑不起来」（`Start` 里的注释）；停止也不把暂停状态留给下一个生命周期（`StopAsync` 里的注释）。**注意这不解除 rate 为 0 的冻结**——那不是一个暂停。

**C. 反直觉/生命周期类**

10. **重新注册同一个行为会让 `Awake`/`Start` 再跑一遍。** `ProcessAddedBehaviors` 对新来的行为无条件 `InvokeAwake` + `InvokeStart`，并 `_behaviors[hash] = wrapper` **覆盖**旧 wrapper——旧的那个还在泵的缓存数组里活着，直到下一次 `RebuildCachedWrappers`。所以「注册两次」= `Awake` 两次，而「停 channel 再启」**不重跑**（`RestartAsync` 只是 `StopAsync` → `Start`）。demo 专门做了这对按钮（`MainWindow.xaml.cs` 的重新注册/停止重启一对）。
11. **`ExecutionOrder` 只是注册计数器。** `wrapper.Reset(behavior, Interlocked.Increment(ref _instanceCounter))`，插入排序按它排（`RebuildCachedWrappers`）。**用户无法指定行为执行顺序**，唯一的顺序就是注册顺序。
12. **`FrameEventArgs` 是池化对象，绝不能缓存跨帧使用。** 下一帧同一个对象会被改写成新值；FixedUpdate 的那份更激进，当场还池（`FixedUpdateLoop` 里 `_frameEventArgsPool.Return(fixedFrameArgs)`）。
13. **`ExecuteOnMainThread` 的「Main」是 channel 自己的更新线程。** 它入 `_mainThreadQueue`（`LoopChannel.ExecuteOnMainThread`），由 `ProcessMainThreadOperations` 在更新泵里排空。名字容易读成 UI 线程 —— 不是。且仓库内**零调用者**（grep `Src/`+`Examples/` 只有定义与静态转发两处）。
14. **`Bus(channel)` 对从未创建的 channel 返回 `null`，不创建。** 注释写死：「查询不该有副作用地创建 channel」（`TickManager.Bus` 的注释）。而 `Start`/`Pause`/`SetTargetFPS` 等**全部走 `GetOrCreateChannel`**（静态命令面的每个转发）——所以「读状态」与「下命令」对 channel 存在性的影响不对称。
15. **`UseAsyncLoop` 的默认值按 TFM 分叉**：`NET5_0_OR_GREATER` 时是 `IsBrowser() || IsIOS()`，**否则是 `true`**（`TickManager.UseAsyncLoop` 的属性初始化器）。也就是 netstandard2.0 / netframework 目标**默认走异步模式**，桌面老框架上并没有原生线程泵。
16. **`IsUpdateThreadAlive` 把「停摆」也算活着**：`!_bus.IsAdvancing || IsRecentActivity(...)`（`LoopChannel.IsUpdateThreadAlive`）。注释写死了理由——停摆中的循环 park 在总线上，「最近有没有活动」必然为假，不改会把一条好线程报成已死，`RestartAsync` 也会因此走 `ForceCleanup`（该属性上方的注释）。
17. **`SetTargetFPS` 越界静默丢弃**（`LoopChannel.SetTargetFPS` 的 `if (fps < MIN_FPS || fps > MAX_FPS) return;`），不抛也不日志。合法值也只是**入队**，由更新泵在下一帧应用。

---

## 七、入口：我要改 X，先打开哪个文件

| 我想改 | 打开 |
|---|---|
| 帧体顺序 / 生命周期落点 | `TimeLine/TickManager.cs` 的 `ProcessMainThreadOperations` 与 `UpdateLoop` |
| Update 与 LateUpdate 的调用点、`Handled` 的检查 | `TickManager.cs` 的三个 `ExecuteBehaviors*Sync` |
| 「本帧传出去什么」 | `TickManager.cs` 的 `CreateFrameEventArgs` |
| 目标帧率怎么生效 | `TickManager.cs` 的 `ProcessConfigChanges` + `FrameRateControlSync`（墙钟） |
| FixedUpdate 的步长与补步 | `TickManager.cs` 的 `FixedUpdateLoop` + `Timing/CompensatingTimeSampler.cs` |
| 暂停/倍速（channel 级） | `TickManager.cs` 的 `LoopChannel.Pause`/`Resume`/`SetTimeScale`，实现全在 `Timing/TimeSourceCore.cs` |
| 线程名字/优先级/是否用线程 | `TickManager.cs` 的 `LoopChannel.Start`、`TickManager.UseAsyncLoop` |
| 启动/停止/重启语义 | `TickManager.cs` 的 `LoopChannel.Start`、`StopAsync`、`RestartAsync` |
| 注册为什么没生效 | `TickManager.cs` 的 `RegisterBehaviour`（只入队）→ `ProcessAddedBehaviors`（帧体里结算） |
| 钩子怎么被声明出来 | 契约 `Interfaces/Tickable/ITickable.cs` + 生成器 `Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs` |
| `Handled` 在动画一侧的含义 | `TransitionSystem/Runtime/TransitionDiagnostics.cs` 的 `Raise` + `TransitionSystem/Runtime/TransitionInterpreter.cs` 三处 `Args.Handled` 检查 |
| 跨模块共用的参数形状 | `TimeLine/TimeLineEventArgs.cs`、`TimeLine/FrameEventArgs.cs`；`TransitionSystem/Events/TransitionEventArgs.cs`、`Events/TransitionEventArgs{TStage,TValue}.cs`、`Interfaces/TransitionSystem/Enums/{Warn,Error}Stage.cs` |

---

## 八、陷阱（带依据）

1. **改 `ITickable` 这类名字时当心不可见字符** —— 它曾带着一个零宽空格 U+200B（2026-10-01 已清除，当时它还叫 `IMonoBehaviour`）。它为什么编译得过却仍然有害，见 `memory/modules/Interfaces/architecture.md` §六·9 —— 一句话：C# 忽略 Cf 类字符，所以带与不带是同一个标识符，坑全在人这一侧（裸路径打不开、`grep -l` 漏、`git ls-files` 会把路径转义）。现在按字符串找这个名字用普通拼写就行。
2. **只贴 `[Tickable]` 什么都不会发生。** 特性只被**生成器**读（`TickWriter.ReadTickableConfig`），运行时的 `TickManager` **从不反射**这个特性。要真正跑起来，必须调生成的 `InitializeTickable()`。仓库内唯一的运行期读法是**没有**——对照组：`Analizer.cs` 的 `TriggerAttributes` 里 `"VeloxDev.TimeLine.TickableAttribute"` 那一项只决定生成器是否介入。
3. **`[Tickable]` 上的 `fps` 只在第一次注册时入队一次。** 生成器把它展开成 `TickManager.SetTargetFPS({fps}, "{Channel}")` **放在 `RegisterBehaviour` 之前**（`TickWriter` 的 `GenerateBody` 里 `setFpsLine`），`fps >= 1` 时才发这句。而 `fps = -1`（默认）时**整句不生成**。所以「特性里写了 fps 却没生效」先看这个值是不是 `-1`；`TreeHelper` 用的是 `fps: 10`（`TreeHelper.cs` 的 `[Tickable(channel: nameof(TreeHelper), fps: 10)]`）。
4. **对一条从未被创建（或从未被启动）的 channel 调 `SetTargetFPS` 是静默无效的**：它会**创建** channel（`GetOrCreateChannel`）并把请求**入队**，但队列只有更新泵会排空，没启动就没有读者。（例子见 `Examples/Tickable/WPF/Demo/SimState.cs` 里那段注释：三个组件注册在 default channel，却调 `SetTargetFPS(30, "game")`。）
5. **`ThreadSafeFrameEventArgs` 已于 2026-10-04 删除 —— 它是一份「带锁的摆设」，而且那个锁永远走不到。** 它 `public new bool Handled` **遮蔽**基类的 `virtual bool Handled`，而泵的读写都在 `FrameEventArgs` 静态类型上（`ExecuteBehaviorsUpdateSync(FrameEventArgs frameArgs, …)` 的读、`CreateFrameEventArgs` 的写），所以即便把实例塞进池子，走上来的也是基类那个**没锁**的属性。何况它连池子都进不了：池的声明就是 `ObjectPool<FrameEventArgs>`（`_frameEventArgsPool`），全仓除定义与三条测试外零引用，生成器也不产出它。
   ⇒ **记住这个形状**：`virtual` 留着是给 `override` 的，用 `new` 遮蔽等于让虚分派失效 —— 症状是「看起来有线程安全，实际读写的是另一个字段」，而且类型自己的单测**通过派生类型**访问，绿得毫无意义。要线程安全，这家的既有写法是 `Volatile.Read`（同一文件 `CreateFrameEventArgs` 里读 `_targetFPS` 就是），比对每次读写进出 `lock` 更贴。
6. **`Handled` 在 Update 里置上 = 本帧没有 LateUpdate。** 见 §三·1。这不是推断，是 `ExecuteBehaviorsLateUpdateSync` 的循环体第一句 + 同一个 `frameArgs` 对象造成的确定结果。
7. **`SafeExecute` 吞掉钩子异常**（`TickManager.SafeExecute`，只写 `Debug.WriteLine`）。在钩子里做 `Dispatcher.Invoke` 之类的跨线程动作一旦抛，症状是「循环还在跑但界面停止更新，且哪儿都没有日志」。WPF demo 的钩子侧头部注释把这条写成了它的整体设计理由（`Examples/Tickable/WPF/Demo/MainWindow.Hooks.cs` 的头部注释）。
8. **`GetCachedWrappers` 有最长 1000ms 的陈旧窗口。** `_wrappersNeedSort` 为假且距上次检查不到 `MAX_CONFIG_CACHE_DURATION_MS` 时直接用旧数组（`GetCachedWrappers`）。注册与注销都会置脏（`ProcessAddedBehaviors` / `ProcessRemovedBehaviors`），所以正常路径上是即时的；但如果有人在别处直接改了 `_behaviors`，最坏要等 1 秒。
9. **池是有上限的。** `ObjectPool.Return` 在 `Interlocked.Increment(ref _count) > maxSize` 时不入栈（`TickManager` 里 `ObjectPool<T>` 的 `Return`）。上限 `DEFAULT_OBJECT_POOL_SIZE = 50`。所以「池」不是无限缓存 —— 超出就丢给 GC。
10. **`StopAsync` 的关闭预算只有 1000ms，超时后不报错。** 线程用 `Join(1000)`，异步路径用 `Task.WhenAny(..., Task.Delay(1000))`，两条都在 `catch (Exception) { }` 里（`LoopChannel.StopAsync`）——**放弃等待后照常置空线程字段、清统计、清队列，并发 `Stopped` 事件**。所以 `Stopped` 的意思是「我停止等了」，不是「它们都停了」。
11. **`RestartAsync` 里有两级超时，且第一级失败会 `ForceCleanup`。** 等关闭确认 1000ms，失败就 `ForceCleanup()` —— 它会 `Dispose` 掉 `_cts` 并**新建一个**（`LoopChannel.ForceCleanup`）。第二级等队列排空 500ms，失败**不**报错，直接 `Start()`（`LoopChannel.RestartAsync`）。
12. **`_channels` 是进程级静态字典，从不移除条目。** `GetOrCreateChannel` 只有 `GetOrAdd`，没有任何移除路径；`ChannelNames` 直接把键暴露出去（`TickManager.ChannelNames`）。所以「这个 channel 曾经存在过」是永真的，`UnregisterBehaviour` 之后 channel 仍在。
