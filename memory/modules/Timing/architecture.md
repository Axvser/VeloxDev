# Timing 架构

> 代码：`Src/Core/VeloxDev.Core/Timing/`（5 个 .cs）、契约在 `Src/Core/VeloxDev.Core/Interfaces/Timing/`（6 个文件）。
> 这是**全仓库唯一的时间权威**。任何「现在几点、走了多远、还走不走」的问题，答案只在这里。
> 帧节奏（每次唤醒隔多久、下一个续体怎么排队）归 `TransitionSystem`，见 `Src/Core/VeloxDev.Core/TransitionSystem/FramePacerCore.cs`；本模块只回答「时间轴在哪」。

本文只写「读完这 5 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一条**绝对、从不重启**的虚拟时间轴，外加把它包成帧增量的两个采样器。

三件事各自独立地定义它：

| 维度 | 由谁决定 | 依据 |
|---|---|---|
| 现在在哪 | `_anchorVirtual` + (真实时钟 − `_anchorReal`) × `_speed` | `TimeSourceCore.cs:494-503`（`Advance`） |
| 走不走 | `_hostFeeding && !_paused && _rate > 0` | `TimeSourceCore.cs:212-213`（`Advances`） |
| 停在哪 | `_parkGate` 非空 ⟺ `!IsAdvancing` | `TimeSourceCore.cs:410-423`（`RefreshParkGate`） |

**不解决什么（这几条决定了你找不到代码时该往哪看）：**

| 你以为在这里 | 其实在哪 |
|---|---|
| 「下一帧什么时候来」/ 帧栅格 / FPS 上限 | `TransitionSystem/FramePacerCore.cs` + 各适配器的 `TransitionInterpreter.CreateFramePacer`。`TimerCore.cs:21-25` 的类注释明说「把 loop 钉在 UI 线程上是另一个问题，归拥有 loop 的子系统」 |
| 暂停时要不要记欠账、恢复后补不补帧 | `CompensatingTimeSampler.cs:130-141` 的 **epoch 丢弃**：暂停期间一步不欠，是构造性的 |
| 睡眠/自旋/续体调度 | 消费者自己的循环（`TransitionInterpreter.cs:298-301` 的 `EmitFrame`-then-`WaitWhileStalledAsync`） |
| 定时器（「N 毫秒后叫我」） | 没有这个能力。本模块**只报位置，不报事件**。`TimeSample` 刻意不带绝对位置（见 `Interfaces/Timing/` 的 `TimeSample`） |
| 反向播放 | 不成立。时间只向前，`SetRate` 拒绝负数（`TimeSourceCore.cs:287-290`） |
| 真实墙钟的单调性保证 | 靠 `Stopwatch.GetTimestamp()`（`TimeSourceCore.cs:130`）。注入源自己负责单调 |
| GUI 线程安全 | 不保证、也不需要：读无锁、写串行，控制调用可从任意线程发（`TimeSourceCore.cs:365` 明说） |

---

## 二、一份状态，两套协议：写者串行、读者无锁

这是本模块第一个「读代码才知道」的点。`TimeSourceCore` 的状态是 5 个 `long`（`_version`/`_anchorReal`/`_anchorVirtual`/`_speed`/`_rate`，`TimeSourceCore.cs:83-94`）+ 3 个 `bool`/`TaskCompletionSource`（`_paused`/`_hostFeeding`/`_parkGate`）。两套协议分开管：

- **写**：`lock (_writeGate)`（`TimeSourceCore.cs:78`）。所有控制调用都在里面改状态。
- **读**：`Now` 走 sequence counter（seqlock）：读前读后各看一次 `_version`，奇数或变了就 `SpinOnce` 重来（`TimeSourceCore.cs:426-453`）。读者**从不碰锁**。

为什么必须是两套、不能合成一套：

1. 读发生在**每一帧、每个消费者**上，锁会成为最热的那条路径；写只发生在控制调用。
2. `_version` 每次 publish **被撞两次**（奇数 → 偶数，`TimeSourceCore.cs:524,533`），所以它**不能**当身份标识给消费者比较。这就是 `_epoch` 存在的唯一理由（`TimeSourceCore.cs:85-89` 的注释写死了这句话）：`_epoch` 每次 rebase 只 +1，在括号**内**自增（`TimeSourceCore.cs:532`），所以消费者绝不会看到「新 epoch + 旧字段」。

