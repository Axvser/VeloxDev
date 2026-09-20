# MAUI — TransitionSystem 适配器

> 代码：`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/`（采样器在 `Samplers/`）。
> 本文只写这一家的差异。契约本身、要实现的八个类、以及所有平台共通的坑，在 `../extension.md`；用法层文档在
> `skills/veloxdev-create-animation/references/adapter.md`（MAUI 段落含 `IsRepeating` 与「首个 stop」两条），本文不重抄。
> 该目录下 `ThemeValueConverters.cs` / `TransitionEffects.cs` **不属于八件套**：前者是 `VeloxDev.DynamicTheme` 的
> 值转换器，后者是三条预置 effect（`TransitionEffects.cs:4-18`），照八件套清单核对时跳过它们。

---

## 一、这家必须实现哪些契约成员，为什么是这些

八件套里 MAUI 真正写逻辑的只有四处，其余四个是空壳（`TransitionScheduler.cs:2`、`State.cs:3`、`TransitionEffect.cs:3` 各一行）：

| 成员 | MAUI 的选择 | 为什么 |
|---|---|---|
| `UIThreadInspector`（`UIThreadInspector.cs:6`） | `TransitionHostBase<NonPriority>`；覆写 `ThreadFor` / `IsCurrentThread` / `PostCore` / `IsAlive` | MAUI 没有可传的 dispatcher 优先级，所以 `TPriorityCore` 取 `NonPriority`，也就**没有 `InternalPriority` 这条路可走**（`../extension.md` §三·C 的选型表把 MAUI 与 WinForms/Razor 归同类）。四处的型参必须一致：`UIThreadInspector.cs:6`、`Transition.cs:18`、`TransitionScheduler.cs:2`、`Interpolator.cs:26`。 |
| `TransitionInterpreter`（`TransitionInterpreter.cs:6`） | 必须覆写 `CreateFramePacer` | MAUI 上「活着的帧时钟」只有 `IDispatcherTimer`，由本家提供；见 §二·2 |
| `Interpolator`（`Interpolator.cs:24-27`） | 必须覆写 `CreateScheduler` | 七家同形，走 `FindOrCreate`；漏了主题切换静默瞬切（`../extension.md` §二·3） |
| `Interpolator` 静态构造（`Interpolator.cs:8-22`） | **12 条**注册 | 见下 |

**为什么是这 12 条**：MAUI 是七家里唯一自带一套**单精度几何类型**的平台 —— `Microsoft.Maui.Graphics.PointF/SizeF/RectF`
与 MAUI Controls 的 `Point/Size/Rect/Thickness` 同时存在。所以别家各一份的采样器在这里要**成对**注册：

```
Point  : PointF   (Interpolator.cs:12,13)
Size   : SizeF    (Interpolator.cs:17,18)
Rect   : RectF    (Interpolator.cs:19,20)
```

`*FSampler` 里的 **F 就是单精度 `float`**，对应 MAUI 的 `PointF/SizeF/RectF` —— 名字里的 F 与 .NET 的
`PointF`/`SizeF`/`RectangleF` 同源，不是「Frame」也不是平台缩写。七家里只有 MAUI 有这套重复类型，所以
`PointFSampler` / `SizeFSampler` / `RectFSampler` / `ShadowSampler` 这四个类**只有 MAUI 有**
（对比：`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/Samplers/`、`…/Avalonia/…`、`…/WPF/…` 等六家的目录清单里都没有）。
校验身份时按**注册键类型**核对，不要按类名 —— 注册表的 key 就是 `Type` 本身（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:39` 的 `ConcurrentDictionary<Type, ISampler>`，查找是 `:50-60` 的精确命中→基类→接口），同名而不同程序集的两个类型是两个键、各注册各的；只有把**同一个 `Type`** 注册两次才会被 `AddOrUpdate` 顶掉（历史上那条 `RectFSampler` 的教训见 §四·1）。

---

## 二、平台硬限制（别家不能照抄这里的哪些前提）

### 1. 存活只能从「窗口还在不在」推，而基类默认永不报死

`UIThreadInspector.cs:8`：`IsAlive => Application.Current?.Windows?.Count > 0`。

- 基类的默认实现是 `Lifetime.IsAlive`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:14`），而
  `ApplicationState` 的初值是 `true` 且只有宿主主动 `SetAlive(false)` 才变（`Src/Core/VeloxDev.Core/Lifetime/IApplicationState.cs:13,22`）。
  也就是说**不覆写就没有「应用已退出」这个信号**。
