# Timing 扩展

> 契约：`Src/Core/VeloxDev.Core/Interfaces/Timing/`。实现与注册表：`Src/Core/VeloxDev.Core/Timing/`。
> 架构与不变量见同目录 `architecture.md`。

本模块只有**两类**扩展，且第二类几乎不该做：

| 你想扩展的 | 官方做法 | 走哪条路 |
|---|---|---|
| 换掉「时间从哪来」 | 继承 `TimeSourceCore`，用 protected 构造注入宿主钟 + `SetHostFeeding` | 见 §三·A / §三·B |
| 换掉「时间怎么切成帧」 | 实现 `ITimeSampler` 的一个派生契约并在 `TimerCore` 注册 | 见 §三·C |
| 换掉整条时间轴的状态机 | **不要做**。见 §二·1 | — |

---

## 一、扩展点地图

| 扩展点 | 在哪 | 什么时候用 |
|---|---|---|
| `protected TimeSourceCore(Func<long> nowStamp, long ticksPerSecond)` | `Timing/TimeSourceCore.cs:168` | 宿主能回答「现在几点」——**拉模式** |
| `protected SetHostFeeding(bool)` | `Timing/TimeSourceCore.cs:368` | 宿主的位置在回调里到达、喂一段停一段——**推模式**（必须和上一条同用） |
| `TimerCore.RegisterTimeSource<TContract>(Func<TContract>)` | `Timing/TimerCore.cs:62` | 把某个契约的时钟换成自己的 |
| `TimerCore.RegisterTimeSampler<TSampler>(Func<ITimeSource, TSampler>)` | `Timing/TimerCore.cs:78` | 换掉「时间怎么被切成帧增量」 |
| `TimerCore.UnregisterTimeSource/TimeSampler<T>()` | `Timing/TimerCore.cs:91,95` | 卸载自己的注册（主要给测试） |
| `ITimeSampler` 的三个派生契约 | `Interfaces/Timing/ITimeSampler.cs`、`ICompensatingTimeSampler.cs`、`IUncompensatedTimeSampler.cs` | 新采样模式挂哪个名下 |
| `TimeConversion` | `Timing/TimeSourceCore.cs:14` | 换算 tick ↔ TimeSpan。**注意它住在 `TimeSourceCore.cs` 里，没有自己的文件** |

**不需要扩展的**：`ITimeSource`（读面）、`ITimeSourceControl`（控制面：`Pause`/`Resume`/`SetRate`/`Seek`/`Wake`）—— 这两个契约由 `TimeSourceCore` 实现，宿主**不该**自己实现它们（见 §二·1）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 宿主自有钟 —— 继承，而不是实现 `ITimeSourceControl`

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `class GameLoopSource : TimeSourceCore { public GameLoopSource() : base(() => GameLoop.Now, GameLoop.TicksPerSecond) { } }`，feed 起停时调 `SetHostFeeding(...)` | 锚点算术、epoch 协议、溢出守卫、park 信号全部继承，只把「现在几点」搬出去 |
| ❌ 捷径 | `class GameLoopSource : ITimeSourceControl { ... }` 从头实现 | 你得自己重新发明：`_version` 成对撞、`_parkGate` 装着时绝不为 completed、`Wake` 必须替换、epoch 在括号内自增、`Advance` 必须先除后乘。**任何一条漏了都不报错**，症状分别是热转、掉帧、消费者永久卡死、`CompensatingTimeSampler` 在 seek 后凭空补步 |
| ❌ 捷径 | 继承 `TimeSourceCore` 但重写 `Ticks`/`IsAdvancing` | `Ticks`/`IsAdvancing` 都不是虚的（`TimeSourceCore.cs:194,207`），编不过 —— 这是刻意的：读面只能由状态导出，不能由宿主另说一套 |

依据：`TimeSourceCore.cs:146-167` 的 XML 注释原文 —— 「宿主在别处计时，用这条缝而不是重新实现时间轴」；`TimerCore.cs:20-25`。

### 2. 推模式宿主 —— `SetHostFeeding` 不可省

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | stamp 返回**上一次收到的位置**，feed 开始/停止时调 `SetHostFeeding(true/false)` | `IsAdvancing` 在 feed 静默时为假，消费者 park 住 |
| ❌ 捷径 | 只给构造，不调 `SetHostFeeding` | **这是唯一会坏掉的组合**（`TimeSourceCore.cs:155-159` 点名）：一个冻结的 stamp 背后是一个仍报 `true` 的 `IsAdvancing`。消费者永远不 park，按自己的采样间隔空转，每帧画同一帧 |

**注意不要用 `Pause` 代替 `SetHostFeeding`**：`Pause` 是 rebase（动 epoch、动锚点），而 feed 静默**不该**动这些 —— 位置要原地接上，累加器基准还要有效（`TimeSourceCore.cs:359-363`）。

