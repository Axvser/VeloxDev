# TimeLine 扩展

> 契约：`Src/Core/VeloxDev.Core/Interfaces/MonoBehaviour/`（**文件名含零宽空格**）。
> 实现：`Src/Core/VeloxDev.Core/TimeLine/`。生成器：`Src/Generators/VeloxDev.Core.Generator/Writers/MonoWriter.cs`。
> 架构与不变量见同目录 `architecture.md`。

本模块的扩展点只有**一个**是给用户的：**写一个派生自生成器的 MonoBehaviour 式行为**。其余（事件参数族）是给 `TransitionSystem` 消费的，不该在这里扩。

---

## 一、扩展点地图

| 扩展点 | 在哪 | 谁该用 |
|---|---|---|
| `[MonoBehaviour(channel, fps)]` | `TimeLine/MonoBehaviourAttribute.cs:7` | **首选**：声明一个行为 |
| 生成的 `InitializeMonoBehaviour()` / `CloseMonoBehaviour()` | `MonoWriter.cs:81-89` | 与上一条配对，**必须由你调** |
| 生成的五个 `partial void` 钩子：`Awake`/`Start`/`Update`/`LateUpdate`/`FixedUpdate` | `MonoWriter.cs:116-120` | 上面那条的实现位置 |
| `MonoBehaviourManager.RegisterBehaviour/UnregisterBehaviour`（实例版） | `MonoBehaviourManager.cs:431`、`:436` | 手写 `IMonoBehaviour` 实现时用（**不推荐**，见 §二·2） |
| `MonoBehaviourManager.Bus(channel)`（实例版） | `MonoBehaviourManager.cs:1145` | 让一条动画与 channel 共用时间轴 |
| 全局 channel 事件 `OnChannelStarted/Paused/Resumed/Stopped` | `MonoBehaviourManager.cs:1040-1043` | 观察 channel 生命周期 |
| `MonoBehaviourChannelEventArgs`（含 `ChannelName`） | `MonoBehaviourManager.cs:1160-1163` | 上一条的参数 |
| `TimeLineEventArgs` | `TimeLineEventArgs.cs` | 只给 `TransitionSystem` 那侧用；不要在这里加派生类型 |
| `ExecuteOnMainThread` | `MonoBehaviourManager.cs:241`、`:1086` | **死扩展点**，见 §五 |

**不需要扩展的**：`IMonoBehaviour` 本身（名字里带零宽空格，见 §二·2；且实现全由生成器产出）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 定一个行为 —— 生成器，而不是手写接口

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `[MonoBehaviour("我的通道", fps: 30)] public partial class Foo { partial void Update(FrameEventArgs e) { ... } }`，然后在合适的时机调 `InitializeMonoBehaviour()` | 生成器补一个 partial 部分，实现 `IMonoBehaviour` 的七个成员，把 `Invoke*` 转成你那五个 `partial void` |
| ❌ 捷径 | 手写 `class Foo : IMonoBehaviour { public void InvokeUpdate(FrameEventArgs e) ... }` | **名字写不对**：契约的类型名是 `IMonoBehaviour`（带 ZWSP），你把接口名抄成不带 ZWSP 的样子**能编译**（编译器忽略 Cf 字符，元数据里也剥掉了），但成员 `InitializeMonoBehaviour()` 也带 ZWSP——抄漏一处就 `CS0535` 未实现。而且你放弃了生成器的 `SetTargetFPS` 展开与钩子转发 |
| ❌ 捷径 | 贴了 `[MonoBehaviour]` 就以为生效 | 特性**只有生成器读**（`MonoWriter.cs:19-50`），运行时的 `MonoBehaviourManager` 从不反射它。而不调 `InitializeMonoBehaviour()` 就永远不注册（生成器也不会替你调） |
| ❌ 捷径 | `[MonoBehaviour]` 贴在**非 partial** 类上 | 生成器**静默跳过**：`Analizer.cs:157-161` 的 `IsCandidateClass` 要求 `IsPartialClass(declaration)`。编译通过、什么都不生成 |
| ⚠️ 注意 | `fps` 参数传 `-1`（默认） | `MonoWriter.cs:76-78` 的 `TargetFPS >= 1` 不成立，**整句 `SetTargetFPS` 不生成**。想要帧率就得写正数 |

