# TimeLine 扩展

> 契约：`Src/Core/VeloxDev.Core/Interfaces/Tickable/`。
> 实现：`Src/Core/VeloxDev.Core/TimeLine/`。生成器：`Src/Generators/VeloxDev.Core.Generator/Writers/TickWriter.cs`。
> 架构与不变量见同目录 `architecture.md`。

本模块的扩展点只有**一个**是给用户的：**写一个派生自生成器的 Tickable 式行为**。其余（事件参数族）是给 `TransitionSystem` 消费的，不该在这里扩。

---

## 一、扩展点地图

| 扩展点 | 在哪 | 谁该用 |
|---|---|---|
| `[Tickable(channel, fps)]` | `TimeLine/TickableAttribute.cs:7` | **首选**：声明一个行为 |
| 生成的 `InitializeTickable()` / `CloseTickable()` | `TickWriter.cs:86-94` | 与上一条配对，**必须由你调** |
| 生成的五个 `partial void` 钩子：`Awake`/`Start`/`Update`/`LateUpdate`/`FixedUpdate` | `TickWriter.cs:121-125` | 上面那条的实现位置 |
| `TickManager.RegisterBehaviour/UnregisterBehaviour`（实例版） | `TickManager.cs:432`、`:437` | 手写 `ITickable` 实现时用（**不推荐**，见 §二·2） |
| `TickManager.Bus(channel)`（实例版） | `TickManager.cs:1146` | 让一条动画与 channel 共用时间轴 |
| 全局 channel 事件 `OnChannelStarted/Paused/Resumed/Stopped` | `TickManager.cs:1041-1044` | 观察 channel 生命周期 |
| `TickChannelEventArgs`（含 `ChannelName`） | `TickManager.cs:1162-1165` | 上一条的参数 |
| `TimeLineEventArgs` | `TimeLineEventArgs.cs` | 只给 `TransitionSystem` 那侧用；不要在这里加派生类型 |
| `ExecuteOnMainThread` | `TickManager.cs:242`、`:1087` | **死扩展点**，见 §五 |

**不需要扩展的**：`ITickable` 本身（实现全由生成器产出）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 定一个行为 —— 生成器，而不是手写接口

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `[Tickable("我的通道", fps: 30)] public partial class Foo { partial void Update(FrameEventArgs e) { ... } }`，然后在合适的时机调 `InitializeTickable()` | 生成器补一个 partial 部分，实现 `ITickable` 的七个成员，把 `Invoke*` 转成你那五个 `partial void` |
| ❌ 捷径 | 手写 `class Foo : ITickable { public void InvokeUpdate(FrameEventArgs e) ... }` | 你要自己实现全部七个成员（`InitializeTickable` / `CloseTickable` / 五个 `Invoke*`，模板见 `TickWriter.cs:86-125`），并且放弃生成器的 `SetTargetFPS` 展开与钩子转发 —— 抄漏一个成员就是 `CS0535`。**注意标识符里曾是零宽空格的历史已清除**（见 `architecture.md` §八·1），现在按普通拼写写 `ITickable` 即可 |
| ❌ 捷径 | 贴了 `[Tickable]` 就以为生效 | 特性**只有生成器读**（`TickWriter.cs:20-51`），运行时的 `TickManager` 从不反射它。而不调 `InitializeTickable()` 就永远不注册（生成器也不会替你调） |
| ❌ 捷径 | `[Tickable]` 贴在**非 partial** 类上 | 生成器**静默跳过**：`Analizer.cs:170-174` 的 `IsCandidateClass` 要求 `IsPartialClass(declaration)`。编译通过、什么都不生成 |
| ⚠️ 注意 | `fps` 参数传 `-1`（默认） | `TickWriter.cs:81-83` 的 `TargetFPS >= 1` 不成立，**整句 `SetTargetFPS` 不生成**。想要帧率就得写正数 |

### 2. 生命周期 —— 用钩子，不要在构造函数里注册

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 在钩子（`Awake`/`Start`）里准备状态；注册由调用方在合适的时机调 `InitializeTickable()` | `Awake`/`Start` 保证在**第一帧体之前**、（同一批注册的）按队列顺序跑（`TickManager.cs:787-803`），且都在更新线程上 |
| ❌ 捷径 | 构造函数里 `RegisterBehaviour(this)` | 入队的是 `this`，而 `ProcessAddedBehaviors` 会立刻 `InvokeAwake` + `InvokeStart`（`:798-799`）——**对象的构造还没走完就先跑了 Awake**。`Examples/Tickable/WPF/Demo/MainWindow.xaml.cs:48` 是在 `Loaded` 里调的，不是构造里 |
| ❌ 捷径 | 依赖「停 channel 再启」来重跑 `Awake`/`Start` | **不会重跑**。`StopAsync` → `Start`（`:406-430`）只是换线程与重置统计；只有**重新注册**才会（`:797-799`）。WPF demo 专门做了这对按钮演示（`MainWindow.xaml.cs:105-119`） |
| ⚠️ 陷阱 | 没先 `CloseTickable()` 就再注册一次 | `Awake` 会跑两次，旧 wrapper 变成无引用对象（`:795-802`） |

