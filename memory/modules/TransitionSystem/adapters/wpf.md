# TransitionSystem — WPF

> **读法**：契约与注册位置在 `memory/modules/TransitionSystem/extension.md`，本文不重复；
> 人面向的「怎么写一个适配器」在 `skills/veloxdev-create-animation/references/adapter.md`，本文只指路不抄。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。代码在 `Src/Adapters/VeloxDev.WPF/PlatformAdapters/`。
> **路径写法**：下文的裸文件名（`Interpolator.cs:31`、`Samplers/TransformSampler.cs:230` 等）都相对 `Src/Adapters/VeloxDev.WPF/PlatformAdapters/`；引用 Core 或别家时一律写全路径。

---

## 一、这家要写什么，为什么是这些

八个类一个不少（表见 extension.md §三·C），但**七个型参里第七个填 `DispatcherPriority`** 这一个决定，牵动了这家所有的「有内容」的类：

| 因为填了 `DispatcherPriority` | 后果 |
|---|---|
| `Interpolator.CreateScheduler` 必须测 `effect is ITransitionEffect<DispatcherPriority>` | `Interpolator.cs:31`；写错 ⇒ 主题切换静默变瞬切 |
| 宿主**必须**覆写 `InternalPriority` | `UIThreadInspector.cs:29` 给 `DispatcherPriority.Send`；不写 ⇒ `default(DispatcherPriority)` 是 `Inactive`，`Prepare` 的阻塞读要等消息泵完全空闲（依据 `Src/Core/VeloxDev.Core/Threading/ThreadDispatcherBase.cs:39-44`） |
| `TransitionInterpreter` 的 pacer 有真正的「按优先级排队」语义可用 | `TransitionInterpreter.cs:8-11` |
| `TransitionEffect` 只需给一个默认优先级 | `TransitionEffect.cs:7` 的 `DispatcherPriority.Render` |

其余类是空壳，**不要以为漏写了什么**：`TransitionScheduler` 是非泛型空壳（`TransitionScheduler.cs:5-9`）、`State : StateCore` 空（`State.cs:3`）。

**`Transition<T>` 的 `Property` 一共 27 个重载：1 个泛型 + 26 个逐类型手写**（泛型在 `Transition.cs:35`，手写在 `:42` 起）。这不是 WPF 类型多 —— **26 个手写里只有 `Transform` 那一个（`Transition.cs:48-70`）有泛型版本表达不了的语义**：单个 transform 必须**直接赋值**而不能包进 `TransformGroup`，否则运行时类型变了，`((TranslateTransform)x.RenderTransform).X` 这类嵌套路径就断（注释在 `:52-53`）。其余 25 个是「照抄泛型体、只换签名」的等价物。

### 为什么这家有 12 个采样器（而 WinForms / Razor 各只有 1 个）

**采样器条数不度量工作量，它度量「这家框架自带的、且 Core 还没覆盖的可动画值类型有多少个」。**

- Core 已把**无 GUI 依赖**的一整族注册完（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:12-28`）：4 个基元 + `System.Drawing` 的 `Point/PointF/Size/SizeF/Color/Rectangle/RectangleF` + 4 个 `System.Numerics` 向量/四元数。
- **WinForms 的整个值类型面就是 `System.Drawing` 那一族**，Core 已经全注册了，只剩 `Padding` 一个没覆盖 ⇒ 所以只有 `PaddingSampler`（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/Interpolator.cs:9`）。
- **Razor 没有任何带类型的 UI 值类型**，它的动画面是内联 CSS 字符串 ⇒ 只有 `StringSampler`（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/Interpolator.cs:9`）。
- WPF 则自带一整族 `System.Windows.*` 可动画类型：`Point/Size/Rect/Vector/Thickness/CornerRadius` + 两个 Freezable 家族（`Brush`/`Transform`）+ `Effect`（服务整个效果家族，实作是 `DropShadowEffectSampler`）+ `Media3D` 的 `Point3D`/`Vector3D` + `Media.Color` ⇒ 12 个（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Interpolator.cs:14-27`）。
- 顺带校准一下别家的数，免得「WPF 最多」被当成事实（**数的是采样器类数 / `RegisterInterpolator` 条数**，两者仅在 Jalium 上不同 —— 它的 `BrushSampler` 同时注册 `Brush` 与 `SolidColorBrush`）：Avalonia 14/14、WPF 12/12、MAUI 12/12、WinUI 10/10、Jalium 9/10、WinForms 1/1、Razor 1/1。