- 七家里覆写 `IsAlive` 的只有三家：MAUI 用窗口数，Razor 用自报的 `_isAppRunning`（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:40`）、
  WinForms 用自报的 `_isAppAlive`（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:51`）；
  WinUI 走的是反写 `Lifetime.SetAlive(accepted)`（`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/UIThreadInspector.cs:56`）。
- **推论（未实测）**：MAUI 的窗口数在进程早期是 0，此时写路径按「已死」处理（`../extension.md` §7 的 `CanSetValue()`），
  所以「在 `App` 构造里就对某个还没上屏的对象起动画」这条路理论上会被拒写。要动这条判定前先实测。

### 2. 这里的定时器必须**可重复**，否则每条动画只画两帧

`TransitionInterpreter.cs:40-43` 的注释明写机制，且代码照此实现：

```csharp
// 必须重复：MAUI 的非重复计时器 fire 过一次之后 Start() 不再装填，于是每条动画画两帧就永久停住。
timer.IsRepeating = true;
```

- **这是本家与 WinUI 的正面对立**：WinUI 是 `timer.IsRepeating = false`（`Src/Adapters/VeloxDev.WinUI/PlatformAdapters/TransitionInterpreter.cs:42`）。
  两家的差异不是风格，是各自的定时器语义；`IsRepeating` 的取值**跟宿主走，不能跨家抄**。
