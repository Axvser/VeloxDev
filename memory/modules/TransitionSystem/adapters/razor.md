# TransitionSystem — Razor (Blazor) 适配器

> 代码：`Src/Adapters/VeloxDev.Razor/PlatformAdapters/`（项目名 `VeloxDev.Razor`，demo 目录却叫
> `Examples/Transition/Blazor/` —— 这处命名不对称在 `Src/Adapters/` 与 `Examples/` 之间是既成事实，找东西时两边都要看）。
> 契约与共性见 `../extension.md`、`../architecture.md`（帧与编组）。
> **逐平台怎么写一个适配器**见 `skills/veloxdev-create-animation/references/adapter.md`，本文不重复。

---

## 一、契约成员里哪些在这家有实质内容，为什么

八个类一个不少，但只有三个承载这家的答案，其余五个是「契约默认行为恰好就是对的」。判断哪三个值得读，
不用看类体长短，只要问一句：**这个成员要不要接触「浏览器/电路」这个概念？**

| 成员 | 这里的实质内容 | 为什么只有它有事做 |
|---|---|---|
| `Interpolator` | 注册 `string` 一个采样器 + `CreateScheduler` 的选择依据 | 唯一要回答「这家平台上什么东西可动画」的地方；答案与别家完全不同（§二·3） |
| `UIThreadInspector` | 线程归属 = **circuit 的** `SynchronizationContext` | 唯一要回答「UI 线程是谁」的地方，而在这家这个答案不是进程级的（§二·4） |
| `Transition<T>` | 逐类型手写的 `Property` 重载表 | 唯一要回答「用户能声明出什么」的地方，而这张表是手写的、因此有缺口（§二·3 末） |
| `State` / `TransitionEffect` / `TransitionEffects` / `TransitionScheduler` / `TransitionInterpreter` | —— | `TransitionScheduler` 只是把三个型参绑好；**`TransitionEffect` 类体为空、连 `Priority` 都不声明**，靠 Core 非泛型基类已经实现好的 `ITransitionEffect<NonPriority>`（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs:38-43`）—— 这是「这家的 `TPriorityCore` = `NonPriority`」的必然结果，别等着一份优先级默认值；`TransitionInterpreter` 的类体为空**本身就是这家的核心决定**，见 §二·2 |

`Transition<T>` 的 `where T : class`（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/Transition.cs:18`）不是随手加的约束，
它是这家的动画目标形状（§二·1）。

---

## 二、平台硬限制与由此产生的做法

### 1. 服务端进程里没有元素对象

**限制。** Blazor Server 的 UI 元素是**浏览器**里的 DOM，服务端进程里没有它的任何对象；中间隔着 SignalR。
所以「动画一个控件」在这家不存在，能做的是：**动画一个服务端的普通对象（POCO ViewModel），再由渲染把它的值变成 CSS**。

**做法。** 动画目标是用户自己的 POCO —— demo 的 `BoxModel` 就是这样：它是一个 `INotifyPropertyChanged` 对象
（`Examples/Transition/Blazor/Demo/Demo/Models/BoxModel.cs:11`），其中 `Style` 属性负责把若干字段拼成一段 CSS 串
（同文件 `:74-79` 的 `transform:translateX(..) translateY(..) rotate(..) scale(..)`）。
适配器侧没有任何元素可挂，于是**「什么时候重绘」变成用户的责任**：demo 在 `Home.razor.cs` 里显式调
`UIThreadInspector.CaptureUIThread()`（`Examples/Transition/Blazor/Demo/Demo/Components/Pages/Home.razor.cs:998`），
并逐个对象挂 `PropertyChanged += (_, _) => InvokeAsync(StateHasChanged)`（同文件 `:1005`、`:1008`）。

**推论（写新用例时先想这一条）。** 别家的 `LateUpdate += InvalidateVisual` / `effect.LateUpdate += (_,_) => 重绘`
之所以能成立，是因为动画目标**就是**渲染对象；这里动画目标与渲染对象之间永远隔着一层用户写的 `PropertyChanged` 桥。
`Transition<T>` 上的 `T` 是 POCO，接不到任何渲染回调。

### 2. 没有「渲染线程上的定时器」→ 全模块唯一没有 pacer 的一家

