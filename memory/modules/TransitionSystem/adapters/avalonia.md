# TransitionSystem — Avalonia

> 代码：`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/`（八个类 + `Samplers/` 14 个采样器）。
> 契约、扩展点、注册位置在 `memory/modules/TransitionSystem/extension.md`；「怎么用这套 API」在
> `skills/veloxdev-create-animation/SKILL.md`，逐平台差异的写法在 `skills/veloxdev-create-animation/references/adapter.md`。
> 本文只写：这家必须实现哪些成员、这家的平台硬限制、与其它六家的刻意背离、改这里最容易踩的坑。
>
> **本文的平台事实用 Avalonia 11.1.0（仓库锁定的版本，`Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj`）
> 的运行时反射实测过**，不是从 WPF 类推来的。凡是类推会推错的地方都标了「实测」。

---

## 一、契约成员在这家的形态，以及为什么是这些形态

| 成员 | 这家的形态 | 为什么是这个形态 |
|---|---|---|
| `TPriorityCore` | `DispatcherPriority`（`TransitionScheduler.cs:7`、`TransitionEffect.cs:5`） | Avalonia 有 dispatcher 优先级，按 `extension.md` §三·C 的选型规则就该用它。**但它是 struct 不是 enum**，见 §二.1 |
| `Interpolator` 注册 | 14 项，`Interpolator.cs:13-26` | 注册的是**接口** `IBrush` / `ITransform`，不是基类 `Brush` / `Transform`。Avalonia 的 `IBrush` 是接口，注册接口能把非 `Brush` 的实现（`ImageBrush` 等）一并接住，走 `InterpolatorCore.TryGetInterpolator` 的接口分支（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:68-84`）。Jalium 反过来同时注册 `Brush` 与 `SolidColorBrush`，两种都对，差别只在覆盖面 |
| `CreateScheduler` | 判 `effect is ITransitionEffect<DispatcherPriority>` 后走 `FindOrCreate`（`Interpolator.cs:29-32`） | 契约要求的写法，无本家特殊之处 |
| `UIThreadInspector` | `ThreadFor` 恒返回 `Dispatcher.UIThread`，**不看 target**（`UIThreadInspector.cs:9`） | 平台所致，见 §二.2 |
| `InternalPriority` | `DispatcherPriority.Send`（`UIThreadInspector.cs:14`） | 必须覆写；不覆写在这家会落到 `Default`（=0），理由与 Core 注释说的不同，见 §二.1 |
| 存活 | **完全不报**（`UIThreadInspector.cs` 没有 `IsAlive` / `Lifetime` 覆写） | 与 WPF 同属「不携带存活」的一类（`skills/veloxdev-create-animation/references/adapter.md:44` 明写「WPF and Avalonia carry none」）。Avalonia 不是没法报，是没报，见 §三.1 |
| pacer | 私有 `UiThreadFramePacer` 包一个 `DispatcherTimer`（`TransitionInterpreter.cs:12-47`） | `DispatcherTimer` 是这家唯一的 UI 线程定时器；无参构造即绑定唯一 dispatcher，见 §二.4 |
| `TransitionEffect.Priority` | `DispatcherPriority.Render`（`TransitionEffect.cs:7`） | 与 WPF/Jalium 取值相同。注意 Avalonia 的 `Render` = 4 = `DataBind`（别名），`Normal` 是 5 —— 名字直觉在这家不成立，见 §二.1 |
| `Transition<T>` | 泛型 `Property<TValue>`（`Transition.cs:35`）+ 26 个显式重载（`:42-234`） | 泛型那一条覆盖大部分场景；显式重载是为了**保留值类型的装箱前形态**与 `Expression` 的重载解析，见 §三.4 |
| `State` | 空实现（`State.cs`） | 同六家 |

注册位置、契约违背的后果**不在本文**：见 `extension.md` §二、§三·C。

---

## 二、平台硬限制（本篇最有价值的部分）

### 1. `DispatcherPriority` 是 **struct**，不是 enum —— 两处后果

11.1.0 实测：`typeof(Avalonia.Threading.DispatcherPriority).IsEnum == false`、`IsValueType == true`、`IsSealed == true`。取值（`Value` 属性）：

```
Invalid -7 · Inactive -6 · SystemIdle -5 · ApplicationIdle -4 · ContextIdle -3 · Background -2
Input -1 · Default 0 · Loaded 1 · UiThreadRender 2 · AfterRender 3 · Render 4 · BeforeRender 5 · AsyncRenderTargetResize 6
别名：DataBind == Render(4) · Normal == BeforeRender(5) · Send == AsyncRenderTargetResize(6) == MaxValue
```

**后果一 —— Core 的一条 remark 在这家不成立。** `Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:38-43` 的 `InternalPriority` remark 写着「`default(DispatcherPriority)` 是 `Inactive`，这条读要等消息泵完全空闲才跑」。**对 WPF 的真实枚举成立**（`default` = 0 = `Inactive`），**对 Avalonia 不成立**：这家 `default` = 0 = `Default`，`Inactive` 是 **-6**。所以：

- 在 Avalonia 上不覆写 `InternalPriority` 的后果不是「停顿一下」，而是「排在默认优先级」；
- 这家实际覆写成 `Send`（最高优先级，`:14`），于是 `Prepare` 的阻塞读（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:147`，`host.Run<object?>`）会插到所有普通消息之前。**这是刻意的，不是笔误** —— 读阻塞的是发起动画的那个线程，越早回来越好。
- 给新平台写适配器时不要照抄「default 是 Inactive」这句话去判断自己的优先级选型，先确认自己的 `DispatcherPriority` 是不是 enum、`default` 落在哪。

