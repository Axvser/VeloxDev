# Lifetime — 架构

> 代码：`Src/Core/VeloxDev.Core/Lifetime/`，**一个文件**：`IApplicationState.cs`（命名空间 `VeloxDev.Lifetime`）。
> 唯一的消费者是 TransitionSystem 的宿主接缝：`Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ITransitionHost.cs:14` 把它与 `Src/Core/VeloxDev.Core/Threading/IThreadDispatcher.cs` 合成一个接口；实现通过 `Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs` 拿到它。
> 七家平台在这条轴上的差异**已经写全**在 `memory/modules/TransitionSystem/adapters/<平台>.md`（总表在 `memory/modules/TransitionSystem/adapters/avalonia.md` 的 §三.1），本文只给代码锚点与指路，不重复七遍。

本文只写「读完这一个文件 + 追完它的读者写者才知道的东西」。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一个 bool 的**读/写分离**：宿主往里报「我还活着」，消费者问它。这个 bool 的唯一作用地点是**动画还能不能写值**。

**不解决什么：**

| 你以为在这里 | 实际在哪 |
|---|---|
| 「宿主死了」这件事怎么被观察到 | 各家 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/UIThreadInspector.cs`；本模块只提供**存**这个结论的地方 |
| 宿主死了要不要**结束**动画 | 不是这里，也不是任何地方。见 §三 |
| 通知机制（事件/回调） | 不存在。接口只有一个 getter（`:7`），消费者只能**轮询** |
| 「线程还能用吗」 | `Src/Core/VeloxDev.Core/Threading/IThreadDispatcher.cs` 的 `Post` 返回值（关停后投递被拒是那条轴的报告方式）。两条轴各报各的 |

---

## 二、流向：全树就这么几条边

| 角色 | 位置 |
|---|---|
| 谁**持有** | `TransitionHostBase<TPriorityCore>.Lifetime`（`protected`，`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:12`）—— 每家适配器的宿主都持有一份；但**六家从不把它变成 false**：MAUI/WinForms/Razor 覆写了 `IsAlive`（连读都不经过它），WPF/Jalium/Avalonia 走它的默认读路径却从不写。只有 WinUI 真的写 |
| 默认怎么读 | `TransitionHostBase.IsAlive => Lifetime.IsAlive`（`:14`，`virtual`） |
| 谁**读**（消费者） | `SamplerSet.CanSetValue() => _host.IsAlive`（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90`） |
| 读到之后 | `SamplerSet.Apply` 排队**之前**（`:103`）与 `ApplyCore` **逐条属性写之前**（`:130`）各拦一次 |
| 谁**写** | `ApplicationState.SetAlive(bool)`（`:22`）—— 树内唯一调用点是 `Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:56` |
| 还有谁引用 `IApplicationState` | 没有。全树只有四处：定义（`IApplicationState.cs:4`）、合成（`Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ITransitionHost.cs:14`）、默认实现（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:1,12,14`）、读（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90`） |

⇒ **整个模块的消费者集合 = 一个 bool 喂给一处 `CanSetValue()`**。它是最小的 Core 模块，但这一条边决定「帧还写不写得进去」。

---

## 三、语义边界（代码比注释更准的地方）

1. **初值是「活着」**（`:13` `private volatile bool _isAlive = true`）。⇒ **不接线就恒为 alive**，且没有任何地方会因为「你声明了却从不写」而报错。
2. **它是 hint，不是真相**（接口注释 `:6`：「a hint, never a reason to throw」）。`IsAlive == false` 不要求任何人抛异常，也**不结束动画**。
3. **`CanSetValue() == false` 的精确后果**（三处合起来看）：帧不再入队（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:103` 直接 return）＋ 已排队的帧落地后也不写（`:130` 在 `foreach` **里面**，逐条检查）＋ **循环本身照跑** —— `Update`/`LateUpdate`/`Completed` 继续触发，时间照走，直到有人 `Exit`。也就是说「宿主死了」表现为**动画还在跑但一个值都写不出去**，不报错、不结束。
4. **刻意双向**（`:17-21` 的 remark）：`SetAlive(false)` 之后还能 `SetAlive(true)`。理由写在注释里 —— WinUI 的信号来自**单次** `TryEnqueue` 的返回值（每次投递都可能瞬时被拒），单向会把一次瞬时拒绝变成**永久判死且无日志**。
5. **`IsAlive` 会被每帧每属性读一次**：`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:130` 的检查在 `foreach (var entry in _entries)` 内。⇒ 实现必须廉价且**不得抛**（MAUI 的实现里为 `Application.Current` 存在但 dispatcher 没建好留了 try/catch，`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/UIThreadInspector.cs:16-25` 是同一考虑的邻居）。
6. **只有一个实现**：具体类 `ApplicationState`（`sealed`，`:11`）是全树唯一的 `IApplicationState` 实现；没有哪家适配器自己实现接口，都是通过 `TransitionHostBase` 拿到。

---

## 四、七家怎么满足这条轴（代码复核过；细节请读那七份）

| 满足方式 | 家 | 代码锚点 |
|---|---|---|
| **覆写 `IsAlive`**（能问就问） | MAUI / WinForms / Razor | `Src/Adapters/VeloxDev.MAUI/PlatformAdapters/UIThreadInspector.cs:8`；`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:51`（静态 `_isAppAlive`，初值 `true`，只在捕获成功那支挂 `ApplicationExit`，`:40`）；`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:40`（`_isAppRunning`，靠外部调 `NotifyShutdown()`，`:22`） |
| **报进 `Lifetime.SetAlive`**（能观测就报） | WinUI | `Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:56` `Lifetime.SetAlive(queue.TryEnqueue(priority, …))`，双向（`:54` 的注释） |
| **不覆写，在 `PostCore` 里问平台并丢弃** | WPF / Jalium | `Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs:34`、`Src/Adapters/VeloxDev.Jalium/PlatformAdapters/UIThreadInspector.cs:36` 的 `dispatcher.HasShutdownStarted` ⇒ 返 `false` |
| **一个存活信号都没有** | Avalonia | `Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/UIThreadInspector.cs:16-22` 既无 `HasShutdownStarted` 也无 `IsAlive` 覆写 ⇒ 基类默认恒 `true`（理由是这家 `Dispatcher` 只有 `ShutdownStarted`/`ShutdownFinished` **事件**、没有可查询属性，见 `memory/modules/TransitionSystem/adapters/avalonia.md` §三.1） |

**这张表里最容易被漏读的一列是「能不能让 `CanSetValue()` 变 false」**：只有上表前两行（4 家）能。WPF/Jalium 的 `IsAlive` **永远是 true**，它们的存活只走 Threading 那条轴（`Post` 返 false → `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:117` 报一次 `Warn("Dropped")`）。所以「七家都有存活机制」是错的，「四家的写路径会因为宿主死而停、两家只丢帧、一家什么都不报」才对。各家理由与代价见 `memory/modules/TransitionSystem/adapters/<平台>.md`。