### 3. 注册 —— 按契约注册，不按实现类型

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `TimerCore.RegisterTimeSource<ITimeSourceControl>(static () => new GameLoopSource());` | 消费者 `CreateTimeSource<ITimeSourceControl>()` 拿到你的 |
| ❌ 捷径 | `TimerCore.RegisterTimeSource<GameLoopSource>(...)` | 注册成功返回 `true`，**什么都没发生**：消费者查的是契约键，字典里多了一个没人查的项（`TimerCore.cs:58-61`、`:109-121`）。测试 `TimerCoreRegistryTests.cs:124-131` 就是固化这条的 |

**也不要在实现类型上注册源码、在契约上查实现** —— 查找按**精确契约**，没有回退（`TimerCore.cs:102-107`）。

### 4. 单例 —— 注册的是「每次新建」的工厂

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | `static () => new Wrapper(OneSharedFeed)` —— 每次返回新实例，包着同一份 feed | 每个消费者有自己的 pause / rate |
| ❌ 捷径 | `static () => singleton` 返回共享实例 | 暂停一个 channel 暂停**所有** channel，与 `MonoBehaviourManager` 的每 channel 一条总线正好相反（`TimerCore.cs:27-32`） |

### 5. 负数 rate —— 让它抛，不要 clamp

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 你（调用方）保证非负 | — |
| ❌ 捷径 | 在消费者侧 `rate = Math.Max(0, rate)` | 调用方的错误被吞掉：`SetRate(-1)` 变成暂停，动画静默不动，而调用方以为设了倒放。要回退用 `Seek`（`TimeSourceCore.cs:279-284`） |

同理**不要**因为「反正只拒绝负数」就把小 rate 当安全：`1e-5` 合法但会被 `Scale` 截断成 0（`TimeSourceCore.cs:292`）。

### 6. 换采样模式 —— 实现契约并在 `TimerCore` 注册

| | 做法 | 结果 |
|---|---|---|
| ✅ 官方 | 实现 `ICustomSampler : ITimeSampler`，`RegisterTimeSampler<ICustomSampler>(...)` | 按契约解析得到 |
| ❌ 捷径 | 直接在消费者里 `new MySampler(source)` | 绕过了替换缝：平台没法替你换（`TimerCore.cs:9-16` 的设计意图） |
| ❌ 捷径 | 继承 `TimeSamplerCore` 后从基类构造里调自己的 `Reset()` | 基类构造**先于**派生字段初始化器，会读到未初始化的字段。基类刻意不提供 Reset（`TimeSamplerCore.cs:6-10`），每个采样器在自己的构造里 prime 自己 |

### 7. 采样器的状态 —— 别往基类塞

`TimeSamplerCore` 只有 `Source` 和两个换算助手（`TimeSamplerCore.cs:18-26`）。新的采样模式**全部状态放自己身上**：epoch 基准、累计量、上次读数都各是各的（对比 `CompensatingTimeSampler.cs:32-43` 与 `UncompensatedTimeSampler.cs:16-17`）。不要为了「共用」把 epoch 判断搬进基类 —— 不记账的采样器根本不需要它。

---

## 三、步骤清单

### A. 接一个「拉模式」宿主（宿主能答「现在几点」）

1. `class <Host>TimeSource : TimeSourceCore`，构造里 `: base(<host>.Now, <host>.TicksPerSecond)`。
2. **确认 `TicksPerSecond` 就是 stamp 的单位**，不是 `Stopwatch.Frequency`。写错了消费者每次换算都差一个比值且无人报错（`TimeSourceCore.cs:161-165`）。
3. 确认宿主时钟**单调**。`Advance` 只在单次调用内防倒走（`TimeSourceCore.cs:497`），跨调用倒走的钟会把位置往回拉。
4. 注册：`TimerCore.RegisterTimeSource<ITimeSourceControl>(static () => new <Host>TimeSource());`。
5. 若该钟只在某一帧跑（渲染循环），**停下来**——那正是 `TimerCore.cs:20-25` 说不该做的事。

### B. 接一个「推模式」宿主（位置在回调里到达）

1. stamp 返回**上次喂进来的位置**（不是「现在几点」）：`private long _last; ... : base(() => _last, unit)`。
2. 每次收到位置回调时，`SetHostFeeding(true)` + 更新 `_last`。**先喂后报**，还是**先报后喂**要选一个并自洽：报 `true` 之后、`_last` 更新之前会有一个读者看到「在走但位置没变」的窗口。
3. feed 停止时 `SetHostFeeding(false)`（可在任意线程调，含宿主自己的线程，`TimeSourceCore.cs:365`）。
4. 若宿主钟在静默期间还在走、恢复时想直接跳到新位置 —— 那用 `Seek`，不要指望 `SetHostFeeding` 帮你（`TimeSourceCore.cs:359-363`）。
5. 注册同 A·4。