**这条的副作用（最容易漏的一点）**：`System.Windows.Point/Size/Color` 与 Core 用 `System.Drawing` 注册的那三个**同名不同型**（Core 侧同名文件在 `Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/PointSampler.cs`、`SizeSampler.cs`、`ColorSampler.cs`），所以 WPF 必须**再写一份**采样器，放在 `VeloxDev.Adapters.NativeSamplers` 命名空间里（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/PointSampler.cs:3`）才能与 Core 的 `VeloxDev.TransitionSystem.NativeSamplers` 共存。⇒ **改 WPF 采样器时不要 `using VeloxDev.TransitionSystem.NativeSamplers;`**，那三个名字立刻歧义。这不是 WPF 独有：Avalonia 的 `PointSampler` 同形同namespace（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/Samplers/PointSampler.cs:3`）。

**没被覆盖的类型（要动它们就得自己写采样器）**：`System.Windows.Media.Matrix` 只被 `TransformSampler` 内部用（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/TransformSampler.cs:230` 的 `LerpMatrix`），**注册表里没有 `typeof(Matrix)`** ⇒ `Matrix` 类型的属性不可动画；`Media3D.Matrix3D`、`GradientStop` 同理。别以为「有 Transform 就有 Matrix」。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 有 dispatcher ⇒ 可以「问它」，不必自己记存活状态

`PostCore` 先 `TryGet<Dispatcher>` 失败返 `false`，**再看 `dispatcher.HasShutdownStarted` 返 `false`**，然后 `InvokeAsync(action, priority); return true;`（`UIThreadInspector.cs:31-38`）。

这家**不覆写 `IsAlive`** —— 关停后由 dispatcher 自己说。七家里同类写法的只有 WPF 与 Jalium（`Src/Adapters/VeloxDev.Jalium/PlatformAdapters/UIThreadInspector.cs:36` 是同一份守卫）；**Avalonia 同样有 dispatcher 却没有这道守卫**（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/UIThreadInspector.cs` 的 `PostCore` 里搜不到 `HasShutdownStarted`），代码里没写理由 —— 接新一家时按「有 dispatcher 就问它」写是合理默认，但别断言七家一致。

`NonPriority` 三家反过来**必须自己造存活状态**，因为没东西可问：MAUI 看 `Application.Current?.Windows?.Count`（`Src/Adapters/VeloxDev.MAUI/PlatformAdapters/UIThreadInspector.cs:8`）、WinForms 持一个 `_isAppAlive`（`Src/Adapters/VeloxDev.WinForms/PlatformAdapters/UIThreadInspector.cs:51`）、Razor 有个手动 `NotifyShutdown` 的 `_isAppRunning`（`Src/Adapters/VeloxDev.Razor/PlatformAdapters/UIThreadInspector.cs:40`）。**别把这三家的做法搬到 WPF**，也别反过来。

### 2.2 pacer：`DispatcherTimer` 天生可重复，但会在它所属的 dispatcher 上留下队列项

