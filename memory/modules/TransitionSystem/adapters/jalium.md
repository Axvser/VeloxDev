# TransitionSystem — Jalium

> **读法**：契约与注册位置在 `memory/modules/TransitionSystem/extension.md`，本文不重复；
> 人面向的「怎么写一个适配器」在 `skills/veloxdev-create-animation/references/adapter.md`，本文只指路不抄。
> 本文只写这家的**硬限制、刻意背离、该家特有的坑**。代码在 `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/`。
> **路径写法**：下文的裸文件名（`Interpolator.cs:23`、`Samplers/RectSampler.cs:18` 等）都相对 `Src/Adapters/VeloxDev.Jalium/PlatformAdapters/`；引用 Core 或别家时一律写全路径。

---

## 一、这家要写什么，为什么是这些

八个类一个不少（表见 extension.md §三·C）。这家唯一"有内容"的选择是**第七型参填 `DispatcherPriority`**（`TransitionScheduler.cs:5`），与 WPF/Avalonia 同族。这个选择牵动的四处与 WPF 逐条同形：`CreateScheduler` 里测 `effect is ITransitionEffect<DispatcherPriority>`（`Interpolator.cs:26-29`；**这一处属 DynamicTheme 轴，在本家是休眠的** —— 它唯一的调用者是 `Src/Core/VeloxDev.Core/DynamicTheme/ThemeManager.cs:219`，而全仓没有任何 Jalium 调用 `SetPlatformInterpolator`、`Examples/Theme/` 下也没有 Jalium）、宿主覆写 `InternalPriority`（`UIThreadInspector.cs:31` 给 `Send`）、pacer 有真优先级可用（`TransitionInterpreter.cs:9-12` 建 `DispatcherFramePacer`，`:37` 建带优先级的 `DispatcherTimer`）、`TransitionEffect.Priority` 给 `Render`（`TransitionEffect.cs:7`）。

**这家的过渡层基本上是 WPF 的逐字副本**：`TransitionInterpreter.cs`（含 `DispatcherFramePacer` 的 `Arm`/`Disarm`/`Dispose`）与 `Src/Adapters/VeloxDev.WPF/PlatformAdapters/TransitionInterpreter.cs` 除注释外逐字相同；`State.cs`、`TransitionScheduler.cs`、`TransitionEffects.cs`、`TransitionEffect.cs` 只换了 `using`。**⇒ 差异全部集中在采样器面与 `Property` 重载面；要给别家找跨平台范本，这两家等价，要理解 Jalium 只需要看那两处。**

### 1.1 采样器面：9 个类 / 10 条注册

`Interpolator.cs:14-23` 注册十条，但 `Samplers/` 下只有九个文件 —— **这是七家里唯一"类数 ≠ 注册条数"的一家**：`typeof(Brush)` 与 `typeof(SolidColorBrush)` 共用同一个 `BrushSampler` 实例。

相对 WPF 的采样器面：

| | Jalium | WPF |
|---|---|---|
| 独有 | `Transform3D`（`:23`）+ `Samplers/Transform3DSampler.cs` | `Vector`、`Effect`（效果家族，**键是抽象基类** `Effect`）、`Point3D`、`Vector3D` |
| 共有 | `Point`/`Rect`/`Thickness`/`CornerRadius`/`Size`/`Color`/`Brush`/`Transform` | 同 |

- **三维面换了一个类型**：Jalium 用**一个聚合类型** `Jalium.UI.Media.Media3D.Transform3D`（反射 `Jalium.UI.Managed` 可证其存在）替掉 WPF 的 `Point3D` + `Vector3D` 两个结构 ⇒ 两家的三维动画**声明面不同形**（`Transition.cs:68-73` 是 Jalium 的 `Transform3D` 重载，WPF 没有）。`Transform3DSampler.cs` 的 `RotateTransform3D` 轴守卫 + `Matrix3D` 回退就是为这个聚合类型付的代价。
- **`Vector` 与 `Vector3D` 的类型存在、但没有采样器**：`Jalium.UI.Vector` 与 `Jalium.UI.Media.Media3D.Vector3D` 都反射得到（后者在 `Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs:20` 里还被 `using` 别名引用，说明可编译）。⇒ **Jalium 上 `Vector` / `Vector3D` 属性不可动画，这是缺口不是平台限制**，代码与注释里都没写理由。要补就照 WPF 的 `Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/VectorSampler.cs`：加注册 +（要声明就）加重载。