### 2. 生命周期 —— 用钩子，不要在构造函数里注册

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 在钩子（`Awake`/`Start`）里准备状态；注册由调用方在合适的时机调 `InitializeMonoBehaviour()` | `Awake`/`Start` 保证在**第一帧体之前**、（同一批注册的）按队列顺序跑（`MonoBehaviourManager.cs:786-802`），且都在更新线程上 |
| ❌ 捷径 | 构造函数里 `RegisterBehaviour(this)` | 入队的是 `this`，而 `ProcessAddedBehaviors` 会立刻 `InvokeAwake` + `InvokeStart`（`:797-798`）——**对象的构造还没走完就先跑了 Awake**。`Examples/MonoBehaviour/WPF/Demo/MainWindow.xaml.cs:57` 是在 `Loaded` 里调的，不是构造里 |
| ❌ 捷径 | 依赖「停 channel 再启」来重跑 `Awake`/`Start` | **不会重跑**。`StopAsync` → `Start`（`:405-429`）只是换线程与重置统计；只有**重新注册**才会（`:796-798`）。WPF demo 专门做了这对按钮演示（`MainWindow.xaml.cs:111-135`） |
| ⚠️ 陷阱 | 没先 `CloseMonoBehaviour()` 就再注册一次 | `Awake` 会跑两次，旧 wrapper 变成无引用对象（`:794-800`） |

### 3. 钩子里跨线程 —— 发数据出去，不要 `Dispatcher.Invoke` 回来

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 钩子里算完，把结果写进一个不可变报告/队列（`Interlocked`/`Volatile`），UI 侧**轮询** | 见 `Examples/MonoBehaviour/WPF/Demo/MainWindow.Hooks.cs:193-213`（`Report`）与 `:223-230`（`RecordIf`） |
| ❌ 捷径 | 在钩子里 `Dispatcher.Invoke(...)` 回 UI 线程 | 异常被 `SafeExecute` / 逐钩子 catch 吞掉（`MonoBehaviourManager.cs:700,716,732` 与 `:940-943`），症状是「循环还在跑但界面停止刷新，且哪儿都没有日志」。WPF demo 的窗口侧注释把这条写成了它的整体设计理由（`MainWindow.xaml.cs:12-19`） |

### 4. 让动画与 channel 共用时间轴 —— 传 `Bus`，不要各用一条

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `Transition<T>.Execute(target, timeline: MonoBehaviourManager.Bus(channel))` | 一次 `Pause()` 同时冻住帧回调与动画，rate 同时乘两者（`MonoBehaviourManager.cs:1135-1144`、`:189` 的注释写死了用途） |
| ❌ 捷径 | 动画用默认时间轴 | 两条钟各自走：暂停 channel 动画不停，反之亦然 |
| ❌ 捷径 | 用 `MonoBehaviourManager.Bus(channel)` 去**创建** channel | 它对不存在的 channel 返回 `null`，**刻意不创建**（`:1135-1144`）。要创建得调 `Start`/`RegisterBehaviour` 之类的命令面 |

### 5. 暂停 / 倍速 —— 用 channel 的，不要另建一套

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `MonoBehaviourManager.Pause/Resume/SetTimeScale(channel)` | 全部落在 channel 的总线上（`:239`、`:388`、`:401`），停摆时两条泵 park、**零唤醒**（`:466-468`） |
| ❌ 捷径 | 自己在一个 `bool _frozen` 上判、在钩子里 `return` | 泵仍在按目标帧率唤醒，暂停的 channel 还在烧 CPU；而且 `TotalTime` 会继续走，因为 `TotalTime` 是**总线**的，不是你的标志的（`:819-823`、`:907-914`） |
| ⚠️ 陷阱 | `SetTimeScale(0)` 之后调 `Resume()` | 不会恢复。rate 为 0 是「冻结不是暂停」，`Resume` 只解除暂停（`Timing/TimeSourceCore.cs:252-258`）。要恢复得把 rate 调回非零。demo 的单元格工具提示明说这条（`MainWindow.xaml.cs:322-324`） |