**限制。** Blazor 没有一个在「渲染器自己的线程」上 tick 的定时器 —— 服务端根本没有渲染器线程，渲染发生在浏览器。
`TransitionInterpreter` 的 XML 把这句写成了结论：`Deliberately without a frame pacer: Blazor has no timer that fires on
the renderer's own thread.`（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/TransitionInterpreter.cs:3-8`，类体为空）。

**这条「唯一」是有据可查的**：七家里六家都覆写了 `CreateFramePacer`（Avalonia / Jalium / MAUI / WPF / WinForms / WinUI
各自的 `PlatformAdapters/TransitionInterpreter.cs`），**只有 Razor 没有** —— 它的那个文件里只有类声明。

**由此产生的做法（这是本篇最该记住的一条链）。** 基类 `CreateFramePacer` 默认返回 `null`
（`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:79`）→ `ArmNextFrame` 落到 `ReusableTimerWait`
（同文件 `:94-110`，`_pacer is null` 分支）→ 整个循环共用一个 `System.Threading.Timer`
（`Src/Core/VeloxDev.Core/TransitionSystem/ReusableTimerWait.cs`），续体在**线程池线程**上跑。
而 `FrameWait` 刻意**不**还原 `SynchronizationContext`（同文件 `:112-127` 明写「a loop started on a UI thread
therefore drifts to a pool thread after its first frame unless the host supplied a pacer」）。

结论：**在这家，第一帧之后 `Update` / `LateUpdate` 就在线程池线程上跑了。**
- 想在这两个回调里碰渲染 → 必须自己 `InvokeAsync`，否则死在 `SynchronizationContext` 上。
- 「每帧一次 dispatch」这件在别家是要避免的事（`../extension.md:89` 的捷径表里写着「pacer 与写路径不一致 → **每帧一次 dispatch**，正是采样路径要避免的那件事」），
  在这家是**默认且唯一**的路径：属性写入经 `SamplerSet.Apply` → `UIThreadInspector.PostCore` → `context.Post`，一帧一次。

这与 mem 里那条「Blazor 刻意没有 pacer」是同一件事的两个面，别把「没有 pacer」当成漏写 —— 它是**做不到**，
而且理由写在了类上。

### 3. 浏览器里没有那些 CLR 类型 → 所以只有一个采样器（Q1）

**事实。** 这家的静态构造只注册了一个采样器：`RegisterInterpolator(typeof(string), new StringSampler());`
（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/Interpolator.cs:7-10`），`PlatformAdapters/Samplers/` 下也只有这一个文件。

**先把「数量对比」这件事本身说准**（它比看起来更容易被误读）。七家的注册数：
WPF 12、Avalonia 14、MAUI 12、WinUI 10、Jalium 10、**WinForms 1、Razor 1**（各家 `PlatformAdapters/Interpolator.cs`
静态构造里 `RegisterInterpolator` 的条数）。所以「只有一个采样器」不是 Razor 独有 —— **WinForms 也只有一个**。
但两者性质完全不同，这正是这条问题的答案：
- WinForms 的那一个是 `RegisterInterpolator(typeof(Padding), new PaddingSampler());`
  （`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/Interpolator.cs:9`）—— `System.Windows.Forms.Padding`
  是**那个框架自己拥有的 CLR 类型**，只是 WinForms 的可动画面确实只剩这一个值得注册。
- Razor 的那一个是 `string` —— `string` **不是 Blazor 的类型**，它是**服务端把值送到浏览器去的唯一载体**。

**真实原因（不是「偷懒」也不是「缺功能」）。** Core 的静态构造**已经把每一个可动画的 CLR 值类型装好了** ——
`double`/`float`/`int`/`long`/`Point`/`PointF`/`Size`/`SizeF`/`Color`/`Rectangle`/`RectangleF`
以及 `Vector2/3/4`/`Quaternion`，共 15 个（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:12-28`），
查找还会向上走「精确类型 → 基类由近及远 → 接口按名字序」（同文件 `:50-87`）。
别家那 12 / 14 个采样器**全都是自己 GUI 框架里的类型**（WPF 的 `Brush`/`Transform`/`Thickness`/…，Avalonia 的 `IBrush`/`ITransform`/…），
它们之所以要注册，是因为**那些 CLR 类型只存在于那个框架的进程里**。

Blazor 这一侧没有这样的类型：**没有任何「可写的元素属性」是 CLR 对象**，动画的产物是一段**字符串**（CSS / SVG 属性）。
于是 `string` 是这家唯一有意义的平台类型，而字符串插值恰恰需要自己的数学 ——
颜色要按 R/G/B 通道共用一条 `BoundedProgress` 进度、alpha 走自己的区间
（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/Samplers/StringSampler.cs:93-105`；
`BoundedProgress` 本身在 Core：`Src/Core/VeloxDev.Core/TransitionSystem/BoundedProgress.cs`），
非颜色串只能退化成**离散标记**：先判两端能不能解析成颜色，不能就挂 `DiscreteMarker`，起点保持到进度到 1 才换终点
（同文件 `:32-49`、`:57`）。

这家 demo 的动画面就是「几个 double + 一个 CSS 颜色串」（`Examples/Transition/Blazor/Demo/Demo/Models/BoxModel.cs:74-79`），
Core 那 15 个本来就够。