### 1.2 `Property` 重载面：1 个泛型 + 12 个手写

`Transition.cs:34-131`：泛型 `Property<TValue>` 一个，手写十二个（`Brush`、`Transform` 的 `ICollection`、`Transform3D`、`Point`、`Rect`、`Thickness`、`CornerRadius`、`Size`、`Color`、`int`、`double`、`float`）。WPF 同位置有 27 个（多出 14 个 `System.Drawing.*` / `System.Numerics.*` / `decimal`）。

**这不是能力缺失**：泛型 `Property<TValue>`（`Transition.cs:34`）覆盖那些类型；手写只为"泛型表达不了的形状"服务。Jalium 手写的十二个里只有 `Transform` 的 `ICollection<Transform>` 那一个（`Transition.cs:48-66`）有泛型表达不了的语义 —— 单个 transform 必须**直接赋值**、不能包进 `TransformGroup`，否则嵌套路径 `((TranslateTransform)x.RenderTransform).X` 的运行时类型会被换掉（同条理由的完整版见 WPF 那份 `memory/modules/TransitionSystem/adapters/wpf.md`）。另外注意 `decimal` 在 Core 侧也没有采样器（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:12-28` 的基元只有 `double`/`float`/`int`/`long`）⇒ 少那十四个重载并没有额外损失什么。

---

## 二、平台硬限制，与由此产生的做法

> 这一节是全文最有价值的部分：它决定别家能照抄什么、不能照抄什么。

### 2.1 `Rect` / `Size` 的构造器在负尺寸上抛 `ArgumentException`（IL 级已验）

`Jalium.UI.Rect(double x, double y, double width, double height)` 的 IL 在 `width < 0` **或** `height < 0` 时 `newobj ArgumentException` + `throw`；`Jalium.UI.Size(double width, double height)` 同形。**负的 `x`/`y` 不抛** —— 同一份 IL 只检查第三个与第四个参数。

由此产生的做法（`Samplers/RectSampler.cs:15-25`、`Samplers/SizeSampler.cs:17-25`）：

- 宽高共享**同一个** `BoundedProgress(t, 0d, double.PositiveInfinity)` —— 在 0 处停住（负尺寸不可表示），上界取 `+∞`（不拦过冲）；宽高共用则过冲不会把形状扭斜。
- **位置仍按原始 `t` 外推**（`new Rect(r1.X + (r2.X - r1.X) * t, ...)`），因为 `x`/`y` 可为负，不需要夹。
- 缓动时间可以越界，越界时如果不夹，整帧会抛 —— 这是"过冲条"里唯一会抛异常的一格。

**同族里只有这两个有这条限制**（同一轮 IL 检查）：`CornerRadius`、`Thickness`、`Point`、`Color` 的构造器**都不抛** ⇒ 它们的采样器逐分量按原始 `t` 线性外推，**没有** `BoundedProgress`（`Samplers/CornerRadiusSampler.cs`、`ThicknessSampler.cs`、`PointSampler.cs`）。**别照着 `RectSampler` 去"统一"它们** —— 那是把一条平台限制抄成了风格。

独立佐证（不是同一份判断的第二遍）：`Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs:205-206` 按采样器登记 `SamplerRule` —— Rect/Size/Brush/Color 是 `Saturate`，CornerRadius/Point 是 `Extrapolate`，与该文件的注释一致。

### 2.2 `Color` 的三通道必须在 0..255 停住，Alpha 自成一界

`Samplers/ColorSampler.cs:15-34`：R/G/B 共享一个 `BoundedProgress(t, 0, 255)`（过冲不偏色），再经一个饱和的 `Channel()` 落到 `byte`；不透明度是另一条 0..1 的独立通道，在两端饱和。构造器本身不抛（IL 已验），夹的是 `byte` 的表示范围。

### 2.3 画刷的"真正交叉淡出"在这家做不到 —— 是渲染层的限制，不是采样器的

`Samplers/BrushSampler.cs:77-83` 的注释记着实测：两条"真正交叉淡出"的路都不成立 ——

- 渲染进 `RenderTargetBitmap` 再包成 `ImageBrush`：那条路的绘制上下文是个桩，**只认 `SolidColorBrush`**（渐变什么都不画，`PushOpacity` 与 `DrawImage` 都是占位实现，位图全空）；
- 画成 `DrawingBrush` 交给屏幕渲染器合成：**整块不画**（实测中间帧那一格 **0/9800 像素**有内容）。

⇒ 两端不可比（非实心↔非实心）时，结果是"混成一个代表性纯色"。**这是唯一一条能画出东西的路，别删、也别当性能问题优化。** 注意这两条失败实现**已经不在代码里**（只剩注释），所以成因无法从代码复核，只能采信注释与那两个数字；要重试就先按同样的方式量同样的格子。

### 2.4 有 dispatcher ⇒ 存活问它；但这家多一级兜底，且不接线 `Lifetime`

- `UIThreadInspector.cs:12-19` 的 `ThreadFor` 是**四级链**：`target is DispatcherObject` → `Application.Current?.Dispatcher` → `Dispatcher.FromThread(Thread.CurrentThread)` → **`Dispatcher.MainDispatcher`**。最后一级是 WPF 没有的（WPF 三级，`Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs:14-17`）。
- 整段包在 `try` 里，异常兜底 `ThreadRef.None`（`UIThreadInspector.cs:21-25`）；按注释，`None` 只让这个目标失去 pacer（退回 `ArmNextFrame`），不抛。
- `PostCore` 先看 `dispatcher.HasShutdownStarted` 再入队（`UIThreadInspector.cs:33-40`）。⇒ **这家不接线 `Lifetime`、不覆写 `IsAlive`**，所以 `CanSetValue()` 恒为 true，真正的存活判据是 `HasShutdownStarted`。与 WPF 同一份守卫；对照：**Avalonia 有 dispatcher 却没有这道守卫**（`Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/UIThreadInspector.cs:16-21` 搜不到 `HasShutdownStarted`）。
- 入队用的是 `dispatcher.BeginInvoke(priority, action)`（`UIThreadInspector.cs:38`），而 WPF/Avalonia 用 `InvokeAsync(action, priority)`。**这不是背离**：Jalium 的 `Dispatcher` 两套都有（反射 `Jalium.UI.Managed`：`BeginInvoke(DispatcherPriority, Action)` 与 `InvokeAsync(Action, DispatcherPriority)` 各 7 个重载），语义相同，只是命名。

### 2.5 csproj：独一家 `net10.0` 无平台后缀 + 独一家抑制 `8605;8604`

- `VeloxDev.Jalium.csproj:7` 是**单目标 `net10.0`，没有 `-windows` / `-android` 后缀**。七家里 WPF/WinForms 多目标 `netframework4.6.1;net5.0-windows;netcoreapp3.0`，Avalonia `netstandard2.0;net6.0`，MAUI `net10.0;net10.0-windows10.0.19041.0`，WinUI 两个 `-windows10.0.19041.0`，Razor 单目标 `net6.0`。⇒ **这不只是打包口味**：它决定了引用必须是"最低的、跨平台的"那个包 —— `Jalium.UI.Controls` 而不是 `Jalium.UI.Desktop`（理由写在 `VeloxDev.Jalium.csproj:4-6` 与 `:29-32` 的注释里），因此这家能同时服务 Windows / Linux / Android。
- `VeloxDev.Jalium.csproj:12` 的 `NoWarn` 含 `8605;8604`，**七家只有这一家**（WPF 是 `1573`/`1591` 两条分开写，MAUI 只有 `CA1416`）。成因实测：去掉后重编，**CS8605**（拆箱可能为 null）来自约二十个附着属性的 CLR 包装（形如 `public static bool GetIsEnabled(DependencyObject e) => (bool)e.GetValue(IsEnabledProperty);`，`Attached/Workflow/WorkflowSurfaceBehavior.cs:79-97` 一整片），**CS8604**（可能为 null 的实参）来自递归下降的 `FindDescendantWithSlotDataContext`（`Attached/Workflow/WorkflowSlotLayoutBehavior.cs:417-430`）。
  ⇒ 这是 **C# 侧的形状**（DP 包装约定 + 递归下降），不是 Jalium API 的问题 —— 同一份抑制放到别家也成立。改代码时别因为"别家不抑制"就删这两条，除非顺手把包装改成 `is bool b ? b : false` 那种形状。

---

## 三、与其它六家的差异

> **先给一个校准**：这一层的差异比看起来少。`TransitionScheduler` 是**非泛型空壳**（`TransitionScheduler.cs:5-9`，与 WPF/MAUI/WinForms/Razor 同，只有 Avalonia/WinUI 用 `TransitionScheduler<TTarget>`）；`TransitionEffect.Priority` 给 `DispatcherPriority.Render`（`TransitionEffect.cs:7`，与 WPF/Avalonia 同，WinUI 给 `DispatcherQueuePriority.High`）；`TransitionEffects` 三条时长与六家逐字相同（`TransitionEffects.cs`）。下面只列真正的背离。

1. **这里的注册表是七家里唯一的"同族双注册"，而且它的注释是错的。** `typeof(Brush)` 与 `typeof(SolidColorBrush)` 都指向 `BrushSampler`（`Interpolator.cs:20-21`）。`Interpolator.cs:12-13` 的注释说这是**必须**的（"Exact-type lookup: register BOTH Brush and SolidColorBrush so brush properties declared as either type animate"）—— **这句与 Core 的查找规则不符**：查找是精确 → 基类由近及远 → 接口按名字序（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:50-87`），而 `SolidColorBrush : Brush`（反射可证：`SolidColorBrush → Brush → Animatable → …`），所以**只留 `typeof(Brush)` 那一行同样命中 `SolidColorBrush`**；第二行是冗余（但无害：两个 key 指向同一实例）。⇒ **别把这段注释当契约抄到别家，也别据此认为"Jalium 的查找和别家不同"。**
2. **唯独这家把 3-D 面注册成一个聚合类型**：`Transform3D`（`Interpolator.cs:23`）+ `Samplers/Transform3DSampler.cs`，而 WPF 用 `Point3D` + `Vector3D` 两个结构、两个采样器（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/Point3DSampler.cs`、`Vector3DSampler.cs`）。⇒ 三维动画的**声明面在两家不同形**，没有可抄的关系；`Transform3DSampler` 里的轴守卫就是聚合类型的代价。
3. **这里的 `Property` 手写重载只有 12 个，而别家把 `System.Drawing` / `System.Numerics` 全铺了一遍。** 这不是"少写了"：泛型 `Property<TValue>` 覆盖它们（见 §1.2）。**唯一的实际差异是声明风格** —— 在 Jalium 上写 `Property(Expression<Func<T, System.Drawing.Point>>, ...)` 要走泛型，签名里显式写类型即可，没有别的后果。
4. **没有第四条。** 另两处容易误判成背离的地方其实是共性，列在这里免去下一次比对：① 存活判据用 `HasShutdownStarted` 而不是 `Lifetime`/`IsAlive`，WPF 同款（`Src/Adapters/VeloxDev.WPF/PlatformAdapters/UIThreadInspector.cs` 的 `PostCore`），而 Avalonia 有 dispatcher 却没这道守卫；② `PostCore` 用 `BeginInvoke`、WPF/Avalonia 用 `InvokeAsync`，Jalium 两套 API 都在、语义相同（反射 `Jalium.UI.Managed`），只是命名。

---

## 四、坑（带依据）

1. **端点短路与 scratch 类型守卫不能删。** `Samplers/TransformSampler.cs:22-23` 的 `t == 0d` / `t == 1d` 直接返回调用方给的 `start`/`end` 本身，`:33` 复用 scratch 前有一道 `wt.GetType() != startT.GetType()`。删掉后嵌套路径（`((TranslateTransform)x.RenderTransform).X`）会拿到换过类型的对象，**不报错**。
2. **`RectSampler` / `SizeSampler` 的 `BoundedProgress(t, 0, +∞)` 是 2.1 的平台补偿，不是审美。** 换成 `Clamp01` 之类会同时改掉两件事：上界从 `+∞` 变成 1（过冲被夹，但那两个采样器本来就该允许过冲）与位置的外推方式。
3. **`BrushSampler` 的第三条路（`Samplers/BrushSampler.cs:84-88`，复用 scratch `SolidColorBrush` 写 `Color`/`Opacity`）是兜底，不要"优化"回真交叉淡出** —— 见 2.3 的实测数字。
4. **`ColorSampler` 的 `BoundedProgress(t, 0, 255)`（`Samplers/ColorSampler.cs:16`）与饱和 `Channel()`（`:29-33`）成对出现**，删一个另一个就不够。画刷那条同款在 `Samplers/BrushSampler.cs:96`。
5. **测试表没有逃逸口。** Jalium 的 9 个采样器**全部**能在纯数据进程里造出端点（`Examples/Transition/AUTO TEST/Samplers/JaliumEntries.cs`），**一条都不在** `Examples/Transition/AUTO TEST/Samplers/UnreachableSamplers.cs`（那份只有 WinUI 3 条 + MAUI 3 条）。⇒ 漏一行 `JaliumEntries.cs` 就是 `SamplerCoverageTests.EveryShippedSampler_IsAccountedFor` 直接红，没有"这个造不出来"可退。
6. **`JaliumEntries.cs:5-22` 记着一条形状上的坑，写别家适配器也会撞上**：七个适配器把采样器放在同一个命名空间 `VeloxDev.Adapters.NativeSamplers` 且类名相同，而桌面 SDK 的隐式 `using`（`System.Drawing` / `System.Windows.Media` / `System.Windows.Media.Media3D`）与 Jalium 的类型在 `Point`/`Rect`/`Size`/`Thickness`/`CornerRadius`/`Color`/`Brush`/`Transform` 上大量同名 ⇒ 那份文件用一长串 `using X = Jalium.UI...` 别名把类型钉死。**任何同时引用 Jalium 与别家的项目都要付这份成本**（WPF 侧同形，见 `memory/modules/TransitionSystem/adapters/wpf.md` 的坑 6）。
7. **`Transform3DSampler` 的 `RotateTransform3D` 分支有轴守卫 + `Matrix3D` 回退**（`Samplers/Transform3DSampler.cs`）。聚合类型的轴/角度形状和 WPF 的结构不同，改三维插值时别照 WPF 的 `LerpAngle` 形状直接搬。

---

## 五、这份文件没写的东西

- 八个类的成员列表、继承树、文件清单 —— IDE 里一按就有。
- 契约本身（要实现的接口、注册在哪、`FindOrCreate` 的理由、pacer 的可重复定时器要求）—— 在 `memory/modules/TransitionSystem/extension.md`。
- 怎么写一个新采样器 / 怎么注册一个新类型 / 端点与 scratch 的规则 / 一条新缓动的三条联动 —— 同上 §三 与 `skills/veloxdev-create-animation/references/adapter.md`。