**后果二 —— `Send` 不等于同步执行。** 11.1.0 的 `Dispatcher.InvokeAsync(Action, DispatcherPriority)` 两参数重载只是转调三参数版（IL 实测，无 `Send` 分支）。所以 `PostCore` 里的 `dispatcher.InvokeAsync(action, priority)`（`UIThreadInspector.cs:20`）在 `priority == Send` 时**仍然是排队**，不会在调用线程上跑。这一点能成立，`PostCore` 的「不阻塞」契约（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:27-31`）才成立。

### 2. Avalonia 没有 `DispatcherObject`，也没有逐对象的 `Dispatcher` 属性

11.1.0 实测：`AvaloniaObject` / `Visual` 上**都没有** `Dispatcher` 属性；`Avalonia.Threading.Dispatcher` 只暴露静态 `UIThread`（公开成员里没有 `DispatcherObject` 这个类型）。因此：

- `ThreadFor` 只能 `=> ThreadRef.From(Dispatcher.UIThread)`，**没有任何 target 侧信息可问**（`UIThreadInspector.cs:9`）。
- 这与 `adapter.md:51` / `extension.md:100` 的通则「`IsCurrentThread` 默认从 target 推」冲突 —— **通则的前提在 Avalonia 11 不存在**（这个前提是 WPF 的 `DispatcherObject.Dispatcher`、WinUI 的 `DependencyObject.DispatcherQueue`、MAUI 的 `BindableObject.Dispatcher` 提供的）。结论仍然正确（Avalonia 的视图本来就只能在 UI 线程创建），但**推理链不同**：这里不是「从 target 推出来正好是 UI 线程」，而是「只有一个 UI 线程可推」。
- `IsCurrentThread` 因此也只能问那唯一的 dispatcher：`thread.TryGet<Dispatcher>(out var d) && d.CheckAccess()`（`UIThreadInspector.cs:11-12`）。`CheckAccess()` 是平台的「我是不是这条线程」谓词，等价于 WPF 的 `Dispatcher.CheckAccess`。
- 推论：**在这家无法区分「target 属于别的 dispatcher」**。如果 Avalonia 将来支持多 dispatcher，这两处是首先要改的。

### 3. `DispatcherTimer` 在 11.1.0 **没有 `IsRepeating`** —— 它永远重复

11.1.0 实测成员只有 `Dispatcher` / `IsEnabled` / `Interval` / `Tag` 加 `Start` / `Stop`；**没有 `IsRepeating`**。两面影响：

- **好的一面**：这家天然没有 WinUI/MAUI 那条坑（`IsRepeating = false` 会让动画恒为两帧，`adapter.md:63`）。`extension.md` 要求的「`Arm` 的定时器必须可重复」在这家是默认满足的，代码里也就没有对应的赋值。
- **要注意的一面**：**不要把「可重复」的表达能力当成七家通用**。升级 Avalonia 版本时要重新判断这一条 —— nuget 缓存里已有 11.3.x / 12.x，将来的版本可能补上 `IsRepeating`，那时这家就会和 WinUI/MAUI 一样需要显式判断。判断依据是版本，不是记忆。

### 4. `new DispatcherTimer()` 无参构造就绑到唯一的 UI dispatcher

`TransitionInterpreter.cs:40-41` 的注释明写这一点。所以这家的 pacer **不需要**先从 target 解析出 `Dispatcher` 再构造定时器 —— 与 WPF / WinUI / MAUI / Jalium 的 `ThreadFor(target).TryGet<TSpecific>(out var d)` 形态不同（那四家都必须把 dispatcher 传进 pacer）。Avalonia 的 `DispatcherTimer` 的构造函数注入路径仍然存在（能传 `Dispatcher`），但在这家没有第二个 dispatcher 可用，传了也是同一个。

### 5. `DispatcherTimer` 持有平台定时器：`Stop()` 不够

`TransitionInterpreter.cs:29-35`：`Dispose` 里除了 `base.Dispose()`（放行挂着的续体），还要 `Stop()` + `Tick -= OnTick` + 置 `_timer = null`，注释写明理由「Avalonia 的 DispatcherTimer 持有平台定时器，只停表不够」。这正是 `adapter.md:70` 说的「宿主定时器有自己的 teardown」那一类，与 WinForms 同级。

### 6. 同时目标 `netstandard2.0` 与 `net6.0`

`Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj` 的 `TargetFrameworks = netstandard2.0;net6.0`。后果：

- `System.Numerics` 的重载与 Core 一样带 `#if !NETSTANDARD2_0` 守卫（`Transition.cs:209`、`234`）。
- `BoxShadowsSampler.cs:100-105` 留了一段 `#if NETSTANDARD` 的 `Clamp(byte, byte, byte)` —— **全文读完确认零调用者，两个 TFM 下都是死代码**（该文件里 `Channel` 自己做饱和，见 `:93-98`）。这类残留是「照着别家抄一半」留下的，不要以为它说明了什么平台限制。