### 6. 目标帧率与固定步长 —— 走对公器的两个入口

| 想改 | 用 | 为什么 |
|---|---|---|
| 目标帧率 | `SetTargetFPS(fps, channel)` | 入队，由**更新泵**在帧体里同时改 `_targetFPS` 与 `_cachedTargetFrameDurationTicks`（`:769-784`）——两者必须同时生效 |
| 固定步长 | `SetFixedUpdateInterval(ms, channel)` | **不走配置队列**，用 `_pendingFixedIntervalMs` 交给**固定泵自己的线程**落（`:222-226` + `:459-464`）。从更新线程写 `Step` 会与 `Advance` 争它要重置的累加器 |
| ❌ 捷径 | 直接改 `FrameEventArgs.TargetFPS` | 字段是 `internal set`（`FrameEventArgs.cs:25`），你改不了；就算能改也只影响这一帧的读数 |

---

## 三、步骤清单：新增一个 MonoBehaviour 式行为

1. **确认你要的是这条循环**：需要一条与 UI 渲染无关、能独立调速、暂停时不占 CPU 的帧泵。若你要的是「UI 线程上出帧的动画」，用 `TransitionSystem`，别用这里。
2. **建类，标 `partial`**（不 partial 生成器静默跳过，`Analizer.cs:157-161`）。
3. **贴特性**：`[MonoBehaviour("通道名")]`；要帧率就写 `fps: 正数`（`-1` 不生成 `SetTargetFPS`）。通道名建议用 `nameof(你的类)`（参照 `TreeHelper.cs:30`），或 `MonoBehaviourManager.DEFAULT_CHANNEL`。
4. **实现需要的 `partial void` 钩子**（可以一个都不实现——钩子是 `partial void`，不实现就是空体，`MonoWriter.cs:116-120`）。
5. **在合适的时机调 `InitializeMonoBehaviour()`**。参考顺序（`Examples/MonoBehaviour/WPF/Demo/MainWindow.xaml.cs:55-60`）：注册 → 配帧率 → 启动 channel。注释写死了理由：注册只入队，由更新泵在其第一帧体里取走并触发 `Awake`/`Start`，所以这个顺序不影响「生命周期先于第一帧」。
6. **启动 channel**：`MonoBehaviourManager.Start(通道名)`。**这一步不可省**——入队不等于有人排空队列。
7. **在同一时机成对调 `CloseMonoBehaviour()`**（通常在窗口/控件关闭处），并 `StopAsync`（不 await 是合理的：两条泵都是后台线程，`MainWindow.xaml.cs:68-69`）。
8. **钩子里的数据只往外发，不往回编组**：写 `Interlocked`/`Volatile` 字段 + 队列，UI 侧轮询。
9. **若要动画共享这条时间轴**，把 `MonoBehaviourManager.Bus(通道名)` 传给 `Execute(target, timeline)`。
10. **加测试**：参照 `Src/Core/VeloxDev.Core.Test/TimeLine/MonoBehaviourManagerTests.cs`（channel 生命周期）、`MonoBehaviourBusTests.cs`（总线与动画共享）、`MonoBehaviourAttributeTests.cs`、`TimeLineEventArgsTests.cs`。

---

## 四、联动清单

改本模块时必须同步检查的位置：