- 重复不会多画：每一拍由 `FramePacerCore.Fire()` 先 `Disarm()` 再唤起续体（`TransitionInterpreter.cs:41`、`../extension.md` §二·5）。
- Razor 则干脆不提供 pacer（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/TransitionInterpreter.cs:3-6` 明写
  「Deliberately without a frame pacer」），所以「MAUI 缺 pacer 会怎样」不能拿 Razor 当参照。

### 3. 等待线程必须取自 `affinity.ThreadFor(target)`

`CreateFramePacer`（`TransitionInterpreter.cs:8-11`）先 `affinity.ThreadFor(target).TryGet<IDispatcher>`，拿到才建 pacer，且**用同一个
dispatcher 建定时器**。这是 `../extension.md` §二·5「从平台派生等待线程 → 每帧一次 dispatch」那条陷阱的正解，照抄时别把 dispatcher 换成 `Application.Current.Dispatcher`。

`ThreadFor` 的取值顺序（`UIThreadInspector.cs:28-43`）：先 `target as BindableObject` 的 `Dispatcher`，**拿不到才退到应用级**。
`IDispatcher` 只提供 `Dispatch(Action)` / `CreateTimer()`，所以 `PostCore` 的诚实报告就是它的返回值本身（`UIThreadInspector.cs:53`）。

两处「合法地为空」是要写对的：`BindableObject.Dispatcher` 在实体还没挂到 handler 上时会抛（`:36-39`），
`Application.Current.Dispatcher` 在应用的 dispatcher 还没建起来时会抛（`:20-24`），两者都被吞成 `null`。
于是 `ThreadFor` 可以返回一个**不含 IDispatcher 的 ThreadRef**，此时 `CreateFramePacer` 返回 null（回落 `ArmNextFrame` 默认实现，
循环会在第一帧后漂到线程池 —— `../extension.md` §F），`PostCore` 则返回 `false`（诚实报告「没入队」）。

### 4. `IDispatcherTimer` 的 tick 异常不会留在 MAUI 里

不是 Transition 侧的代码，但决定本家所有定时器回调必须自带 try：`IDispatcherTimer.Tick` 里抛出的异常 MAUI 不接，
直接冒到 WinUI 的 `UnhandledException`（dotnet/maui #12245，引用处 `Src/Adapters/VeloxDev.MAUI/Attached/Workflow/ViewManager.cs:191-193`、`…/WorkflowSlotLayoutBehavior.cs:357-359`）。
Transition 侧不需要额外处理 —— pacer 的 tick 只调基类 `Fire()`（`TransitionInterpreter.cs:48`），失败由采样路径收口。

---

## 三、与其它家的刻意背离

1. **`IsAlive` 用窗口数，别家要么自报、要么靠基类。** 这里的做法和其他家不一样，因为 MAUI 的 `Application` 是跨平台抽象，
   能观测到的生命周期信号只有 `Windows` 集合（`UIThreadInspector.cs:8`）；Razor/WinForms 各有自己的 app 存活字段，
   WinUI 用「入队被拒」反写，WPF/Avalonia/Jalium 干脆不报（基类默认恒活）。
2. **pacer 的定时器是可重复的，WinUI 是反的。** 这里的做法和其他家不一样，因为 MAUI 的 `IDispatcherTimer` 一旦
   `IsRepeating = false`，`Stop()` 后再 `Start()` 不再装填（`TransitionInterpreter.cs:40-41` 的机制说明，WinUI 对位行
   `Src/Adapters/VeloxDev.WinUI/PlatformAdapters/TransitionInterpreter.cs:42`）。
3. **多三家单精度采样器 + 一个 `Shadow` 采样器**（`PointFSampler`/`SizeFSampler`/`RectFSampler`/`ShadowSampler`）。
   这里的做法和其他家不一样，因为只有 MAUI 同时暴露单精度几何类型（§一）与 `Microsoft.Maui.Controls.Shadow`；
   WPF 的对应物是 `DropShadowEffectSampler`（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/`），别家没有对应类型。
4. **引用型值用 scratch 复用，但它的 brush 是「中点硬切」而不是插值。** `ShadowSampler` 每帧只改
   一个复用 `Shadow` 的四个字段（`Samplers/ShadowSampler.cs:27-37`），**`Brush` 在 `t >= 0.5` 时整体切换**（`:36`，注释自称
   "Simple transition handling"）。这和 `BrushSampler` 的「梯度细节丢失、退到 `gradientStops[0]` 当代表」是同一类让步：
   引用型且带内部结构的属性，本家只保证**容器级**平滑。改动画效果前先确认真实行为，别按「颜色会渐变过去」写用例。

---

## 四、改这里时最容易踩的坑

### 1. `RectFSampler` 的键与实现一度是两个不同的类型（已修，2026-09-20）

- **旧形状**：注册处 `Interpolator.cs:20` 是 `RegisterInterpolator(typeof(RectF), new RectFSampler());`，该文件只有
  `using Microsoft.Maui.Controls.Shapes;` 与 `using VeloxDev.Adapters.NativeSamplers;`（`:1-2`），**没有 `System.Drawing`**；
  而 `System.Drawing` 里根本没有 `RectF` 这个名字（它叫 `RectangleF`）⇒ 这个键只能是 `Microsoft.Maui.Graphics.RectF`。
  实现处 `Samplers/RectFSampler.cs` 却 `using System.Drawing;`、解的是 `System.Drawing.RectangleF`。
  **同名不是原因**：键是 `Type`，两者本可并存；错在**实现解的不是自己那条键的类型**。
- **旧后果**：声明为 Maui `RectF` 的属性（声明入口在本家自己的 `Transition.cs:112` 的 `Property(Expression<Func<T, RectF>>…)`）
  走注册表时，第一帧在采样器里抛 `InvalidCastException`；`SamplerSet.ApplyCore` 只报一次 `"Sampling"` 诊断然后
  `CancelQuietly()` —— **整条 run 被取消**，不是静默降级（`Src/Core/VeloxDev.Core/TransitionSystem/SamplerSet.cs:136-142`）。