| 控制调用 | `speed` | `rate` | `virtualTarget` | 动 epoch | 装/清 gate | 依据 |
|---|---|---|---|---|---|---|
| `Pause()` | `0` | 不变 | — | ✅ | 只可能装 | `:238` |
| `Resume()` | `_rate` | 不变 | — | ✅ | 清（若 rate>0） | `:267` |
| `SetRate(r)` | `_paused ? null : r` | `r` | — | ✅ | 两向都可能 | `:298` |
| `Seek(pos)` | 不变 | 不变 | `target` | ✅ | 实际恒 null | `:314` |
| `SetHostFeeding(b)` | 不变 | 不变 | — | ❌ | 两向都可能 | `:368-382` |

**三条只有对照着读才看得出来的规矩：**

- **`SetRate` 在暂停中只记住、不启动。** `speed: _paused ? null : scaled`（`TimeSourceCore.cs:298`）：暂停中设一个非零 rate，`_rate` 变了但 `_speed` 留 0，于是 `IsPaused` 为真、`IsAdvancing` 仍为假，等 `Resume` 才真正走。代价是「暂停中设 rate 不会恢复播放」，收益是 `SetTimeScale(0)` 能冻住帧循环而不是留下一根转轴（`TimeSourceCore.cs:252-258` 的 remarks 把这条当成设计意图）。
- **`SetHostFeeding` 不是 rebase。** 它**不动 epoch、不动锚点**（`TimeSourceCore.cs:359-363` 明说）：位置原样接上，累加器的基准仍然有效。宿主自己的钟在 feed 静默期间还在走，那是「跳变」不是「停摆」，该用 `Seek` 表达。
- **`Wake()` 是替换 gate，不是就地 complete。** `_parkGate = NewGate()` 之后才 `TrySetResult` 旧的那个（`TimeSourceCore.cs:331-342`）。就地 complete 会让停着的消费者下一次 `await` 立刻返回，一帧帧空转。

**「暂停期间不记账」是怎么实现的：不是判 `_paused`，而是 `speed = 0`。** 读者 `Now` 从来不读 `_paused`/`_rate`，只读 `_anchorVirtual`/`_anchorReal`/`_speed`（`TimeSourceCore.cs:440-442`）。所以暂停语义完全落在 `Pause` 把那三个字段写成什么上。改了 `Pause` 的写入就等于改了暂停语义，而 `RefreshParkGate` 那边看的是 `Advances`，两者由 `Advances` 一个表达式绑定，不可能不一致（`TimeSourceCore.cs:209-213` 的注释）。

**续体在锁外完成。** `Pause`/`Resume`/`SetRate`/`Seek`/`SetHostFeeding` 五个调用都是「锁内改状态 + 拿回待完成的 gate，锁外 `TrySetResult`」（`:246`、`:273`、`:304`、`:318`、`:381`）。理由是 `resume` 的注释原文：完成 gate 会**同步**跑续体，续体若重入控制调用就死在不可重入的锁上（`TimeSourceCore.cs:271-272`）。

---

## 三、状态机：`IsAdvancing` 是不变量，不是公式

`IsAdvancing` 有三个独立来源能让它为假，且**互不蕴含**：

| 变假的路径 | 字段 | 谁改 |
|---|---|---|
| 被暂停 | `_paused` | `Pause`/`Resume` |
| rate 为 0 | `_rate`（经 `_speed`） | `SetRate(0)` |
| 宿主停止喂 | `_hostFeeding` | `SetHostFeeding(false)` |

所以它写成 `Advances` 一个表达式而不是分散判断（`TimeSourceCore.cs:212-213`），且 `IsAdvancing` 与 `_parkGate` 都从它派生。**消费者必须 park 在 `IsAdvancing` 上，不能 park 在 `IsPaused` 上** —— 契约里把这条写成了「不变量」而非「公式」，理由是：一个 host 停止 feed 会让位置冻结但 `IsPaused` 仍为假，若 `IsAdvancing` 报真，消费者永远不会 park，而这个失败**在宿主侧完全看不见**（`TimeSourceCore.cs:104-110`、`Interfaces/Timing/ITimeSource.cs` 的同名说明）。

**`_parkGate` 的生命周期规矩（一个「必须成对」的不变量）：**