### 3. 钩子里跨线程 —— 发数据出去，不要 `Dispatcher.Invoke` 回来

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 钩子里算完，把结果写进一个不可变报告/队列（`Interlocked`/`Volatile`），UI 侧**轮询** | 见 `Examples/Tickable/WPF/Demo/MainWindow.Hooks.cs:173-193`（`Report`）与 `:198-205`（`RecordIf`） |
| ❌ 捷径 | 在钩子里 `Dispatcher.Invoke(...)` 回 UI 线程 | 异常被 `SafeExecute` / 逐钩子 catch 吞掉（`TickManager.cs:701,717,733` 与 `:941-944`），症状是「循环还在跑但界面停止刷新，且哪儿都没有日志」。WPF demo 的钩子侧头部注释把这条写成了它的整体设计理由（`MainWindow.Hooks.cs:15`） |

### 4. 让动画与 channel 共用时间轴 —— 传 `Bus`，不要各用一条

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `Transition<T>.Execute(target, timeline: TickManager.Bus(channel))` | 一次 `Pause()` 同时冻住帧回调与动画，rate 同时乘两者（`TickManager.cs:1136-1145`、`:190` 的注释写死了用途） |
| ❌ 捷径 | 动画用默认时间轴 | 两条钟各自走：暂停 channel 动画不停，反之亦然 |
| ❌ 捷径 | 用 `TickManager.Bus(channel)` 去**创建** channel | 它对不存在的 channel 返回 `null`，**刻意不创建**（`:1136-1145`）。要创建得调 `Start`/`RegisterBehaviour` 之类的命令面 |

### 5. 暂停 / 倍速 —— 用 channel 的，不要另建一套

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `TickManager.Pause/Resume/SetTimeScale(channel)` | 全部落在 channel 的总线上（`:240`、`:386`、`:399`），停摆时两条泵 park、**零唤醒**（`:467-469`） |
| ❌ 捷径 | 自己在一个 `bool _frozen` 上判、在钩子里 `return` | 泵仍在按目标帧率唤醒，暂停的 channel 还在烧 CPU；而且 `TotalTime` 会继续走，因为 `TotalTime` 是**总线**的，不是你的标志的（`:820-824`、`:908-915`） |
| ⚠️ 陷阱 | `SetTimeScale(0)` 之后调 `Resume()` | 不会恢复。rate 为 0 是「冻结不是暂停」，`Resume` 只解除暂停（`Timing/TimeSourceCore.cs:260-266`）。要恢复得把 rate 调回非零。demo 的单元格工具提示明说这条（`MainWindow.xaml.cs:291-294`） |

### 6. 目标帧率与固定步长 —— 走对公器的两个入口

| 想改 | 用 | 为什么 |
|---|---|---|
| 目标帧率 | `SetTargetFPS(fps, channel)` | 入队，由**更新泵**在帧体里同时改 `_targetFPS` 与 `_cachedTargetFrameDurationTicks`（`:770-785`）——两者必须同时生效 |
| 固定步长 | `SetFixedUpdateInterval(ms, channel)` | **不走配置队列**，用 `_pendingFixedIntervalMs` 交给**固定泵自己的线程**落（`:223-227` + `:460-465`）。从更新线程写 `Step` 会与 `Advance` 争它要重置的累加器 |
| ❌ 捷径 | 直接改 `FrameEventArgs.TargetFPS` | 字段是 `internal set`（`FrameEventArgs.cs:26`），你改不了；就算能改也只影响这一帧的读数 |

---

## 三、步骤清单：新增一个 Tickable 式行为

1. **确认你要的是这条循环**：需要一条与 UI 渲染无关、能独立调速、暂停时不占 CPU 的帧泵。若你要的是「UI 线程上出帧的动画」，用 `TransitionSystem`，别用这里。
2. **建类，标 `partial`**（不 partial 生成器静默跳过，`Analizer.cs:170-174`）。
3. **贴特性**：`[Tickable("通道名")]`；要帧率就写 `fps: 正数`（`-1` 不生成 `SetTargetFPS`）。通道名建议用 `nameof(你的类)`（参照 `TreeHelper.cs:33`），或 `TickManager.DEFAULT_CHANNEL`。
4. **实现需要的 `partial void` 钩子**（可以一个都不实现——钩子是 `partial void`，不实现就是空体，`TickWriter.cs:121-125`）。
5. **在合适的时机调 `InitializeTickable()`**。参考顺序（`Examples/Tickable/WPF/Demo/MainWindow.xaml.cs:43-52`）：注册 → 配帧率 → 启动 channel。注释写死了理由：注册只入队，由更新泵在其第一帧体里取走并触发 `Awake`/`Start`，所以这个顺序不影响「生命周期先于第一帧」。
6. **启动 channel**：`TickManager.Start(通道名)`。**这一步不可省**——入队不等于有人排空队列。
7. **在同一时机成对调 `CloseTickable()`**（通常在窗口/控件关闭处），并 `StopAsync`（不 await 是合理的：两条泵都是后台线程，`MainWindow.xaml.cs:58-60`）。
8. **钩子里的数据只往外发，不往回编组**：写 `Interlocked`/`Volatile` 字段 + 队列，UI 侧轮询。
9. **若要动画共享这条时间轴**，把 `TickManager.Bus(通道名)` 传给 `Execute(target, timeline)`。
10. **加测试**：参照 `Src/Core/VeloxDev.Core.Test/TimeLine/TickManagerTests.cs`（channel 生命周期）、`TickableBusTests.cs`（总线与动画共享）、`TickableAttributeTests.cs`、`TimeLineEventArgsTests.cs`。