### 7. 为什么这家的采样器比别家宽：**类型系统驱动，不是实现驱动**

这是最容易被误读成「Avalonia 更完整」的一处。把四家的注册表摊开对比（`VeloxDev.{Avalonia,WPF,WinUI,MAUI}/PlatformAdapters/Interpolator.cs` 的静态构造）：

| 家 | 独有项 | 为什么只有它有 |
|---|---|---|
| Avalonia | `PixelPoint` `PixelSize` `PixelRect`、`RelativePoint` `RelativeRect`、`BoxShadows` | Avalonia 把这些概念**做成了独立的取值类型**：`Window.Position` 是 `PixelPoint`（实测），`Screen.Bounds` 是 `PixelRect`，`LinearGradientBrush.StartPoint` 是 `RelativePoint`（实测），圆角阴影是 `Border.BoxShadow` 上的 `BoxShadows` 值类型。WPF/MAUI 里对应概念是 `double` / `Point` / `double` / `Effect` 对象，**没有新的值类型可注册**（效果那一格上 WPF 有一条 `Effect` 抽象基类的键、MAUI 是 `Shadow`，见下一行） |
| WPF | `Point3D` `Vector3D` `Effect`（**键是抽象基类**，服务整个效果家族，实作在 `DropShadowEffectSampler`） | WPF 有 3-D 场景类型（`System.Windows.Media.Media3D`）与 `Effect` 继承树。**Avalonia 11 完全没有**：实测 `Avalonia.Media.Media3D.Point3D`、`Avalonia.Media.Effects.Effect`、`Avalonia.Media.Effects.DropShadowEffect` 三者都不存在。Avalonia 唯一的 3-D 面是 `Rotate3DTransform`（是 `Transform`，已被 `ITransform` 覆盖），唯一的「效果」就是值类型 `BoxShadows` |
| WinUI | `Projection` | WinUI 的 3-D 变换叫 `Projection`，是一个独立属性类型；Avalonia 的等价物是 `Rotate3DTransform`（属 `Transform`） |
| MAUI | `PointF` `SizeF` `RectF` `Shadow` | 同一逻辑：`Microsoft.Maui.Graphics` 的单精度类型与 `Shadow` 类 |

**一句话结论：采样器覆盖面是「平台把多少概念命名成了取值类型」的函数，不是「适配器写得全不全」的函数。** 新平台写注册表时按本家类型集合去盘，不要按本表的差异去凑。