- 装：`RefreshParkGate` 里 `_parkGate ??= NewGate()`（`TimeSourceCore.cs:421`）。**绝不允许「装着一个已完成的 gate」**，否则 `while (!IsAdvancing) await WaitWhileStalledAsync()` 立刻返回、立刻重进，把一个核心热转（`TimeSourceCore.cs:419-420` 的原注释）。
- 清：`Advances` 为真时置 null 并把它交出去让调用方 complete（`TimeSourceCore.cs:412-417`）。
- 重装（`Wake`）：替换（见 §二）。
- **`WaitOnGateAsync` 在取消时是 `ThrowIfCancellationRequested`，不是 return**（`TimeSourceCore.cs:455-465`）。理由原文：循环在正常返回上转的消费者一旦被取消就永久空转，因为没人会替它 resume 时间轴。
- `WaitWhileStalledAsync` 只在**真正停摆时**才建取消注册（`TimeSourceCore.cs:392-397`）：活着的消费者一分钱不花。

**epoch 的读法有个坑：epoch 与 ticks 是两次独立读。** `CompensatingTimeSampler.Advance` 先读 `Source.Epoch` 再读 `Source.Ticks`（`CompensatingTimeSampler.cs:127-128`），中间没有原子性保证。所以「这两个值属于同一个 epoch」不能用它们配对来推断——采样器只在 epoch **变了**时才丢弃基准，读到的是「略早的 epoch + 略晚的 ticks」，表现是极偶然地多结算一小段。想看设计意图就照 `Advance` 的用法写，别自己发明配对判据。

---

## 四、两个采样器：一个记账，一个不记

| | `UncompensatedTimeSampler` | `CompensatingTimeSampler` |
|---|---|---|
| 输出 | 距上次采样的**实测间隔** | **固定步数** + 每步时长 |
| 状态 | 两个 `long`（`_lastTicks`/`_originTicks`） | 六个 `long`（`_acc`/`_earned`/`_delivered`/`_dropped`/`_lastTicks`/`_epoch`） |
| 欠账 | 没有「欠」的概念 | 有，且**可被宽恕**（`_dropped`） |
| 暂停后首帧 | 天然干净：源不前进，下次采样只覆盖 resume 之后的帧（`UncompensatedTimeSampler.cs:8-12`） | epoch 变了 → 基准归零（`CompensatingTimeSampler.cs:133-141`） |
| 适用 | **帧循环**：一帧一个增量，不关心帧率 | **固定步物理/积分**：步数必须与时间成严格函数 |
| 缺省注册为 | `IUncompensatedTimeSampler` | `ICompensatingTimeSampler`，步长 16ms（`TimerCore.cs:41,49-51`） |

**`CompensatingTimeSampler` 的三个量必须分开（`CompensatingTimeSampler.cs:8-15` 把这句话写死了）：**

- `_acc` —— 还不够一整步的时间。**永远携带、永不四舍五入**。这是「不漂移」的全部内容。
- `_earned` —— 时间已经付过账的步数。有上限时它会跑在交付前面。
- `_delivered` —— 真正报出去的步数。**虚拟钟 = `_delivered × Step`**，所以它永远是整数计数、永远不是实测时间（`CompensatingTimeSampler.cs:168-171`）。

配套的数字与边界：

| 数字 | 值 | 含义 |
|---|---|---|
| `DefaultMaxStepsPerCall` | `8`（`CompensatingTimeSampler.cs:24`） | 一次调用最多补几步 |
| `DefaultMaxPendingSteps` | `64`（`:30`） | 允许积的欠账，≈ 16ms 步长下一秒 |
| 超上限怎么办 | **宽恕超出部分并计入 `_dropped`**（`:155-163`）。理由原文：不宽恕的话被挂起过的机器欠几百万步，限速偿还期间消费者比直接宽恕还落后得久 |
| `Step` 赋值 | 抛 `ArgumentOutOfRangeException` 当 `<= 0`（`:61-64`），并**清空 `_acc`**（`:69`）：余数是「旧步长的分数」，对新步长不成立。已交付步数不动 |
| `_stepTicks <= 0` 兜底 | 置 1（`:68`）：总线单位比 TimeSpan 的 100ns 还粗时，一步至少占一个 tick |
| `Total` 的算法 | `_delivered * _step.Ticks`（`:170`），**不吃余数** |

`TimeSamplerCore`（基类）**刻意不持有采样状态、也不声明 `Reset`**：基类构造先于派生字段初始化器，从这里调 reset 会读到派生类型还没初始化的字段。每个采样器在自己的构造里 prime 自己（`TimeSamplerCore.cs:6-10`）。