- **为什么长期没露头**（两条独立原因）：① 单精度矩形在仓库里一直按 `System.Drawing.RectangleF` 用，而那个类型由
  **Core** 注册（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:22` 的 `RectangleFSampler`），根本不经过 MAUI 这条键；
  ② demo 的验收路径用 `SetInterpolator` 逐条覆盖，注册表被整个绕过（`Examples/Transition/MAUI/Demo/MainPage.xaml.cs:502`）。
- **修法（已落地）= 让体去解自己那条键的类型**：删掉 `using System.Drawing;`，改解 Maui `RectF`
  （`Samplers/RectFSampler.cs:12-13` 现在是 `(RectF)(start ?? new RectF())`）。名字 / 注册键 / 实现三处就此对齐。
  `System.Drawing.RectangleF` 的覆盖**没有丢**：它本来就由 Core 那条独立承担，纯数据套件里也一直有自己的表项
  （`Examples/Transition/AUTO TEST/Samplers/CoreSamplerEntries.cs:174` 的 `new RectangleFSampler()`，标着 `"Core"`）。
- **反方向为什么不能走**：把键改成 `typeof(System.Drawing.RectangleF)` 会经 `AddOrUpdate` **顶掉 Core 的
  `RectangleFSampler`**（安装是 last-writer-wins，`../extension.md` §二·1）—— 一个适配器版本静默替换 Core 的实现，
  之后两家各自演化。**同一个 `Type` 才会顶，同名不会。**
- **测试为什么抓不到这类错**：`SamplerCoverageTests` 对的是**采样器类型**集合（反射 vs 两张表），不是 `(键, 采样器)`
  配对，所以「键与体不符」在库里完全不可见（`Examples/Transition/AUTO TEST/Samplers/SamplerCoverageTests.cs:64-87`）。
  **这道网现已补上**（2026-09-20）：`Examples/Transition/AUTO TEST/Samplers/SamplerKeyTests.cs` 先把七家 + Core 的注册入口
  在这个进程里真跑起来，再问**真实注册表**「条目声明的类型解析到谁」，断言它等于条目写的那条采样器、且接得住该类型的值
  （`EveryEntry_ValueTypeResolvesToTheSamplerItNames` `:73`、`EveryEntry_SamplerTheRegistryResolves_AcceptsAValueOfThatKey` `:125`）。
  依据是条目新增的 `ValueType`（声明类型 = 注册键）与 `UnregisteredReason`（`SamplerEntry.cs:43,50`）。
- **这次一起动的三处连带**（都不在 `Src/` 里）：
  1. demo 的 subject 属性由 `SysRectangleF` 改 `MauiRectF`（`Examples/Transition/MAUI/Demo/SamplerSubject.cs:105-106,139`），
     端点工厂同步（同目录 `SamplerProbe.cs:170-171`）；
  2. 纯数据表 `MauiEntries` 的 `Target.BoundsF` 与端点（`Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs:38,184-196`）——
     注意这张表是**按名字反射**到 `VeloxDev.Adapters.NativeSamplers.RectFSampler` 的（`:61`），端点类型不对会直接炸；
  3. live 载荷的**类型标签**由 `"RectangleF"` 改 `"RectF"`（`SamplerProbe.cs:354`），`MauiConformance.cs:100` 同步 ——
     `ConformanceEntry.TypeTag` 的定义就是「产物必须有的类型名」，由 `ConformanceChecks.cs:434` 与 demo 报回的标签逐字比对。
     `LiveContract` 里 `["RectF"]` 是**新增**的键，`["RectangleF"]` 保留（它现在没有 live 生产者，但仍是 Core 那条
     `System.Drawing.RectangleF` 的形状声明）—— 这两条 key 都只有 MAUI/Core 一侧相关，改动不外溢。

### 2. 新加一个采样器时，「在哪一侧注册」比类名重要

`PointFSampler` / `SizeFSampler` / `RectFSampler` 都没有 `using System.Drawing`，解的都是 MAUI 的 `PointF`/`SizeF`/`RectF`
（`Samplers/PointFSampler.cs:12-13`、`Samplers/SizeFSampler.cs:12-13`、`Samplers/RectFSampler.cs:12-13`）—— 与各自的注册键一致。
所以「加一个单精度采样器」时：
先写 `(MauiXxxF)(start ?? …)`，再确认 `Interpolator.cs` 里注册的是**同一个**类型；`Interpolator.cs` 没有 `using System.Drawing`，
同名类型天然取 MAUI 那一侧，但**不要靠这个巧合** —— 一旦有人为了别的采样器加了 `using System.Drawing`，
文件里所有裸名 `PointF/SizeF` 会静默改指 `System.Drawing`（这两个名字在两边都有），`Interpolator.cs:13,18` 立刻变成注册 Core 的类型。
`Examples/Transition/MAUI/Demo/MainPage.xaml.cs:6`、`SamplerSubject.cs:3`、`SamplerProbe.cs:9` 三处的共同约定就是为此：
**同名类型一律显式取 MAUI 那一侧**。

### 3. 采样器必须无状态；引用型值只能落在 `ref object? working`

本家的 `ShadowSampler` 把 scratch 放在 `working`（`Samplers/ShadowSampler.cs:28-32`）、`BrushSampler` 同形，符合
`../extension.md` §二·1。反例的形状要知道：注册表是**进程级**的（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:39`），
把「本次动画」的字段挂在采样器上会直接串台。除 `TransparentBrush`（`static readonly`，只读共享，`ShadowSampler.cs:5`）外，
本家采样器不应有可变静态字段。