---

## 四、联动清单

改本模块时必须同步检查的位置：

| 改动 | 联动 |
|---|---|
| 加/改 `ITickable` 的成员 | ① 契约文件 `Interfaces/Tickable/ITickable.cs`（标识符里曾有的 ZWSP 已于 2026-10-01 清除，见 `architecture.md` §八·1）② `TickWriter.cs:85-126` 的模板与 `GenerateBaseInterfaces`（`:68-71`）③ 五个 `partial void` 声明 ④ `Src/Core/VeloxDev.Core.Test/TimeLine/` ⑤ `Examples/Tickable/WPF/Demo/` |
| 加/改 `[Tickable]` 的参数 | ① `TickableAttribute.cs` ② `TickWriter.cs:33-49`（位置参数 + 命名参数两条读取路径，**命名参数覆盖位置参数**）③ `TickableAttributeTests.cs` |
| 改 `TriggerAttributes` 的组合方式 | `Src/Generators/VeloxDev.Core.Generator/Base/Analizer.cs:95-107`；`"VeloxDev.TimeLine.TickableAttribute"` 在 `:103`。**它同时决定了「哪些类会进入生成器」**，删掉它等于整个特性失效 |
| 改事件参数族的形状 | `TransitionSystem`（`TransitionDiagnostics.cs:42`、`TransitionInterpreter.cs:204,208,294`、`Transition.cs:350`）+ `TimeLineEventArgsTests.cs` |
| 改 channel 的静态 API 面 | `TickManager.cs:1050-1156` 七组；注意**命令面走 `GetOrCreateChannel`、查询面走 `TryGetValue`**（`:1146` 的注释写死了这条不对称） |
| 加一条「跨线程可见」的字段 | 检查它的写者是否只在一条泵的线程上：`_state` 那种「一条泵一个字段」的写法见 `Examples/Tickable/WPF/Demo/MainWindow.Hooks.cs:33-43` |
| 改暂停/倍速语义 | 实现全在 `Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs`；本模块只转发（`:240`、`:386`、`:399`） |
| 改固定步长 | `Timing/CompensatingTimeSampler.cs`（`Step` 的 setter 会清 `_acc`） |

> **注意**：本模块在 `Docs/` 那套文档站点里另有一套公开文档，**不在本仓库里**（`.gitignore:461` 排除，`git ls-files Docs/` 为空）。它与本仓库各自独立、不随本仓库的改动一起记账 —— **本文的一切依据都只取本仓库**。

**唯一的示例工程**：`Examples/Tickable/WPF/Demo/`（TFM `net10.0-windows`）。它不是「顺手写的 demo」，而是本模块的**可执行规格**：两球同权重力学、`Drift = Σdt²`、`EffectiveDt = Σdt²/Σdt`，把两条泵的节奏做成可测量的量（`MainWindow.Hooks.cs:1-16` 的头部注释是全部设计意图）。改本模块前先跑它。

---

## 五、死扩展点与已失效的钩子

| 扩展点 | 状态 | 依据 |
|---|---|---|
| `ExecuteOnMainThread` | **零调用者**。`Src/` + `Examples/` 下只有定义（`:242`）与静态转发（`:1087`）。且它的「Main」是 channel 的**更新线程**，不是 UI 线程 | grep `ExecuteOnMainThread` |
| `TimeLineEventArgs.Handled` 是 `virtual` | **全仓库零 `override`，而且现在没有候选者了**：唯一想重写它的 `ThreadSafeFrameEventArgs` 用了 `new` 遮蔽，已于 2026-10-04 删除。这个 `virtual` 今天纯属多余 —— 但删它是 API 变更，另说 | grep `override bool Handled` 无命中 |
| `ITickable.Invoke*` 系列 | **只能由泵调**，手写实现在仓库内不存在。不要把它们当公开 API 用 | `TickManager.cs:701,717,733` 是唯一的调用点 |
| 通道级 `_useAsyncLoopOverride` | 活的，但**只在 channel 未运行时能设**（`:252-253`） | — |

**这意味着**：本模块真正活的扩展面只有「生成器 + 五个 `partial void` + `InitializeTickable`」这一条。`ExecuteOnMainThread` 是唯一剩下那条**建好了但没接上**的路——遇到「我需要从钩子回到 UI 线程」时，先看它是不是能接上，再接；不要因为「看起来有现成的」就假定它在工作。（另一条同类的 `ThreadSafeFrameEventArgs` 已删除，理由见 `architecture.md` §八·5。）