---

## 五、`TimerCore`：按契约注册，不按实现注册

`TimerCore` 是静态注册表 + 工厂，两个 `ConcurrentDictionary<Type, Func<...>>`（`TimerCore.cs:43-44`），静态构造装上 Core 默认（`:46-52`），所以**任何查找永远解析得到**，除非平台在同一契约上覆盖过。

四条读代码才知道的规矩：

1. **键是契约类型，不是实现类型。** 注册 `RegisterTimeSource<ITimeSourceControl>(...)`，不是写具体源的类型（`TimerCore.cs:10-16`、`:58-61`）。注册到实现类型上会存到一个没人查的地方。
2. **查找按精确契约，没有回退。** 不回退到「更宽或更窄」的注册，理由原文：回退得在多个可赋值键里挑一个，而字典顺序未定义 → 同一查找在不同运行里返回不同实现（`TimerCore.cs:102-107`）。找不到就抛 `InvalidOperationException`（`:118-121`）。
3. **每次查找都新建实例。** 有唯一时钟的宿主也保持这个形状：**每次调用返回一个「包着同一份 feed 的新 wrapper」，不是共享单例**（`TimerCore.cs:27-32`）。单例会让暂停一个通道暂停所有通道。
4. **`AddOrUpdate` 后写者胜**，与 `InterpolatorCore.RegisterInterpolator` 同一规矩（`TimerCore.cs:14-16`）。

**「定时器不是时间权威」这条边界的落点。** `TimerCore` 的类注释（`TimerCore.cs:20-25`）明说这条缝是给「**拥有时间的宿主**」——player loop、媒体位置、音频回调——用的，**刻意不是给框架的渲染循环用的**：只在帧上走的钟会让帧率变成时间权威，而采样路径建立在反面。`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:144` 那句「`Task.Delay` 从来不是 timing source」是同一件事的另一面。

**「谁在用 Timing」——仓库内的消费者清单（全在 Core 内）：**

| 调用 | 位置 |
|---|---|
| `TimerCore.CreateTimeSource<ITimeSourceControl>()` | `TransitionSystem/Transition.cs:386`（默认时间轴）、`TransitionSystem/SamplerSet.cs:78`（没人控制得住的私有时间轴）、`DynamicTheme/ThemeManager.cs:236`（整场共享一条轴）、`TimeLine/MonoBehaviourManager.cs:117`（每个 channel 一条总线） |
| `TimerCore.CreateTimeSampler<...>` | `TimeLine/MonoBehaviourManager.cs:164-167`（两个采样器，显式 16ms 步长） |

也就是说 **Timing 只服务 `TransitionSystem` 与 `TimeLine` 两个模块**。`Src/Adapters/` 下七家 GUI 适配器**一家都不直接引用 Timing**（它们经 `Transition<T>`/`TransitionHostBase` 间接用）。

**`Src/` 里零个非测试注册者。** `RegisterTimeSource`/`RegisterTimeSampler` 的调用者只有 `TimerCore` 自己的静态构造（`TimerCore.cs:48-51`）和测试（`Src/Core/VeloxDev.Core.Test/Timing/TimerCoreRegistryTests.cs`）。宿主自有钟那条缝（`protected TimeSourceCore(Func<long>, long)` + `SetHostFeeding`）在仓库内**没有任何实现**——`ITimeSourceControl` 的仓库外实现仅见于测试的 `FakeTimeSource`/`PullHostSource`/`PushHostSource`。所以那条缝是「给未来的宿主预留的、已建好但没人走的路」。

---

## 六、不变量

**A. 顺序/成对类**

1. **`_version` 必须成对撞。** 每次 publish 撞两次（奇数 → 偶数，`TimeSourceCore.cs:524,533`），且 `_epoch` 在两次之间（`:532`）。漏撞一次，读者会永久看到奇数 version 而热转。
2. **控制调用改状态必须在 `_writeGate` 内。** 这是 `_version` 协议成立的前提：锁保证没有另一个写者卡在两次撞之间（`TimeSourceCore.cs:509-511` 的注释）。
3. **gate 只能由「清掉它的那次调用」complete。** 统一形式：`RefreshParkGate` 返回被清的那个，调用方在**锁外**完成（`TimeSourceCore.cs:400-423`）。这条规矩四次控制调用完全一致，`Pause`/`Seek` 里 `woken` 恒为 null 也照写（`:240-243`、`:315`）。
4. **`Wake` 必须替换而非就地完成**（`TimeSourceCore.cs:326-330`）。
5. **`Advance` 必须先取整再相乘**（`TimeSourceCore.cs:499-502`）。