**唯一的例外（这一处不是类型驱动，是缺口）**：Avalonia **有** `Avalonia.Rect` 与 `Avalonia.Vector`（实测存在；`Visual.Bounds` 就是 `Avalonia.Rect`），但注册表里既没有 `Rect` 也没有 `Vector`，`Samplers/` 目录下也没有对应的采样器文件（14 个文件全数列过）。而 WPF、WinUI、MAUI **三家都注册了 `Rect`**。代码与注释里没有任何地方解释这个缺口 —— **视为遗漏而非背离**：Avalonia 上任何 `Rect` / `Vector` 类型的属性动画都会走 `Warn("Unsampled")` 静默跳过（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:175-179`）。要不要补是一个决定，但**不该被当成「Avalonia 就是这样」**。

---

## 三、与其它家的刻意背离

### 1. `PostCore` 乐观返回 `true`，且完全不报存活

`UIThreadInspector.cs:16-22` 只检查「能不能拿到 `Dispatcher`」，拿到就 `InvokeAsync` 并 `return true`；**没有任何存活判断**，`IsAlive` 也从未覆写（恒 `true`）。

- 七家的存活机制**没有一家相同**，逐个记：**覆写 `IsAlive` 的三家** —— MAUI 用 `Windows.Count > 0`、WinForms 挂 `Application.ApplicationExit` 存一个 `_isAppAlive`、Razor 提供手动 `NotifyShutdown()`；**不覆写但问了平台的两个** —— WPF 与 Jalium 在 `PostCore` 里检查 `dispatcher.HasShutdownStarted`（同一份守卫）；**不覆写但回报基类的** —— WinUI 在 `PostCore` 里 `Lifetime.SetAlive(queue.TryEnqueue(...))` 双向报告。⇒ **七家里只有 Avalonia 一个存活信号都没有**，`IsAlive` 恒 `true`。别把「Avalonia 与 WPF 都不携带存活」当成一条 —— WPF 至少看了一眼 shutdown，而 Jalium 与它情况完全相同。
- **这里的做法和其他家不一样，因为 Avalonia 的 `Dispatcher` 没有可查询的 shutdown 属性**：11.1.0 实测 `Dispatcher` 的公开成员里**没有** `HasShutdownStarted` 之类的 bool；它有的是 `ShutdownStarted` / `ShutdownFinished` **两个事件**（实测）。要用事件做存活，就得订阅后自己存一个标志 —— 这正是基类给的 `Lifetime`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionHostBase.cs:12` + `ApplicationState`）的用途，而这家**没有接**。
- **代价是清楚的**：`ThreadDispatcherBase.PostAsync` 的等待条件是「动作被真正接受」（`Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:63-72`，注释「宿主静默丢掉的动作永远不会完成它的 TCS，等下去就是等一辈子」）。dispatcher 已关闭后仍返回 `true`，就是让消费者挂到进程结束。这条在 Avalonia 上**可以修**（订阅 `ShutdownStarted` → `Lifetime` 报 false），但目前没修 —— 评估「要不要修」时按上面的事实判断，不要按「WPF 也这样」判断，因为 WPF 那边是真的没有同步信号、只能靠 `HasShutdownStarted`。

### 2. `GridLength` 单位不同时**保持起始值**，WinUI 切到结束值

`Samplers/GridLengthSampler.cs:17-22`：单位不同就 `SetValue(target, g1)` 直接返回，**永远到不了终点**。WinUI 的同名采样器是 `property.SetValue(target, t >= 1 ? g2 : g1)`（`VeloxDev.WinUI/PlatformAdapters/Samplers/GridLengthSampler.cs:18-23`）。

**这里的做法和其他家不一样，因为 Avalonia 的这条分支选择了「不动」而不是「收敛」**：单位不一样（`Auto` vs `Star` vs 像素）时没有可插值的量，两种选择都是「离散切换」，区别只在切换点。`skills/veloxdev-create-animation/references/adapter.md:106` 明确站 WinUI 那边：「prefer the branch that still reaches the target over the one that holds the start ... only the former ever arrives」。**要保持一致的话，这处该改成 WinUI 的写法**；保持现状则要接受「跨单位的 GridLength 动画在 Avalonia 上画不出终点」这个静默结果（不抛、不报，只是永远停在起点）。

### 3. 同一类分支还有两处：`RelativePoint` 与 `RelativeRect`

