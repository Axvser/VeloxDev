# Threading — 架构

> 代码：`Src/Core/VeloxDev.Core/Threading/`（4 个 .cs，命名空间 `VeloxDev.Threading`）。
> 注意 **`IThreadDispatcher.cs` 装的是两个接口** —— `IThreadAffinity`（`:12`）与 `IThreadDispatcher<TPriorityCore>`（`:24`），文件名只对了后一半。
> 唯一的消费者是 TransitionSystem 的宿主接缝：`Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ITransitionHost.cs:14` 把 `IThreadDispatcher<TPriorityCore>` 与 `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs` 合成一个接口；七家实现见 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/UIThreadInspector.cs`。

本文只写「读完这 4 个文件 + 找到全部调用点才知道的东西」。成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 宿主接缝的**同一条轴**：给定一个 target，回答「它归哪条线程」、把一段工作送过去、并报告「送出去了没有」。抽象的目的是让 Core 只依赖**答案**，不依赖任何平台的线程类型（`ThreadRef` 里的句柄是不透明的）。

**不解决什么（这些边界常常被误以为在模块内）：**

| 你以为在这里 | 实际在哪 |
|---|---|
| 优先级的取值与含义 | 宿主的类型参数 `TPriorityCore`。Threading **不知道也不检查它** —— 全树没有任何 `where TPriorityCore` 约束 |
| 「宿主还活着吗」 | `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs`；两个模块直到 `Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ITransitionHost.cs:14` 才被合成 |
| 某家怎么找线程、怎么投递 | 七家 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/UIThreadInspector.cs`（差异指路 `memory/modules/TransitionSystem/adapters/<平台>.md`） |
| 帧的节拍 / 等在哪条线程上 | `Src/Core/VeloxDev.Core/TransitionSystem/FramePacerCore.cs`；Threading 只提供「哪条线程」这个答案 |
| 排队、去重、取消、代际 | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs` |
| 「把异常安全地送回去」 | **不成立**。只有 `PostAsync`/`Run` 把动作的异常装进完成源；`Post` 是 fire-and-forget，动作的异常落在泵它的那条线程上 |
| 线程池 / 自建线程 | 没有。这里的每条路径最终都落到宿主已有的一条线程上 |
| 「全局的 UI 线程」 | 刻意没有这个概念（`IThreadDispatcher.cs:4-10`）：每个成员都是 **target 相对**的。理由写在注释里 —— 「全局问一次 + 按 target 问一次」在单 UI 线程的宿主上恒一致，而**不一致恰恰发生在多 UI 线程的宿主上**（Razor 的每个 circuit） |

---

## 二、三个契约，两层

| 契约 | 位置 | 谁拿它 | 为什么是这一层 |
|---|---|---|---|
| `IThreadAffinity` | `IThreadDispatcher.cs:12` | 帧 pacer（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:79` 的 `CreateFramePacer(object, IThreadAffinity)`） | pacer 只需要「等在哪条线程上」。给它投递能力就是给它绕过 stale-frame 守卫的机会 |
| `IThreadDispatcher<TPriorityCore>` | `:24` | 写路径（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:115`）与调度器（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:128`） | 加投递、加阻塞读 |
| `ITransitionHost<TPriorityCore>` | `Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ITransitionHost.cs:14` | 采样帧集、调度器 | = dispatcher + `IApplicationState`，**零新增成员**（注释明说它只是合成） |

派生面只写一次：`ThreadDispatcherBase<TPriorityCore>` 是全树唯一实现，注释写明「implemented once so no host can derive it differently from another」（`ThreadDispatcherBase.cs:3-5`）。宿主提供三个成员（`ThreadFor` / `IsCurrentThread` / `PostCore`），可选再给两个（`IsCurrentFor` / `InternalPriority`），其余全在基类。

**`ThreadRef` 是句柄，不是线程身份。** 它没有任何「当前线程」的入口，相等即句柄引用相等（`ThreadRef.cs:36`），只能 `TryGet<T>` 出宿主自己的类型（`:30-34`）。所以**「调用线程是不是这条线程」这个谓词基类给不出**，只能由宿主实现 `IsCurrentThread(ThreadRef)`（`ThreadDispatcherBase.cs:21-27` 的注释把这条说死了）。`None` 是「没有线程拥有它」这个**答案**，不是失败（`ThreadRef.cs:17-18`）。

---

## 三、谁调用了什么（调用点普查）

这是本模块最值得先看的一张表：**每个成员在树内的消费者少得惊人**，这决定了改它的代价。

