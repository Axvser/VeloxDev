# Threading — 扩展

> 代码：`Src/Core/VeloxDev.Core/Threading/`（契约在 `IThreadDispatcher.cs`，基类在 `ThreadDispatcherBase.cs`）。
> **本文只写这条轴本身的扩展做法**：新接一家平台时 Threading 侧要提供什么、哪些写法看着能编译但错、要同步改哪几处。
> 八类适配器的总清单与其余契约（pacer、采样器、`CreateScheduler`…）在 `memory/modules/TransitionSystem/extension.md` §三·C —— 那里是入口，本文只补这条轴。

---

## 一、扩展点地图

| 我要扩展… | 扩展点 | 位置 | 必须吗 |
|---|---|---|---|
| 某家「线程」怎么命名 | `ThreadFor(object) → ThreadRef` | `ThreadDispatcherBase.cs:13`（public abstract） | 必须 |
| 「调用方在这条线程上吗」 | `IsCurrentThread(ThreadRef) → bool` | `:27`（protected abstract） | 必须 —— 基类给不出（`ThreadRef` 是不透明句柄） |
| 怎么把动作送到那条线程 | `PostCore(target, thread, action, priority) → bool` | `:37`（protected abstract） | 必须 |
| 更精确的「在不在 target 的线程上」 | `IsCurrentFor(target, thread)` | `:24`（protected virtual，默认 `IsCurrentThread(thread)`） | 可选 —— 覆写它 = 决定 UI 线程上的写要不要走消息泵（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:63` 是全树唯一例子） |
| 阻塞读用的优先级 | `InternalPriority` | `:44`（protected virtual，默认 `default!`） | **有优先级的宿主必须覆写** |
| 「宿主还活着吗」 | `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs`；宿主侧的入口是 `TransitionHostBase.IsAlive` 与 `TransitionHostBase.Lifetime`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:12,14`） | | 可选，但见 `memory/modules/Lifetime/extension.md` |

**不要在适配器里实现 `IThreadDispatcher<TPriorityCore>`**：派生 `ThreadDispatcherBase<TPriorityCore>`（→ 实用上再派生 `TransitionHostBase<TPriorityCore>`）。基类的注释（`ThreadDispatcherBase.cs:3-5`）写明了理由：派生面只写一次，宿主的差异只剩三个答案。直接实现接口 = 自己重写 `PostAsync` 的「只在被接受时才等」规则（`:57-68`）与 `Run<T>` 的失败语义（`:83`），这两条都是错了不报错的东西。

---

## 二、官方做法 vs 看着能编译、但错的捷径

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| `PostCore` 在拿不到句柄/队列已关时乐观返回 `true` | `PostAsync` 的消费者会等一个永不完成的 TCS；而它唯一消费者 `Awake` 是在持有 scheduler `_gate` 时 await 的 ⇒ 那条 `_gate` 永不释放，该 target 之后每次动画都永远排队 | `ThreadDispatcherBase.cs:57-68`、`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:114`/`:128`/`:178` |
| `ThreadFor` 里为调用方**造**一个句柄 | 消费者被钉在一个没人驱动的消息泵上，且没有任何地方会报 | `IThreadDispatcher.cs:14-17` |
| 把 `IsCurrentFor` 覆写成「我是 UI 线程就 true」之类的通用判断 | 判据是 **target 相对**的；谎报 true 会让 inline 分支在错的线程上直写 target，静默通过（inline 分支不投递、不排队、也不 catch） | `ThreadDispatcherBase.cs:50`、`:89-93`、`IThreadDispatcher.cs:4-10` |
| 有优先级的宿主不覆写 `InternalPriority` | 它决定 `Run<T>` 那次**阻塞读**排在哪个优先级。默认 `default!`，而 Core 那句注释（「`default(DispatcherPriority)` 是 `Inactive`，这条读要等消息泵完全空闲才跑」）**只在 WPF 的枚举上成立** —— Avalonia 的 `default` 不是 `Inactive`，两家的差别与实测见 `memory/modules/TransitionSystem/adapters/avalonia.md` §二.1（两个枚举都定义在 SDK 里，树内核不到） | `ThreadDispatcherBase.cs:39-44`、`:81`；`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:147` |
| 往 `ThreadFor` 加 `catch { return null; }` 之外的语义 | 「没有线程拥有它」已经是 `ThreadRef.None` 这个**答案**，不是异常；返 None 的后果是确定的：pacer 得到 `null`、`PostCore` 该丢帧 | `ThreadRef.cs:17-18`、`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/TransitionInterpreter.cs:10` |
| 用三参 `Post(target, action, priority)` 做写路径 | 它每次自己解析线程（`:46-47`），在「答案随调用者变化」的宿主上必然解析错；写路径必须用钉好的那条 | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:114-115`、`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:107-109` |
| 以为 `Post` 的 `true` 意味着「跑完了」 | 它只意味着「入队了」。要等就得用 `PostAsync`，而 `PostAsync` 也只在真入队时才等 | `IThreadDispatcher.cs:26-29` vs `:41` |
| 把 `ThreadRef` 存起来当「UI 线程身份」跨进程/跨 circuit 复用 | 它是不透明句柄、相等即引用相等；Razor 上句柄随 circuit 变，缓存第一个就等于把所有 circuit 的帧投进第一个 | `ThreadRef.cs:30-36`；`Src/Core/VeloxDev.Core.Test/TransitionSystem/TransitionRunThreadAffinityTests.cs:70-104` 是这条的回归测试（两个 circuit 各投各的） |

**没有「官方 vs 捷径」的一条提醒**：`Post`/`PostAsync`/`Run` 三条路径对**异常**的处理不同 —— `Run`/`PostAsync` 把动作异常装进完成源（`Run` 再在 `GetResult` 处重抛，`PostAsync` 由 `await` 处重抛），`Post` 让异常落在泵它的线程上。写路径的异常已经被 `SamplerSet.ApplyCore` 收口（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:132-142`），**不要在适配器里再加一层 catch**。