### C. 新增一种采样模式

1. 在 `Interfaces/Timing/` 加契约 `IXxxSampler : ITimeSampler`（继承而非改 `ITimeSampler` —— 契约是消费者查表用的键）。
2. 在 `Timing/` 实现它，继承 `TimeSamplerCore`，**自己的构造里 prime 自己**，`Reset()` 只重置自己的字段。
3. 在 `TimerCore.cs:46-52` 的静态构造里 `RegisterTimeSampler<IXxxSampler>(...)`。
4. 决定它在 rebase 后的行为：跟随 `Source.Epoch` 丢弃基准，还是照常结算。**丢弃是默认答案**（`CompensatingTimeSampler.cs:130-141`）—— 不丢弃会在向后 seek 时还出负账、向前 seek 时补出一串从未发生过的步。
5. 若消费者会用 `TimerCore.CreateTimeSampler<IXxxSampler>(source)` 拿它，确认它不在 `_writeGate` 里做重活：`Advance`/`Sample` 是热路径。

---

## 四、联动清单

改 Timing 时**必须同步检查**的位置：

| 改动 | 联动 |
|---|---|
| 加/改 `Times/TimeSourceCore` 的控制调用语义 | `Src/Core/VeloxDev.Core/TimeLine/MonoBehaviourManager.cs`（`SetTimeScale` = `_bus.SetRate` 逐字转发，`:239`；`Pause`/`Resume` 受 `_isRunning` 门控，`:385-403`）、`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:78` |
| 加/改采样器契约 | `Interfaces/Timing/` + `Timing/TimerCore.cs:46-52` + `TimerCoreRegistryTests.cs` |
| 改 `Scale` 或 `DefaultTicksPerSecond` | 消费者侧的 `TimeConversion` 调用会静默改变量纲（`Src/Core/VeloxDev.Core/Timing/TimeSourceCore.cs:9-12` 的 remarks 写明「quietly used Stopwatch.Frequency would be wrong by orders of magnitude with no symptom」）；消费点如 `TimeLine/MonoBehaviourManager.cs:665-667,746,846,935-937`、`TransitionSystem/Transition.cs:171,176,529` |
| 改 `Advance` 的溢出守卫 | `TimeSourceContractTests.cs` / `HostTimeSourceTests.cs` |
| 让某个契约注册变得「可选」 | `CreateTimeSource` 无回退（`TimerCore.cs:102-107`），去掉默认注册会让**所有**消费者在运行时抛 |

**写测试时必须用「标记契约」模式，不要拿 `ITimeSourceControl` 做实验。** `TimerCoreRegistryTests.cs:20-26` 定义私有的 `IMarkerSource`/`IMarkerSampler`，注册、断言、`finally` 里 `Unregister` 全部走标记契约。直接 `RegisterTimeSource<ITimeSourceControl>(...)` 会把 Core 默认从**整个进程**里换掉，而这类测试是并行跑的（`Src/Core/VeloxDev.Core.Test` 的方法级并行）—— 受害的是完全无关的动画测试，症状是「一个 park 在冻结时钟上的动画不失败，它挂住」。

---

## 五、死扩展点与已失效的钩子

| 扩展点 | 状态 | 依据 |
|---|---|---|
| `protected TimeSourceCore(Func<long>, long)` | **仓库内零实现**。`Src/` 与 `Examples/` 下没有任何子类 | 仅测试 `Src/Core/VeloxDev.Core.Test/Timing/FakeTimeSource.cs`（及同文件的 `PullHostSource`/`PushHostSource`）用到 |
| `SetHostFeeding` | 同上，零非测试调用者 | 同上 |
| `RegisterTimeSource` / `RegisterTimeSampler` | **`Src/` 内非测试调用者为零**，只有 `TimerCore` 自己的静态构造（`TimerCore.cs:48-51`） | — |
| `UnregisterTimeSource` / `UnregisterTimeSampler` | 只有测试调用 | `TimerCoreRegistryTests.cs:82,99,114,131,146` |
| `TimeSamplerCore.ToTicks` | 只有 `CompensatingTimeSampler.Step` 用（`CompensatingTimeSampler.cs:67`） | — |
| 「定时器」能力（延迟触发、超时） | **不存在**，也不该在这里加 | `TimeSample` 刻意不带绝对位置；`TimerCore` 类注释把时间与循环分开（`TimerCore.cs:20-25`） |

**这意味着**：本模块的扩展缝是「已经建好、但仓库内没人走」的形态。写新代码时别把「没人用」当成「可以删」——`TimerCore.cs:9-16` 与 `TimeSourceCore.cs:146-167` 都把这条缝写成了对外承诺，七家 GUI 适配器现在是间接消费者（经 `Transition<T>`），加第八家或加一个非 GUI 宿主时它就会有人走。