**由此得出的联动陷阱。** 这张表是**手写**的（`PlatformAdapters/Transition.cs:31-129`，十三组逐类型重载），
而不是别家那种泛型 `Property<TValue>`，于是它的两个边缘各有一个坑（`../extension.md:162` 已记 `long`）：
- **`long` 声明不出来**：Core 注册了 `LongSampler`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:15`），
  但这张手写表里没有 `long` 重载 → 在 Razor 上**根本写不出**这条路径，不是「写了不生效」而是「写不了」。
- **`decimal` 声明得出来但不会动**：表里有 `decimal` 重载（`PlatformAdapters/Transition.cs:55-60`），
  但 Core **从没注册过** decimal 采样器 → `Prepare` 走 `Warn("Unsampled")` 静默跳过。
  这一格在**六家都存在**（WPF/Avalonia/WinUI/MAUI/WinForms/Razor 的重载表都有 `decimal`；**只有 Jalium 没有**，它的 `Transition.cs` 里搜不到 `decimal`），不是 Razor 特有；写在这是因为手写表没有编译器帮你对账。

**加一个类型的完整动作**：写采样器 → 在该家 `Interpolator` 静态构造里注册 → **顺手在这张手写表里补一个重载**
→ 补 `Examples/Transition/AUTO TEST/Samplers/RazorEntries.cs` 的一行（现行只有 `SamplerRule.Saturate` 一条）
→ 跑 `SamplerCoverageTests`。漏第三步的后果与其他家不同：别家漏了是运行期静默，这里是**声明期就写不出来**。

### 4. `SynchronizationContext` 属于 circuit，不属于进程

**限制。** 一个 Blazor Server 进程同时跑很多 circuit，每个 circuit 有自己的同步上下文。
「UI 线程」在这家**不是一个进程级答案，而是「哪个 circuit 在问」**（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:42-51`
的 remarks 把这句写死了）。

**做法。** `ThreadFor` 返回**当前线程的 context 优先、首次捕获的兜底**（`:52-56`）；
`IsCurrentThread` 是 `SynchronizationContext` 的**引用相等**（`:58-60`）；
调度器在启动这趟动画的线程上把答案钉进 `TransitionRun.Thread`（`../architecture.md` §三），
这才让两个同时活着的 circuit 不会互相投递。

**陷阱。** `_uiSyncContext` / `_uiThreadId` 是 **static**（`:7-9`），所以它记住的是**第一个**触碰它的 circuit 的 context。
因此 `ThreadFor` 的兜底只在「动画从后台线程启动」这一种情形下有意义 —— 这正是 `CaptureUIThread` 的 XML 说的用法
（`:11-15`），别把那个 static 当成「本进程的 UI 线程」。同理 `IsAlive` 只由 `NotifyShutdown()` 翻牌（`:40`），
没有框架生命周期可依赖。

### 5. 编组的失败没有信号

**限制。** `SynchronizationContext.Post` 返回 `void`，队列已经消失（circuit 断了）也不抛、也不报。
`PostCore` 因此**无条件返回 `true`**，代码里原文承认了这一点：
`// Post 没有失败信号,只能按"已接受"记;真正的丢弃由帧侧的取消标记兜住。`（`UIThreadInspector.cs:62-69`）。

**与契约的表面冲突 —— 这里要标记一下。** `../extension.md:195` 写着「`PostCore` 必须诚实报告是否入队」，
而这里返回的是一个恒真的乐观值。读代码时不要把它当违规：**是平台做不到，不是没做**。
能被做的那半做到了 —— `if (!thread.TryGet<SynchronizationContext>(out var context)) return false;`（`:64`），
即「这个 `ThreadRef` 里没有 context」时报 `false`。

**实践结论。** 「这趟动画还在不在」的唯一可靠判据是 `run.Cts`（与宿主自己的 `Dispose`），
不要指望从 `Post` 的返回值上读到东西；真正兜住迟到帧的是 `SamplerSet.Apply` 里那两道 stale-frame 守卫。

---

## 三、与其它六家的刻意背离

1. **这里的动画目标天然是 POCO，别家天然是 UI 元素。** 别家的 `InvalidateVisual` / `Invalidate()` 能直接挂在
   `Transition` 的 `LateUpdate` 上，是因为目标就是被渲染的那个对象；这家必须由**用户代码**把
   `PropertyChanged` 桥接成重绘（§二·1），适配器给不了这个桥 —— 服务端进程里没有元素可读。