---

## 三、步骤清单：新接一家平台的 Threading 侧

1. **定句柄类型**：这家拿什么代表「一条线程」？`ThreadRef.From(handle)` 包起来（`ThreadRef.cs:23`）。七家现状：`Dispatcher`（WPF/Jalium）、`DispatcherQueue`（WinUI）、`SynchronizationContext`（WinForms/Razor）、`IDispatcher`（MAUI）、Avalonia 用全局 `Dispatcher.UIThread`。
2. **定 `ThreadFor`**：能从 target 推就推（`target is DispatcherObject`），推不出再退到应用级（`Application.Current?.Dispatcher`）。**不得造句柄**。
3. **定 `IsCurrentThread`**：`TryGet<T>` 出自己的类型 → 问它（`CheckAccess()` / `HasThreadAccess` / `ReferenceEquals(SynchronizationContext.Current, …)`）。
4. **定 `PostCore`**：返回**真实的**「入队了吗」。平台没有失败信号时（`SynchronizationContext.Post` 无返回值）只能按已接受记，但要在注释里写明兜底是谁 —— 现成的措辞见 `Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:66-68`。
5. **有优先级就覆写 `InternalPriority`**；没有优先级就把第七型参填 `NonPriority`（`NonPriority.cs`），不需要碰它。
6. **存活**：见 `memory/modules/Lifetime/extension.md`。这一条是可以**先不做**的，但要知道代价（同一份文件里写了）。
7. **pacer 必须用 `affinity.ThreadFor(target)`**，不能用平台侧的等价调用去另推一次 —— 两者不一致 = 每帧一次 dispatch（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:64-67`）。`CreateFramePacer` 只拿到 `IThreadAffinity`，正是为了让这家无法悄悄拿到投递/存活。
8. 跑 `Src/Core/VeloxDev.Core.Test/` 里已有的宿主形状（`Src/Core/VeloxDev.Core.Test/TestHosts.cs` 三个：`ImmediateHost` 全 inline、`InlinePostHost` 永不在线程上、`DeferredHost` 入队后手动 `Pump()`）—— 这三条覆盖了「真投递」「优先级参数」「fire-and-forget 落地」三件事，新宿主至少要在同形的位置有对应物。

---

## 四、联动清单

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | 该家 `UIThreadInspector` 的三个答案（`ThreadFor`/`IsCurrentThread`/`PostCore`） | 编译不过（抽象成员） |
| 2 | 有优先级时覆写 `InternalPriority` | 编译过、跑得像好的 —— 只在拿不到 target 那条线程时，`Run<T>` 那次读的排队档位不对 |
| 3 | **第七型参 `TPriorityCore` 四处必须一致**：该家 `UIThreadInspector` 的基类型参、`TransitionScheduler`、`Transition`、`Interpolator.CreateScheduler` 里的 `is ITransitionEffect<TPriorityCore>` | `CreateScheduler` 那处写错 ⇒ 主题切换**静默降级为瞬切**（`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/Interpolator.cs:25-26` 是正确形状） |
| 4 | `memory/modules/TransitionSystem/adapters/<平台>.md` 记一条本家在存活/线程归属上的差异 | 下一家接平台的人会照抄错的默认（`memory/modules/TransitionSystem/adapters/avalonia.md` §三.1 的比对本就是为这个存在的） |
| 5 | 若该家 `IsAlive`/`PostCore` 是「乐观恒真」：`Src/Core/VeloxDev.Core.Test/` 里补一条宿主形状，覆盖「关停后投递会怎样」 | 该类缺陷验收套件看不见（单 circuit/单线程跑不出来） |

---

## 五、树内没有验证过的路径（省掉无谓的搜索）

- **三参 `Post(target, action, priority)` 零调用者**（`Src/`、`Examples/`、`Src/Core/VeloxDev.Core.Test/`）。它是接口上的便利面，但没有任何测试或 demo 走过它 —— 改基类时别以为它被覆盖了。
- **`IThreadAffinity.IsCurrent` 只有一个消费者**：WinForms 的 `Src/Adapters/VeloxDev.WinForms/PlatformAdapters/TransitionInterpreter.cs:10`。所以「`IsCurrent` 的实现对不对」在其余六家上是**没有任何测试压力**的。
- **`IThreadDispatcher` 在树内只有 `ThreadDispatcherBase` 一个实现**；测试里的宿主（`Src/Core/VeloxDev.Core.Test/TestHosts.cs` 三个 + `TransitionSchedulerExitTests.cs:31`、`TransitionSchedulerAwakeTests.cs:33`、`TransitionRunThreadAffinityTests.cs:70`、`TransitionDiagnosticsTests.cs:175` 等）全部派生自它，其中只有 `DeferredHost` 覆写了 `Run<T>`（`TestHosts.cs:58`，把读改成 inline）。