**B. 值语义/边界类**

6. **`Scale = 10_000L` 决定 rate 的分辨率是 1e-4。** `SetRate` 里 `scaled = (long)(rate * Scale)`（`TimeSourceCore.cs:292`），小于 1e-4 的非零 rate 会**截断为 0** → 时钟静默冻结，**不抛异常**。`SetRate` 只拒绝负数（`:287-290`），不拒绝「小到变成 0」。
7. **`-0.0` 不抛。** `rate < 0d` 对 `-0.0` 为假，`(long)(-0.0 * Scale) == 0`，落到「冻住」这一支。是「负数的边界含 -0.0」还是「不含」，代码只给你这一个答案。
8. **极大 rate / `double.PositiveInfinity` 落到 `long` 饱和**，不是异常（`(long)` 转换语义）。会被 `Advances` 判为在走，位置按饱和后的大 speed 前进。
9. **`Advance` 对 `elapsedTicks <= 0` 返回原位置**（`TimeSourceCore.cs:497`）：单调钟不该倒走，真出现负值时保持不动，好过把往回跳的位置交给采样循环。
10. **`DefaultTicksPerSecond = Stopwatch.Frequency`，不可移植**（`TimeSourceCore.cs:16-17`）。宿主在别处计时必须自报单位，否则消费者做的每次换算都静默差一个比值（`:161-165`）。`TimeConversion` 的每个成员都**收单位做参数**而不是假设一个（`:8-13`）。

**C. 反直觉类**

11. **`TimeConversion` 住在 `TimeSourceCore.cs` 里**（`TimeSourceCore.cs:14`），没有自己的文件。找「tick 换算」时别去 grep 文件名。
12. **`_nowStamp` 是委托字段，不是虚方法**（`TimeSourceCore.cs:132-140`）。刻意的：stamp 在构造函数里读一次，虚方法会在子类自己的构造赋值之前跑 override —— 经典的构造顺序陷阱，这里以时钟为症状。
13. **`TimeSourceCore` 的默认构造把 `MachineStamp` 与 `DefaultTicksPerSecond` 一起传下去**（`TimeSourceCore.cs:142-143`），所以两个默认值是**绑定的**：换了自定义 stamp 却忘了 ticksPerSecond，就得到上面第 10 条的静默比例错误。
14. **`Advance` 的溢出边界被量化过**（`TimeSourceCore.cs:476-478`）：先乘后除在 `long.MaxValue / speed` 处溢出 —— speed 10000 时 Windows 约 **2.9 年**、Linux 约 **10.7 天**（Linux 上一个 Stopwatch tick 是一纳秒）。C# 算术 unchecked，失败不是异常而是静默回绕成负位置，消费者从此永久卡住。先除后乘的性能代价被量过并写进注释：乘在前 **0.23 ns**、除在前 **0.61 ns**，安全成本 **0.38 ns**；一次动画每帧跑一次，100 个动画 60FPS 下是**每秒 2.3 µs**（`:486-492`）。
15. **`protected TimeSourceCore(Func<long>, long)` 的守卫刻意不用 `ThrowIfNull`/`ThrowIfLessThan`**：Core 多目标到 netstandard2.0 与 netframework4.6.1，那两个助手在这些目标上不存在（`TimeSourceCore.cs:170-171`）。
16. **`Ticks` 是绝对虚拟 tick，`Position` 是相对 `_origin` 的 `TimeSpan`**（`TimeSourceCore.cs:194-197`）。`PassAnchor` 这类锚点用 `Ticks`（绝对），人看的用 `Position`（相对）。`_origin` 在构造时一次性记下（`:183-185`），之后没有任何写者。

---

## 七、入口：我要改 X，先打开哪个文件