| 改动 | 联动 |
|---|---|
| 加/改 `IMonoBehaviour` 的成员 | ① 契约文件（**文件名与标识符里的 ZWSP 得跟着走**）② `MonoWriter.cs:80-121` 的模板与 `GenerateBaseInterfaces`（`:64-67`）③ 五个 `partial void` 声明 ④ `Src/Core/VeloxDev.Core.Test/TimeLine/` ⑤ `Examples/MonoBehaviour/WPF/Demo/` |
| 加/改 `[MonoBehaviour]` 的参数 | ① `MonoBehaviourAttribute.cs` ② `MonoWriter.cs:32-48`（位置参数 + 命名参数两条读取路径，**命名参数覆盖位置参数**）③ `MonoBehaviourAttributeTests.cs` |
| 改 `TriggerAttributes` 的组合方式 | `Src/Generators/VeloxDev.Core.Generator/Base/Analizer.cs:82-94`；`"VeloxDev.TimeLine.MonoBehaviourAttribute"` 在 `:90`。**它同时决定了「哪些类会进入生成器」**，删掉它等于整个特性失效 |
| 改事件参数族的形状 | `TransitionSystem`（`TransitionDiagnostics.cs:44`、`TransitionInterpreter.cs:199,203,289`、`Transition.cs:340`）+ `TimeLineEventArgsTests.cs` |
| 改 channel 的静态 API 面 | `MonoBehaviourManager.cs:1049-1155` 七组；注意**命令面走 `GetOrCreateChannel`、查询面走 `TryGetValue`**（`:1145` 的注释写死了这条不对称） |
| 加一条「跨线程可见」的字段 | 检查它的写者是否只在一条泵的线程上：`_state` 那种「一条泵一个字段」的写法见 `Examples/MonoBehaviour/WPF/Demo/MainWindow.Hooks.cs:39-48` |
| 改暂停/倍速语义 | 实现全在 `Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs`；本模块只转发（`:239`、`:388`、`:401`） |
| 改固定步长 | `Timing/CompensatingTimeSampler.cs`（`Step` 的 setter 会清 `_acc`） |

> **注意**：本模块在 `Docs/` 那套文档站点里另有一套公开文档，**不在本仓库里**（`.gitignore:461` 排除，`git ls-files Docs/` 为空）。它与本仓库各自独立、不随本仓库的改动一起记账 —— **本文的一切依据都只取本仓库**。

**唯一的示例工程**：`Examples/MonoBehaviour/WPF/Demo/`（TFM `net10.0-windows`）。它不是「顺手写的 demo」，而是本模块的**可执行规格**：两球同权重力学、`Drift = Σdt²`、`EffectiveDt = Σdt²/Σdt`，把两条泵的节奏做成可测量的量（`MainWindow.Hooks.cs:1-23` 的头部注释是全部设计意图）。改本模块前先跑它。

---

## 五、死扩展点与已失效的钩子

| 扩展点 | 状态 | 依据 |
|---|---|---|
| `ExecuteOnMainThread` | **零调用者**。`Src/` + `Examples/` 下只有定义（`:241`）与静态转发（`:1086`）。且它的「Main」是 channel 的**更新线程**，不是 UI 线程 | grep `ExecuteOnMainThread` |
| `ThreadSafeFrameEventArgs` | **零生产者、零消费者**。泵只造 `FrameEventArgs`（`:827`）。它的 `public new bool Handled` 还遮蔽了基类的虚属性，按基类引用读会永远读到 `false` | grep `ThreadSafeFrameEventArgs`（只命中定义与 `Src/Core/VeloxDev.Core.Test/TimeLine/TimeLineEventArgsTests.cs`） |
| `TimeLineEventArgs.Handled` 是 `virtual` | **全仓库零 `override`**。它 `virtual` 而不是普通属性，本意大概是给 `ThreadSafeFrameEventArgs` 那种重写留位，但那个类用了 `new` | grep `override bool Handled` 无命中 |
| `IMonoBehaviour.Invoke*` 系列 | **只能由泵调**，手写实现在仓库内不存在。不要把它们当公开 API 用 | `MonoBehaviourManager.cs:699,715,731` 是唯一的调用点 |
| 通道级 `_useAsyncLoopOverride` | 活的，但**只在 channel 未运行时能设**（`:250-252`） | — |

**这意味着**：本模块真正活的扩展面只有「生成器 + 五个 `partial void` + `InitializeMonoBehaviour`」这一条。`ExecuteOnMainThread` 与 `ThreadSafeFrameEventArgs` 是两条**建好了但没接上**的路——遇到「我需要从钩子回到 UI 线程」时，先看这两条是不是能接上，再接；不要因为「看起来有现成的」就假定它在工作。