2. **这里的做法是「接入契约的默认路径」而不是「覆写它」。** 别家覆写 `CreateFramePacer` 把循环钉在 UI 线程上；
   这家**不覆写**（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/TransitionInterpreter.cs` 类体为空），
   让循环按 Core 的默认落到线程池定时器
   （`Src/Core/VeloxDev.Core/TransitionSystem/TransitionInterpreter.cs:94-110` 与 `:112-127` 的漂移说明），
   再靠 `UIThreadInspector` 把每一帧编组回去。理由：Blazor 没有渲染线程定时器可用（§二·2）。
   所以读这家的性能特征要用别家的相反直觉 —— **每帧一次 dispatch 是设计，不是退化**。
3. **这里的 `Property` 是手写重载表，别家是泛型。** 后果是「Core 有采样器的类型在这家可能声明不出来」
   （`long`）；反过来别家也不会遇到「声明得出来却没有采样器」这一格被放大（§二·3）。
4. **只在这家，「把数字写进字符串」是一条主线。** 别家的动画产物是 CLR 值（画刷、变换、数值），
   只有这家必须把 `double` 格式化进 CSS/SVG 属性串。因此**全仓库唯一必须把 `double` 格式化进标记文本的平台就是这家**
   —— 动画侧的实例见 §四·1，视图侧的全量清单在 `WorkflowSystem/adapters/razor.md`。

---

## 四、改这里时最容易踩的坑（带依据）

1. **Culture：把 `double` 格式化进 CSS。** 这是这家的标志性坑。反面（自己做对的样子）就在采样器里：
   `StringSampler` 输出颜色用 `string.Create(CultureInfo.InvariantCulture, ...)`（`Samplers/StringSampler.cs:120`），
   解析百分比用 `float.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture, ...)`（`:228`）。
   **采样器侧是干净的；坑全在采样器之外** —— 也就是用户/demo 把动画值拼进样式串的地方。
   已修的两个范本在 demo 里：`Examples/Workflow/Blazor/Demo/Demo/Components/Workflow/TemplateLinkView.razor.cs:128`
   的注释把规则写成了显式约定，`Css()` 走不变文化（`:312-315`，alpha 那一处的理由写在 `:311`：
   「它是周期的一个端点」），`N()` 同理（`:317`）。
   这条**为什么会变成 bug**，树里就能看全：光带颜色是一个 `string` 属性（`TemplateLinkView.razor.cs:133-134`
   的 `BandColor`，注释写明「周期写它，中间那个 stop 读它」），而它与三个偏移量一样**全部是动画状态**
   （`:127` 的注释：「本组件即动画对象，路径直接读视图，中间没有标量要映射回停靠点」）—— 串一旦成为动画端点，
   采样器就必须能解析它，于是小数点写法从「显示问题」升级成「正确性问题」；`:128` 的注释正是这条结论的落点。
   适配器侧仍有活体，全量清单见 `WorkflowSystem/adapters/razor.md`。凡是要在 Razor 上写「把 double 变进字符串」的新代码，
   先确认 `CultureInfo.InvariantCulture`。
2. **`Transition<T>.Execute` 是 `async void`，而 circuit 会在导航时销毁。** 别家的窗口通常活到进程结束；
   这家的 circuit 一导航就没了，动画却还在线程池线程上跑、还在往一个已死的 `SynchronizationContext` 上投帧
   （§二·5 的乐观 `true` 意味着**你不会收到任何抱怨**）。写用例时要在 `Dispose` 路径上退出动画，
   并在 `InvokeAsync` 之前检查自己的 `_disposed` 标记 —— demo 的读表定时器就是这么写的：
   `if (_disposed) return;` 在回调第一行（`Examples/Transition/Blazor/Demo/Demo/Components/Pages/Home.razor.cs:1020-1022`，
   标记声明在 `:827`），注释写明「定时器线程可能在 Dispose 之后才轮到」。
3. **静态状态在 Blazor Server 下的语义被放大了。** 一个进程多个 circuit，任何 `static` 字段都是**跨用户共享**的。
   这家的适配器里确实有两个 `static`（`UIThreadInspector` 的 context/线程号），它们能成立是因为语义恰好是
   「首个 circuit 的兜底」；**新增任何 `static` 之前先想清楚它是进程级还是 circuit 级** —— 视图侧为同一件事
   专门用了 `AsyncLocal`（`WorkflowSystem/adapters/razor.md` 有依据）。
4. **`StringSampler` 的端点语义依赖「写回调用方给的字符串本身」。** 它的 XML 明写端点精确靠 `t == 0` 写 start 原文、
   `t == 1` 写 end 原文（`Samplers/StringSampler.cs:18-19`、`:26-27`）；中间帧才解析颜色。所以
   **不要试图在端点做归一化**（例如统一成 `rgba(...)`）—— 那会改掉调用方声明的字面文本，
   在回放/快照路径上是可观察的行为变化。这条对「非颜色串」更硬：它们直接退化成离散标记（§二·3）。