`Samplers/RelativePointSampler.cs:16-21`、`Samplers/RelativeRectSampler.cs` 是同一个形状：「单位不同无法插值；保持起始值」。这两处**没有对应家可对比**（别家没有 `RelativePoint`/`RelativeRect` 类型），所以不是背离，而是**同一条决策被复制到三个采样器上**：改 §三.2 时要顺手看这两处，否则会得到「GridLength 会收敛、RelativePoint 不会」的不一致。

### 4. 重载表：`Point3D/Vector3D/Vector` 没有（与 WPF 的类型集差），`ICollection<Transform>` 那一条是独有

`Transition.cs:166-207` 有完整的 `System.Drawing` 重载（`Point` / `PointF` / `Size` / `SizeF` / `Color` / `Rectangle` / `RectangleF`），`:209-234` 有 `System.Numerics` 的（`Vector2` / `Vector3` / `Vector4` / `Quaternion`）。这套与 WPF 一致（`System.Drawing` 与 `System.Numerics` 在这两家都是**有采样器的 Core 类型**，`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:16-27`），所以是**共同做法而非背离**。真正的本家特点只有一条：

- **`Property(Expression<Func<T, ITransform?>>, ICollection<Transform>)` 单列一个重载**（`Transition.cs:42`），且 `Count == 1` 时**不包装**、直接把那个变换赋进去（`:44-51`），注释写明理由「单个 transform 直接赋值以保留运行时类型：包成 `TransformGroup` 会改变运行时类型，破坏 `((TranslateTransform)x.RenderTransform).X` 这类嵌套路径」（`:46-47`）；只有多个才包成 `TransformGroup`。**别家没有这一条重载**：这是这家给「一次声明一组变换」开的独立入口，形状由「一个变换不能被无谓地包装」这条要求定下来。

### 5. 其余「看着像差异、实际不是」的，列出来免得下一个 agent 白查

- `decimal` 重载：`Transition.cs:160` 声明了 `decimal`，但**全仓库没有 `DecimalSampler`、也没有 `RegisterInterpolator(typeof(decimal), …)`**（Core 与七家适配器都搜过）。所以 `Property(x => x.SomeDecimal, 1.5m)` 编译通过、运行正常、**该属性被静默跳过**（每趟一次 `Warn("Unsampled")`）。**这是六家共有的缺口**（WPF/WinUI/MAUI/WinForms/Razor/Avalonia 都声明了 `decimal`），不是 Avalonia 的差异 —— 但在 Avalonia 上写动画时最容易撞上，所以记在这里。
- 渐变代表色取**第一个** stop（`BrushSampler.cs:155`）：与 MAUI 相同，WinUI 与 Jalium 取**最后一个**，WPF 根本不走这条路（它做位图交叉淡化）。**四家两派、没有约定**，不是背离；新写一家时选定一种并写进注释即可。
- `CornerRadius` **不做任何钳制**（`Samplers/CornerRadiusSampler.cs:16-20`），而 WinUI 逐角 `ClampAtZero`（`VeloxDev.WinUI/.../CornerRadiusSampler.cs:21-27`，`adapter.md:102` 推荐 WinUI 那种）。**这不是抄漏**：WinUI 必须钳是因为它的 `CornerRadius` 构造函数会校验并**抛异常**；Avalonia 的不会。实测：`new Avalonia.CornerRadius(-5,-5,-5,-5)`、`new Avalonia.Thickness(-1)`、`new Avalonia.Size(-1,-1)`、`new Avalonia.PixelSize(-1,-1)`、`BoxShadow{ Blur = -1 }` **全部构造成功**。所以「必须钳否则抛」这条通则**在 Avalonia 不成立**。
  - 反过来也成立：`new Avalonia.Controls.GridLength(-1)` **抛 `ArgumentException: Invalid value`**（实测），所以 `GridLengthSampler.cs:27` 的 `Math.Max(0, …)` 是**必须的**，不是保险。这一对（`CornerRadius` 不钳、`GridLength` 钳）看起来矛盾，实际是两家构造函数校验策略不同 —— 判断依据只能逐个类型实测，不能按「值类型都要钳」类推。

---

## 四、改这里最容易踩的坑