- 等待线程必须从 `affinity.ThreadFor(target)` 派生，拿不到就返回 `null`（退回 `ArmNextFrame` 兜底）：`CreateFramePacer` 写成 `affinity.ThreadFor(target).TryGet<Dispatcher>(out var d) ? new DispatcherFramePacer(d) : null`（`TransitionInterpreter.cs:8-11`）。**不要用 `Application.Current.Dispatcher` 代替** —— 那正是 extension.md §二·5「从平台而不是从 affinity 派生等待线程」的错法。
- `DispatcherTimer` **本身没有 `IsRepeating` 属性**（它按设计就是重复的），所以 adapter.md 里那条「一次性定时器 ⇒ 动画恒为两帧」的陷阱在这家**结构上不可能发生**。这是 WPF 比 MAUI 好写的地方。
- 反向的坑：**`Dispose` 必须 `base.Dispose()` + `_timer?.Stop()` + `_timer = null`**（`TransitionInterpreter.cs:26-33`，注释在 `:30` 明写「WPF 的定时器在它所属的 dispatcher 上排队，不停表就会在动画结束后继续占着它」）。

### 2.3 一切以 `UserControl` 为界，不是就静默无效

宿主行为直接 cast（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:113`、`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSlotLayoutBehavior.cs:78`），或从事件源往上找最近的 UserControl（`Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:294`/`:386`/`:418`/`:494`）。根元素换成 `Border`/`Grid` ⇒ 全部 no-op，**不抛异常**。demo 里因此 Surface/NodeView/SlotView 全是 UserControl。

（这条在 WorkflowSystem 那半边更要命，见 `memory/modules/WorkflowSystem/adapters/wpf.md`。放在这里是因为同一个 cast 形状贯穿两个模块。）

### 2.4 别家该抄的、抄不动的，分界在哪

| 这家的做法 | 别家能不能抄 | 为什么 |
|---|---|---|
| `FindOrCreate` 三行式 `CreateScheduler` | **能** | 纯契约，与平台无关（`Interpolator.cs:30-33`） |
| pacer 用平台定时器 + `Dispose` 停表 | **能**，换 API | 形状对，具体定时器对象各家不同 |
| `HasShutdownStarted` 守卫 | **只有有 dispatcher 的家能** | 它问的是 dispatcher |
| 采样器「无状态单例 + scratch」 | **能**，且必须 | `Samplers/TransformSampler.cs`（262 行：端点短路、scratch 类型守卫、`CloneTransform` 穷举、`LerpAngle` 读 `RotationDirection`、类型不匹配时退矩阵插值）是这家最完整的范本 |
| 26 个手写 `Property` 重载 | **不要抄数量** | 只有 `Transform` 那一个不是冗余；泛型 `Property<TValue>` 已经覆盖其余全部 |

---

## 三、与其它六家的差异

> **先给一个反直觉的结论**：TransitionSystem 这边 WPF 是七家里的**平均样本** —— 它既不是最多采样器的（Avalonia 14 > 12），也不是唯一有存活守卫的，`TransitionScheduler`/`TransitionEffect.Priority` 都跟多数派一致。所以下面既列真正的背离，也列**「看起来像这家特有、其实不是」**的校准项，免得下一个 agent 把共性当差异去改。

1. **这里的 Ctrl 判定是精确相等，不是 `HasFlag`** —— 见 2.3 同源的 `Src/Adapters/VeloxDev.WPF/Attached/Workflow/WorkflowSurfaceBehavior.cs:300` 的 `Keyboard.Modifiers != ModifierKeys.Control`（WinForms 同形：`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/WorkflowSurfaceBehavior.cs:42` 的 `Control.ModifierKeys != Keys.Control`）；另外四家用 `HasFlag`：`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/WorkflowSurfaceBehavior.cs:274`、`Src/Adapters/VeloxDev.WinUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:313`、`Src/Adapters/VeloxDev.MAUI/Attached/Workflow/WorkflowSurfaceBehavior.cs:467`、`Src/Adapters/VeloxDev.Jalium/Attached/Workflow/WorkflowSurfaceBehavior.cs:322`。可观察后果：**Ctrl+Shift+滚轮在 WPF 与 WinForms 上不缩放，在另外四家上缩放**。代码与注释里**没有**记录这是刻意还是遗漏 —— 按现状保留或统一都行，但别以为七家一致。

2. **这家与 Avalonia 是七家里唯二会与 Core 撞名的** —— `System.Windows.Point/Size/Color`（Avalonia 则是 `Avalonia.Point/Size/Color`）与 Core 注册的 `System.Drawing` 三兄弟同名，只能靠第二命名空间分开（两家的采样器都放在 `VeloxDev.Adapters.NativeSamplers`）。WinUI/Jalium/MAUI 的平台类型与 `System.Drawing` 不同名，不需要这一层。理由见第一节。

3. **`TransitionScheduler` 写的是非泛型空壳（`TransitionScheduler.cs:5-9`）** —— 七家里五家如此（WPF/MAUI/WinForms/Razor/Jalium），只有 Avalonia 与 WinUI 用 `TransitionScheduler<TTarget>`。泛型那两家为什么要泛型，代码里没写理由；接新一家时以 WPF 这份为形（空壳）就够。

4. **`TransitionEffect.Priority` 给 `Render`（`TransitionEffect.cs:7`）** —— 与 Avalonia/Jalium 同值，而 WinUI 给 `DispatcherQueuePriority.High`。这是三家里的一致，不是 WPF 单独的背离。

---

## 四、坑（带依据）

1. **12 个采样器里每个的端点都必须返回调用方给的 `start`/`end` 本身，不能返回 scratch。** `Samplers/TransformSampler.cs:16-17` 的 `t == 0d` / `t == 1d` 短路是标准形态；scratch 复用前有一道 `wt.GetType() != startT.GetType()` 的类型守卫（`:26`）。这条容易在「顺手优化」时被删掉，删掉后嵌套路径（`((TranslateTransform)x.RenderTransform).X`）会拿到换过类型的对象而**不报错**。

2. **`Samplers/BrushSampler.cs` 的交叉淡化路径会把 `blend` 夹到 `[0,1]`**（`:102` 的 `t <= 0d ? 0d : (t >= 1d ? 1d : t)`）—— 因为它把两个端点各画一次到同一张复用的 `RenderTargetBitmap` 上再叠不透明度（`:93`、`:98`、`:106`、`:110`），交叉淡化表达不了 overshoot。⇒ 这家上给 `Brush` 配 overshoot 缓动**不会**过冲。这是采样器自己的限制，不是缓动的。

3. **`CornerRadius` 用原始 `t` 逐角插值，没有共享的 `BoundedProgress`**（`Samplers/CornerRadiusSampler.cs:15-19`）；`Size`/`Rect` 反过来对 W/H 用**同一个** `BoundedProgress(t, 0d, double.PositiveInfinity)`（`Samplers/SizeSampler.cs:17`、`Samplers/RectSampler.cs:17`），而位置不夹。⇒ 同一批采样器里「夹不夹」不一致，是按各自语义定的，**别照着某一个去统一另外几个**。

4. **`Interpolator` 的注册是静态构造，注册顺序不影响查找**（查找按精确 → 基类由近及远 → 接口按名字序，`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:50-87`）。WPF 注册的是 `typeof(Brush)`/`typeof(Transform)` 这种**基类型**（`Interpolator.cs:14`/`:18`），所以它**承诺处理整个家族** —— 梯度 Brush、`TransformGroup` 都会进来，`TransformSampler` 里那一大段 `CloneTransform` 的穷举 switch 就是为这个付出的代价。在这家加一个 `typeof(SolidColorBrush)` 之类的具体注册只会让查找更早命中，不会更晚。

5. **改采样器后必须补测试表。** WPF 的 12 个采样器**全部**能在 `Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs` 那个纯数据进程里构造出端点值 —— 一个都不在 `Examples/Transition/AUTO TEST/Samplers/UnreachableSamplers.cs`（那份只有 WinUI 3 条 + MAUI 3 条，理由写在该文件顶部：要真 XAML/MAUI 运行时）。⇒ **WPF 上没有 `Unreachable` 这个逃逸口**，漏了 `WpfEntries.cs` 一行就是 `SamplerCoverageTests.EveryShippedSampler_IsAccountedFor` 直接红。

6. **`WpfEntries.cs` 记录了一个真会咬人的坑：七家采样器共享同一个命名空间 `VeloxDev.Adapters.NativeSamplers` 且类名相同** ⇒ 同一个测试项目里引用两家就是 CS0433/CS0104。那份文件里的解法是显式 using 别名 + 程序集限定名反射（`VeloxDev.Adapters.NativeSamplers.{name}`）。**这条不是测试的怪癖，是真实的适配器形状**：新接一家沿用同名同namespace 时，任何同时引用两家的项目都要付这份成本。

7. **声明成 `Effect`（抽象基类）的属性曾经在注册表里一条键都没有 —— 2026-09-20 已改成注册基类型。** 查找只**向上**走（精确 → 基类 → 接口，`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:50-87`），所以注册具体类型 `DropShadowEffect` 时，`Effect` 的基类链（`Animatable` → `Freezable` → `DependencyObject` → `object`）上没有键 ⇒ `Prepare` 报一次 `Unsampled` 并把该属性**整个跳过**（`Interpolator.cs:175-179`），**不抛、不降级、不提示**。现在注册的是 `typeof(Effect)`（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Interpolator.cs:25`），**这条键的代价是必须服务整个家族**（Core 注册表那条通则的实例）：
   - **两处独立地照 `Effect` 声明**，所以这不是测试的臆造：真 demo 的 DP 是 `Register(nameof(Shadow), typeof(Effect), null)`、属性是 `public Effect? Shadow`（`Examples/Transition/WPF/Demo/SamplerSubject.cs:49,93`）；纯数据表的 `Target.Shadow` 同样是 `Effect`（`Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs:37`）。
   - **两处此前能跑，都是绕开了注册表**：demo 用 `SetInterpolator` 逐条覆盖（`Examples/Transition/WPF/Demo/MainWindow.xaml.cs:353`），纯数据表直接拿采样器实例。⇒ 改注之前，「`Effect` 属性能动画」这句话在仓库里**没有任何一条经注册表的证据**；现在有了。
   - **公开 API 暴露的是具体类型**：`Transition.cs:111` 的重载签名是 `Expression<Func<T, DropShadowEffect?>>`。这条重载仍是「编译期就知道是阴影」时的最短写法，但**它不是走通 `Effect` 声明的前提** —— 泛型 `Property<TValue>` 配 `typeof(Effect)` 那条键即可，demo 与纯数据表用的都是后者。
   - **兜底分支同时被改诚实了**：`Samplers/DropShadowEffectSampler.cs:44` 现在是 `property.SetValue(target, t >= 0.5d ? end : start)` —— 两端不是同一类具体效果时（例如 `BlurEffect`）**如实交出调用方给的实例**，不再凭空造一个 `DropShadowEffect` 顶替。这正是 `adapter.md:153` 那句「改注成基类型是义务的开始，不是结束」的落地：改注把 `BlurEffect` 引进来，兜底决定它被静默画成阴影还是被原样交出。
   - **这一条现在由两条测试钉着**：`SamplerKeyTests.cs:184` 的 `ASamplerRegisteredForABaseType_HandsBackTheFamilyItWasGiven` 用 `Assert.AreSame` 证明交出的不是替身对象；`WpfEntries.cs:219` 那条条目的声明类型回到键校验里（此前挂的是 `UnregisteredReason`「该键不该存在」的可证伪声明），由 `SamplerKeyTests` 的 `EveryEntry_ValueTypeResolvesToTheSamplerItNames` 核。**两条都做过变异验证**：注册退回 `typeof(DropShadowEffect)` 时两条同时红，兜底退回「造 `DropShadowEffect`」时 `AreSame` 那条红。

---

## 五、这份文件没写的东西

- 八个类的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（要实现的接口、注册在哪、`FindOrCreate` 的理由）—— 在 `memory/modules/TransitionSystem/extension.md`。
- 怎么写一个新采样器 / 怎么注册一个新类型 / 端点与 scratch 的规则 —— 在 `skills/veloxdev-create-animation/references/adapter.md`。