| 成员 | 树内唯一/全部调用点 | 用途 |
|---|---|---|
| `ThreadFor(object)` | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:109`（每趟解析一次并钉到 run 上）、`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:114`（没有 run 时的回落）、基类内部 4 处（`ThreadDispatcherBase.cs:15`/`:47`/`:54`/`:73`）、各家的 `CreateFramePacer` | 唯一的「解析线程」入口 |
| `Post(target, thread, action, priority)` | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:115` | **写路径的唯一出口**，线程是钉好的那条 |
| `Post(target, action, priority)`（三参便利重载） | **零调用者**（`Src/`、`Examples/`、`Src/Core/VeloxDev.Core.Test/` 全域搜不到） | 每次自己解析线程；写路径刻意不用它 |
| `PostAsync` | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:128` | 只给 `Awake` 用 |
| `Run<T>` | `Interpolator`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:147`） | 只给 `Prepare` 读起点用 |
| `IsCurrent(object)` | `Src/Adapters/VeloxDev.WinForms/PlatformAdapters/TransitionInterpreter.cs:10` | 全树唯一消费者：决定这家要不要 pacer |
| `IsCurrentFor(target, thread)` | 基类内部 3 处（`ThreadDispatcherBase.cs:50`/`:55`/`:74`） | **inline 还是 queue 的唯一判定** |
| `InternalPriority` | `ThreadDispatcherBase.cs:81` 一处 | 只服务于 `Run<T>` 的跨线程跳 |

两个推论：① 两个成员的 `IThreadAffinity` 里那个 `IsCurrent` 全树只有一个消费者 —— 它是给 pacer 用的谓词，不是给「我在不在 UI 线程」用的通用工具；② 三参 `Post` 是**没人用的便利面**，别以为写路径走它。

---

## 四、inline 还是 queue：判定在基类，平台不参与

`ThreadDispatcherBase.cs:50`：

```
Post(target, thread, action, priority)
  => IsCurrentFor(target, thread) ? RunInline(action) : PostCore(target, thread, action, priority);
```

- **适配器的 `PostCore` 永远不会在拥有 target 的那条线程上被调用**（除非它自己覆写 `IsCurrentFor` 并答 true）。所以 `PostCore` 里不需要「我已经在这条线程上了」这条分支。
- **Core 不再问「我是不是 UI 线程」**：只有 `IsCurrentFor` 一个判据，而它的默认实现是 `IsCurrentThread(thread)`（`:24`）。
- 覆写 `IsCurrentFor` 等于决定「UI 线程上的写要不要走消息泵」。七家里只有 WinForms 覆写（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:63`），问的是 `Control.InvokeRequired` —— 同一个问题的对偶问法（细节见 `memory/modules/TransitionSystem/adapters/winforms.md` §三.1）。
- inline 分支**绕过宿主排队**：没有优先级、没有排队延迟、`RunInline` 不 catch，异常直接抛在调用者线程上（`ThreadDispatcherBase.cs:89-93`）。

---

## 五、不对称：读阻塞、写即弃（本模块最重的一条）

| | 成员 | 行为 | 失败怎么表示 |
|---|---|---|---|
| 读 | `Run<T>` | **同步阻塞调用线程**直到宿主泵出（`:86` `GetAwaiter().GetResult()`），按 `InternalPriority` 排队（`:81`） | **零值**。`:83` 直接 `return default!` —— 与「读到的就是零值」不可区分（接口注释 `IThreadDispatcher.cs:44-47` 自己承认了这一点） |
| 写 | `Post` | 立即返回 bool（有没有入队） | `false`；调用方自己决定报不报 |
| 半等 | `PostAsync` | **只在动作真的被接受时才等**（`:64-65` 的中文注释：「宿主静默丢掉的动作永远不会完成它的 TCS，等下去就是等一辈子」）；没入队就 `return false` | `false` |
| 读（inline） | `PostAsync`/`Run` 的 inline 分支 | 直接跑，不投递 | —— |

**为什么读必须阻塞。** 唯一读者是 `Interpolator.Prepare`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:147`），它要在**发起动画的那条线程上、`Prepare` 的同一个栈帧里**拿到 target 的当前值。改成异步就要把 `Prepare` 改 async，并放松调度器写死的既有顺序：`Awake` 跑完（`await`）之前不得读 target（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:122-124`）。

**为什么写不能等。** 写是每帧每属性的（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:100-119`），采样循环一旦等 UI 线程就变成每帧一次 dispatch —— 正是采样路径要避免的事。

**为什么 `Awake` 走 `PostAsync`。** 它既要能靠 `Args.Handled` 否决，又必须让 `Prepare` 在它之后；`await` 完还要看 `awoken == false` = 宿主队列没了 ⇒ 放弃整趟（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:146-150`）。写路径不需要这个保证，所以是 `Post`。

**读的失败是零值，这条有实际后果（值得单独记）。** `Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:147` 拿到的是 `Run<object?>`，被拒时是 `null`；而 `:150` 只把 `TransitionProperty.UnreadablePath` 当哨兵，`null` 会被当成「当前值就是 null」继续流到 `NormalizeStart`（`:181`），`DoubleSampler.InsertFrame` 又把它当 0（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/DoubleSampler.cs:11` 的 `(double)(start ?? 0d)`）。⇒ **「宿主拒绝了这次读」静默变成「从 0 开始动画」**。要往读路径加东西，先决定失败长什么样；本仓库现成的答案是哨兵对象（`UnreadablePath`），不是 `null`。

---

## 六、不变量

违反下面任何一条通常**不报错**，只是静默行为错。

