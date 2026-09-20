# Lifetime — 扩展

> 代码：一个文件 `Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs`。
> 本文只写三件事：**新接一家平台该不该实现它**、**`CanSetValue()` 恒 true 的后果**、**「不接线」会让什么静默失效**。
> 七家现状的差异在 `memory/modules/TransitionSystem/adapters/<平台>.md`（总表 `memory/modules/TransitionSystem/adapters/avalonia.md` §三.1）。

---

## 一、扩展点地图

| 我要… | 扩展点 | 位置 |
|---|---|---|
| 报告「宿主还活着」 | `ApplicationState.SetAlive(bool)` | `IApplicationState.cs:22`；从 `TransitionHostBase<TPriorityCore>.Lifetime` 拿到（`protected`，`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:12`） |
| 只能问、不能观测 | 覆写 `TransitionHostBase.IsAlive` | `Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:14`（`virtual`，默认 `Lifetime.IsAlive`） |
| 读它 | `SamplerSet.CanSetValue()` | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90` |

**官方分工（基类那句注释就是判据，`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:11`）：能观测就报进 `Lifetime`，只能问就覆写 `IsAlive`。** 两者选一，别既覆写又往 `Lifetime` 写（那样 `Lifetime` 变成没人读的死字段）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 宿主持有 `Lifetime` 却从不写 | 初值 `true`（`:13`）⇒ 与「完全不实现 `IApplicationState`」行为一致，且没有任何地方会报 | `IApplicationState.cs:13` |
| 单向使用：只报死、从不报活 | 信号源若是「每次投递的返回值」，一次瞬时拒绝就**永久判死且无日志**；`ApplicationState` 的双向备注正是为此写的 | `:17-21` |
| 把 `IsAlive` 当「可以安全调用平台 API」的守卫 | 它只被 `CanSetValue` 一处读（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90`），不守卫任何其它调用。平台 API 的可用性要自己带 try/catch（`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/UIThreadInspector.cs:16-25` 是同一考虑的邻居） | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90,103,130` |
| `IsAlive` getter 里做昂贵或可能抛的查询 | 它每帧**每属性**读一次（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:130` 在 `foreach` **内**）。MAUI 的实现（`Application.Current?.Windows?.Count > 0`，`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/UIThreadInspector.cs:8`）就是 O(属性数)/帧的属性链 | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:128-130` |
| 以为 `IsAlive = false` 会停掉或收尾动画 | 不会：循环照跑、`Update`/`LateUpdate`/`Completed` 照发，只是值写不出去。要停只有 `Transition.Exit` | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:103,130` |
| 用 `SetAlive` 当取消信号（「宿主要死了先报 false 让动画收尾」） | `CanSetValue` 只读不写，没有任何收尾路径挂在它上面 | `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:90` |

---

## 三、该不该实现它：按平台给什么，四选一

| 平台给你的是… | 做法 | 树内样板 |
|---|---|---|
| **投递操作本身带成功/失败返回值** | `PostCore` 里 `Lifetime.SetAlive(那次返回值)`，**每次投递都报、双向** | WinUI `Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:56` |
| 一个**可订阅的退出事件** | 订阅 → `SetAlive(false)` | WinForms 同形状但把标志存成自己的字段（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:40,51`） |
| 一个**可查询**的平台对象 | 覆写 `IsAlive` 去问它 | MAUI 问窗口数（`:8`） |
| 只有**外部**知道（框架不给钩子） | 覆写 `IsAlive` 读自己的标志 + 对外开一个 `NotifyShutdown()` 形状的方法 | Razor `Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:22,40` |
| **什么都没有**（例：`Dispatcher` 只有 `ShutdownStarted` 事件且没接） | 不接线 —— 编译过、像好的，代价见 §四 | Avalonia |

**新接一家时这是可以推迟的一项**，但「推迟」必须是有意识的：它不会报错，只会少一个能力。

---

## 四、不接线（或接线后仍恒 true）会让什么静默失效

1. **`CanSetValue()` 恒 `true`** ⇒ 关停之后帧照旧入队。真正的后果由**另一条轴**决定（Threading）：
   - `PostCore` **乐观恒 true**（Avalonia 形状）：`PostAsync` 的消费者永远等一个不完成的 TCS。它唯一消费者是 `Awake`，而 `Awake` 是在**持有 scheduler `_gate` 时** `await` 的 ⇒ 那条 `_gate` 永不释放 ⇒ **该 target 之后每一次动画都永久排队**。依据：`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:57-68`、`Src/Core/VeloxDev.Core/TransitionSystem/TransitionScheduler.cs:114`→`:128`→`finally :178`。这是「不接线」最贵的一种组合；该家现状与「要不要修」的评估见 `memory/modules/TransitionSystem/adapters/avalonia.md` §三.1。
   - `PostCore` **诚实拒绝**（WPF/Jalium 形状）：帧被丢，`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:117` 报一次 `Warn("Dropped")` —— 有迹象，不成灾。
2. **没有任何检查会变红**：没有测试、没有断言、没有日志会因为「声明了 `IApplicationState` 却从不写」而失败。树内唯一的存活相关用例是反向的 —— `Src/Core/VeloxDev.Core.Test/TransitionSystem/SamplerSetTests.cs:66-76`（`Apply_WhenAppDead_SkipsWrites`：把宿主的 `Alive` 设成 `false`，断言 `InvokeCount == 0` 且 target 没被写）。
3. **另一条轴的替代品只能顶一部分**：`Post` 返回 false 也会让帧不落地。区别是 `IsAlive` 的判断发生在**入队之前**（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:103`），而 `Post` 的 false 发生在**投递那一刻**；前者省一次投递、后者只省一次写入。所以「有 `HasShutdownStarted` 就不必接线 `Lifetime`」在**不挂起任何等待者**的前提下成立（WPF/Jalium 就是这样），一旦该家 `PostCore` 开始乐观返回 true，这个前提就没了。

---

## 五、联动清单

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | 该家宿主：覆写 `IsAlive` 或在 `PostCore` 里 `Lifetime.SetAlive(...)` | 恒 alive，关停后继续投帧 |
| 2 | `memory/modules/TransitionSystem/adapters/<平台>.md` 记本家的做法与背离 | 下一家接平台的人照抄错的默认 —— 这条比对本现在就存在（`memory/modules/TransitionSystem/adapters/avalonia.md` §三.1） |
| 3 | 若本家自报标志（Razor 形状）：**同时记下谁负责调 `NotifyShutdown()`** | 没人调 = 永不判死，且与「没实现」无从区分（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:22`） |
| 4 | 若本家 `PostCore` 乐观恒 true：把「关停后会不会挂住 `_gate`」评估一遍（§四.1） | 挂死只在该家真机出现，验收套件看不到 |
| 5 | 若本家覆写 `IsAlive`：确认 getter 廉价且不抛（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:130` 每帧每属性读） | 每帧一次属性链 / 一次异常逃进写路径 |

---

## 六、树内不存在的（省掉无谓搜索）

- `IApplicationState` **全树只有一个实现**：`ApplicationState`（`sealed`）。没有哪家适配器自己实现接口。
- `ApplicationState.SetAlive` **全树只有一个调用点**：WinUI 的 `PostCore`。
- `CanSetValue()` 虽然是 `public`，树内（`Src/`、`Examples/`、`Src/Core/VeloxDev.Core.Test/`）只在 `Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs` 内部被读（`:90` 定义、`:103`/`:130` 两处判断），没有外部调用者。