| 我想改 | 打开 |
|---|---|
| 暂停/恢复/调速/Seek 的语义 | `Timing/TimeSourceCore.cs` 的四个控制调用 + `Rebase`（`:505-534`） |
| 「停不停」的判据 | `TimeSourceCore.cs:212-213` 的 `Advances`（**唯一**的合成点） |
| 停摆时消费者怎么被唤醒 | `TimeSourceCore.cs:410-423`（`RefreshParkGate`）、`:326-342`（`Wake`） |
| 位置的数学（锚点、speed、溢出） | `TimeSourceCore.cs:494-503`（`Advance`） |
| 无锁读协议 | `TimeSourceCore.cs:426-453`（`Now`） |
| 固定步长积分 / 补帧上限 / 宽恕 | `Timing/CompensatingTimeSampler.cs` |
| 帧增量（不记账的那种） | `Timing/UncompensatedTimeSampler.cs` |
| 新增一种采样模式 | 契约 `Interfaces/Timing/ITimeSampler.cs` + 在 `TimerCore.cs:46-52` 注册 |
| 换掉时钟（宿主自有钟） | `TimeSourceCore.cs:168` 的 protected 构造 + `:368` 的 `SetHostFeeding`；注册走 `TimerCore.cs:62` |
| tick ↔ TimeSpan 换算 | `TimeSourceCore.cs:14-36`（`TimeConversion`） |
| 「定时器」能力 | 不存在。往回看 `FramePacerCore`（TransitionSystem）或宿主定时器 |

---

## 八、陷阱（带依据）

1. **小 rate 会静默冻结时钟。** `SetRate(1e-5)` → `scaled = 0` → `_rate = 0` → `IsAdvancing` 假 → 消费者 park，没有任何异常、没有日志。想「几乎不动」要停在 1e-4 以上（`TimeSourceCore.cs:292`、`:212-213`）。
2. **`Seek` 会 `Wake()`，`Pause` 不会。** `Seek` 末尾无条件叫 `Wake`（`TimeSourceCore.cs:323`），是为了「暂停中的 seek」可见：停着的消费者被顶醒、画出刚被移到的位置、再停回去。`Pause`/`Resume` 只靠 `RefreshParkGate` 的返回值（`:246`、`:273`）。
3. **在暂停中设 rate 不会开始播放。** 只改 `_rate` 不改 `_speed`（`TimeSourceCore.cs:298`）。要开始得 `Resume()`。
4. **`Resume` 在 rate 为 0 时不会让时间前进。** `IsPaused` 变假但 `IsAdvancing` 仍假（`TimeSourceCore.cs:252-258`）。「resume 了为什么还是不动」答案是 rate。
5. **`SetHostFeeding(true)` 之后位置会「跳」。** 它不是 rebase（`TimeSourceCore.cs:359-363`）：宿主自己钟在静默期间走掉的那段，会在 feed 恢复时一次性补上。要「冻结后原地接上」应该用 `Seek`。
6. **`CompensatingTimeSampler` 的 `PendingSteps` 上限是「宽恕」不是「丢弃」。** 超出的部分进了 `DroppedSteps`（`CompensatingTimeSampler.cs:155-163`），`Total` 却按 `_delivered` 算（`:170`）——所以 `DroppedSteps > 0` 的机器上，虚拟钟**永远落后于真实时间**，这是有意的，不是 bug。
7. **改 `Step` 会丢余数。** `_acc = 0`（`CompensatingTimeSampler.cs:69`）。运行中改步长，当前那不足一步的零头直接没了。
8. **`MaxPendingSteps` 越小越容易「永远追不上」。** 宽恕之后 `pending` 被压回上限（`:162`），消费者不需要还清历史；但代价是虚拟时间与实际时间永久错开。
9. **`UncompensatedTimeSampler.Sample()` 在同一次 tick 内重复调用（或暂停中、rate 为 0 时）返回 `default`**（`UncompensatedTimeSampler.cs:34`，注释把三种情形都列了），调用方应据此跳过这一帧。注意它返回的是 `TotalTime` **也一并归零**的 `default`，所以不能拿 `default` 的 `Total` 当「总时长」——它给的是「本次无进展」。正常返回时 `Total = now − _originTicks`（`:37`），且 `Step` 恒为 `0`（`TimeSample.cs` 的 `Step` 文档：「零表示不补偿的采样器」）。
10. **`TimerCore` 的键是私有的、从不交出**（`TimerCore.cs:33-36`）：子类或调用方整体替换字典会丢掉默认项，让所有查找变成不可解析。
11. **`CreateTimeSource<TContract>` 的泛型参数写错**（写实现类型而不是契约）会在**运行时**抛 `InvalidOperationException`，编译期完全正常（`TimerCore.cs:109-121`）。异常消息里刻意复述了这条。