### 4. 别把 `ThemeValueConverters.cs` / `TransitionEffects.cs` 当成契约的一部分

它们与 TransitionSystem 的平台契约无关（见本文开头）。按 `../extension.md` §三·C 核对「八件套齐不齐」时，
这两个文件既不是缺项也不是多出来的实现；反过来，把它们的改动当成「适配器行为变更」去跑采样器验收套件是白跑。

### 5. 本家没有一处 `#if WINDOWS`

PlatformAdapters 下唯一的分支是 `Transition.cs:192` 的 `#if !NETSTANDARD2_0`（与 TFM 轴 `net10.0` / `net10.0-windows10.0.19041.0` 无关，
`Src/Adapters/VeloxDev.MAUI/VeloxDev.MAUI.csproj:8`）。**Workflow 侧相反** —— 那一侧大量使用 `#if WINDOWS` + `Handler.PlatformView`。
所以在 Transition 侧排查「为什么 Android 上行为不同」时不要去找 `#if`：两个 TFM 跑的是同一份实现，差异只可能来自
`IDispatcherTimer` / `BindableObject.Dispatcher` 的运行时行为。

---

## 附：写这份档案时**没能验证**的

- §二·1 的「窗口数为 0 时写路径拒写」是读代码推的，**没有实测**。
- §四·1 的修复验到两层：**解箱类型**由纯数据套件的 `EverySampler_MatchesItsClosedForm_AtEveryTime` 覆盖
  （用 `MauiRectF` 端点走一遍 `InsertFrame`），**注册表那条路**由 `SamplerKeyTests` 覆盖
  （真跑本家 `Interpolator` 的静态构造，再查 `TryGetInterpolator(typeof(Microsoft.Maui.Graphics.RectF))` 命中谁）。
  **仍未实测**的只剩**一条真动画**：demo 走的仍是 `SetInterpolator`（`Examples/Transition/MAUI/Demo/MainPage.xaml.cs:502`），
  所以「一条 Maui `RectF` 属性经注册表在真 app 里被逐帧写入」要 live 表 `MauiConformance.cs:100` 跑起来才算数。