1. **`PixelRectSampler` 的注释在说一件不成立的事。** `Samplers/PixelRectSampler.cs:22-23` 写「stop at zero: a negative size is not representable」，但实测 `new Avalonia.PixelRect(0,0,-1,-1)` **构造成功**，`new Avalonia.Size(-1,-1)` 也成功 —— Avalonia 里「负尺寸」是**可表示的**（只是渲染出来没意义）。所以 `:30-31` 的两处 `Math.Max(0, …)` 是**约定**而不是平台要求。同类注释在 `Samplers/PixelSizeSampler.cs` 与 `SizeSampler.cs` 也有。照着这句去推断「Avalonia 会在某处抛异常」是错的；真正会抛的只有 `GridLength`（见 §三.5）。
2. **`BoxShadowsSampler` 没有用 `working`，每帧分配。** `:26` 每帧 `new List<BoxShadow>()`，`:48` 每帧 `GetRange(1, …).ToArray()`。`ISampler` 的 scratch 约定（`Src/Core/VeloxDev.Core/Interfaces/TransitionSystem/ISampler.cs:31-36`）是「每动画临时值放 `ref object? working`」，这个采样器没遵守 —— 功能正确，但在采样热路径上每帧两次分配。要优化时先看这里。
3. **一对多的 BoxShadow 没有淡入淡出，只有「取存在的那一侧」。** `:60-64`：某索引只有一侧存在时直接返回那一侧（不是插值）。后果是**增加阴影时，新阴影从第一帧就是它的终值**（`s1 == default` → 返回 `s2`）；**减少阴影时，多的那条一直保持起始值到最后一帧才消失**（`s2 == default` → 返回 `s1`）。这是本家独有的多阴影路径，没有别家可对比；`BoxShadows` 是唯一一个「值里有集合」的采样器，任何改动都要同时照看这两个方向。
4. **`IsInset` 在 `t = 0.5` 切换**（`:73`）。它只能在离散点取值（`BoxShadow.IsInset` 是 bool），所以用的是 `adapter.md` 说的「离散值用阈值切」而不是插值。改这里时不要试图「平滑」，bool 没有中间态。
5. **`TransformSampler` 的 `IsKnownTransform` 门禁是「防抛」而不是「白名单」。** 只有名单上的六种（`TranslateTransform` / `RotateTransform` / `ScaleTransform` / `SkewTransform` / `Rotate3DTransform` / `MatrixTransform`，`Samplers/TransformSampler.cs:58-59`）才走「就地变更暂存对象」的快路径；`CloneTransform`（`:61-70`）对名单外的类型**抛 `InvalidOperationException`**，所以自定义 `Transform` 子类被刻意挡在快路径外，走矩阵路径。名单上方的注释（`:54-56`）就是写这件事的。
   - **因此：新增自定义 `Transform` 子类时不要顺手把类型名加进 `IsKnownTransform`** —— 加进去 = 走进快路径 = 撞 `CloneTransform` 的 `throw`。要在快路径上支持它，得同时补 `CloneTransform` 与 `MutateInPlace`（`:72` 起）两个 `switch`。
   - 不改名单的后果是自定义子类走矩阵路径：能动画，但产物是 `MatrixTransform` / `TransformGroup`（端点两帧例外，`:22-23` 会把调用方自己的实例原样写回，所以嵌套路径的运行时类型仍然保得住）。
   - 这是这家采样器里唯一一处「类型名单」硬编码，也是唯一一处会抛的路径。
6. **`netstandard2.0` 那条腿不要按想象改。** 框架列表在 csproj 里（§二.6）；`Transition.cs:209/234` 与 `BoxShadowsSampler.cs:100` 的 `#if` 都是为它服务的。`netstandard2.0` 下没有 `System.Numerics` 的那四个重载，声明端会少一批 —— 调试「某个重载找不到」时先确认 TFM。
7. **`Examples/Transition/AUTO TEST/Samplers/AvaloniaEntries.cs` 用程序集限定反射取采样器类型**（`SamplerAssembly.GetType("VeloxDev.Adapters.NativeSamplers." + name)`）。原因写在文件里：WPF/WinUI/Jalium 把**同名**采样器（`BrushSampler`、`PointSampler`……）放在同一个命名空间 `VeloxDev.Adapters.NativeSamplers`，编译期直接写类型名会 CS0433。**给 Avalonia 加采样器时照抄这条反射路径**，不要在测试工程里直接引类型名。
   - 顺带：Avalonia 的 14 个采样器**全部能在纯数据进程里构造**，所以 `UnreachableSamplers.cs` 里**没有 Avalonia 的条目**（那份只有 WinUI 3 个与 MAUI 3 个）。这是这家测试面比别家轻的原因。