1. **`ThreadFor` 不得为调用方造句柄**（`IThreadDispatcher.cs:14-17` 的注释：`Dispatcher.CurrentDispatcher` 之流会给调用线程造一个没人泵的 dispatcher，消费者从此被钉死在上面，且无处上报）。
2. **`PostCore` 必须诚实报告入队。** 乐观 `true` 会让 `PostAsync` 的消费者挂到进程结束；而它唯一的消费者 `Awake` 是在**持有 scheduler 的 `_gate` 时** await 的（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:114` → `:128` → `finally` 在 `:178`），于是那条 `_gate` 永不释放：同一个 target 之后每一次动画都会排在它后面，永远不开始。这就是「一个乐观的 true 换一次永久挂死」的完整链条。
3. **`IsCurrentThread(ThreadRef)` 必须由宿主实现**，基类给不出（见 §二末）。
4. **有优先级的宿主必须覆写 `InternalPriority`**（`ThreadDispatcherBase.cs:39-44`）。它只影响 `Run<T>` 的跨线程跳（`:81`），也就是**动画启动前的那一次读**。默认 `default!`；WPF/Jalium/Avalonia 给 `Send`、WinUI 给 `Normal`（`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:48`）。
5. **`IsCurrentFor` 是 inline/queue 的唯一判据**（§四）。谎报 true = 写落在错的线程上，静默通过。
6. **`ThreadRef` 相等 = 句柄引用相等**（`ThreadRef.cs:36`）。各家的 `IsCurrentThread` 一律是「`TryGet<T>` 出自己的类型 → 问它」，其中两家直接 `ReferenceEquals(SynchronizationContext.Current, context)`（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:68-70`、`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:58-60`）。别在里面做值比较。
7. **`ThreadRef.None` 是答案不是失败**：`ThreadFor` 返 None ⇒ pacer 拿到 `null`（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/TransitionInterpreter.cs:10` 的 `IsNone ? null`；`Src/Adapters/VeloxDev.WPF/PlatformAdapters/TransitionInterpreter.cs:10-12` 的 `TryGet` 失败）⇒ 循环等在线程池定时器上；`PostCore` 遇到 None 应当返回 false（帧被丢，`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:117` 只报一次 `Warn("Dropped")`）。
8. **线程归属在「答案随调用者变化」的宿主上属于 run，不属于 target**：调度器在仍是启动线程时解析一次、写进 `TransitionRun.Thread`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:109` → `:170`；`Src/Core/VeloxDev.Core/TransitionSystem/TransitionRun.cs:39-45`），写路径只用钉好的那条（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:114`）。这类宿主上每帧重问会得到错的答案（平台细节见 `memory/modules/TransitionSystem/adapters/razor.md`）。

---

## 七、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| inline/queue 的判定、`Post`/`PostAsync`/`Run` 的语义 | `ThreadDispatcherBase.cs` |
| 契约本身（谁必须实现什么、返回值承诺） | `IThreadDispatcher.cs`（含 `IThreadAffinity`） |
| 「没有优先级」这个概念怎么表达 | `NonPriority.cs`，用法见 §八 |
| 句柄包装、`None`、相等语义 | `ThreadRef.cs` |
| 某家怎么找线程、怎么投递、怎么报存活 | `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/UIThreadInspector.cs` |
| 「宿主死了」这一位从哪来 | `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs`；消费者在 `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90` |
| 帧什么时候被投出去、用什么优先级 | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:100-119` + `Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:96-182` |

---

## 八、`NonPriority` 不是占位符 —— 它是「无优先级全体」的型参

容易被读成「给没有优先级的平台当占位」的一个空 struct，实际是**一整族的类型参数**：

- `Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:38-43`：非泛型基类 `TransitionEffectCore` **显式**实现 `ITransitionEffect<NonPriority>`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:41` 的 `NonPriority ITransitionEffect<NonPriority>.Priority`）。也就是说「无优先级」在 Core 里是一条**一等公民**的路径，不是缺省兜底。
- `Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:27-29`：`TransitionInterpreterCore<TTransitionEffectCore>`（单型参版）就是 `ITransitionInterpreter<NonPriority>` 那一支，注释（`:33`）写明它走 `frameSet.Apply(target, easedT)`，「`NonPriority` 每帧不花任何代价」。
- 三家适配器（MAUI / WinForms / Razor）的第七型参、`SamplerSet<NonPriority>`、以及 `Src/Core/VeloxDev.Core.Test/` 里几乎所有宿主都是它。
- 它是 `readonly struct` 且**从不被实例化**（全树搜不到 `new NonPriority`），实际流动的值是 `default(NonPriority)`（如 `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:100` 的 `priority = default!`）。空 struct 正是为此选的：`default` 是**真值**，所以 `is TPriorityCore` 这类检查（`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/Interpolator.cs:25`）不用为它开特例（`NonPriority.cs:6-10` 的注释）。
- 型参**无约束**（全树无 `where TPriorityCore`），所以 `default!` 与「传一个装箱的类」都能编译；这是刻意留下的自由度，不是遗漏。
