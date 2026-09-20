# DynamicTheme — 架构

> 代码：`Src/Core/VeloxDev.Core/DynamicTheme/`（5 个 .cs），契约在 `Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/`（`ITheme.cs` / `IThemeObject.cs` / `IThemeValueConverter.cs`）。
> 生成器：`Src/Generators/VeloxDev.Core.Generator/Theme.cs`。**下文不带路径的 `Theme.cs` 指这一个**（注意：本模块目录下**没有** `Theme.cs`，别在 `DynamicTheme/` 里找；运行期那 5 个文件是 `Dark.cs` / `Light.cs` / `ThemeCache.cs` / `ThemeConfigAttribute.cs` / `ThemeManager.cs`）。
> 平台侧：各家的 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs`。
> 本文只写「读完这些文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 把「一个对象上的若干属性 × 每个主题各一套值」声明出来（生成器读 `[ThemeConfig<...>]` 写进一张静态表），并在切换主题时把每个属性的**当前值**动画到新主题的值。**动画本身一行都不在本模块** —— 全部委派给 TransitionSystem（见 §三）。

**它自己只做三件事**：① 建表（生成器 + `ThemeCache`）；② 起一场「无 `Transition<T>` 的」动画（`RunSwitch`）；③ 记账（`Current`、`activeThemes`、运行期覆盖）。

**不解决什么（这几条决定了「为什么我的属性没动」）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 主题是什么 | **空标记类型**。Core 只提供 `Dark.cs` / `Light.cs` 两个空类，`ITheme` 也是空接口（`Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/ITheme.cs`）。主题的身份就是 **`Type` 对象本身**，可以自定义 |
| 属性变更通知 | 不产生。`SetThemeValue` 走生成器的 `UpdatePropertyToCurrentTheme` → `propertyInfo.SetValue`（`Theme.cs:372`），**绕过 setter 的语义**（对 `[VeloxProperty]` 属性来说就是绕过生成的通知） |
| 字符串/画刷怎么变成值 | `IThemeValueConverter`，实现全在**适配器**（各家的 `PlatformAdapters/ThemeValueConverters.cs`）。Core 一个也不带 |
| 动画的采样、缓动、帧、时间轴 | TransitionSystem。本模块只用它的 `TransitionSchedulerCore.Track/Execute/Untrack`、`TransitionRun`、`StateCore`、`TransitionProperty` |
| 什么时候能动、什么时候不能（哪家平台画得出来） | 各家的 `Interpolator.CreateScheduler`。**这就是「主题切换为什么在这个平台不动」的答案所在** —— 判定在适配器，不在本模块 |
| 反播、暂停/Seek 的**额外**控制面 | 不新增。整场共用一条时间轴，于是既有的 `Transition.Pause/Seek/SetRate` 对整场生效（`ThemeManager.cs:234-235` 的注释） |
| 无采样器属性的动画 | **做不到**。降级为「切换结束后直接跳变」，见 §三·5 |
| 线程归属 | Core 不解决。`ThemeManager` 直接 `scheduler.Execute(...)`，编组由适配器的 scheduler 负责 |

**与 TransitionSystem 的关系要先说清**：本模块是 TransitionSystem 的**第二个入口**，不是它的子模块。`memory/modules/TransitionSystem/architecture.md` §三·入口 B 记的就是这里（`ThemeManager.cs:200`），`extension.md` 把 `ThemeManager.cs:248-261` 当作「手工驱动 scheduler」的范本。**契约在那两份文档里，本文不重复。**

---

## 二、数据面：两级缓存

整套值存两份，这是本模块最容易混的一处。

| | 静态表（默认值） | 活动表（运行期覆盖） |
|---|---|---|
| 类 | `ThemeCache._staticCache`（`ThemeCache.cs:14`），**按 `Type` 键** | `ThemeCache._activeCache`（`:18`），`ConditionalWeakTable<IThemeObject, InstanceCache>`，**按实例键** |
| 谁写 | 生成器的 `InitializeTheme()` → `ThemeCache.RegisterType` | 生成器的 `SetThemeValue<T>()` → `cache.Overrides[...]` |
| 谁删 | **没有删除路径**（`RegisterType` 幂等，`:58` 的 `ContainsKey` 守卫） | `RestoreThemeValue<T>()` → `Overrides.Remove(propertyName)`；`RemoveActiveEntry` 存在但**无人调用** |
| 谁读 | `GetStaticForType`（`:98`）/ `TryGetDefaultValue`（`:157`） | `GetOrCreateActiveEntry`（`:131`）/ `TryGetActiveEntry`（`:139`） |

**两级都用同一把 `_lock`（`ThemeCache.cs:15`）**，包括 `IsTypeRegistered`。所以「生成器里的懒注册」（`Theme.cs:391` 的 `if (!IsTypeRegistered(...))`）与并发注册不会互相破坏。

**键的形状是三层：`propertyName → PropertyInfo → themeType → value`。**
- 两层都有「属性」是因为**读按名字、写按 `PropertyInfo`**：`PrepareSamplers` 用 `propEntry.Key`（名字）打日志与查 active 表，用 `propertyInfo` 去读值（`ThemeManager.cs:467-475`）。
- 每个 `propertyName` 底下的 `Dictionary<PropertyInfo, ...>` **只会有一条**（`ThemeCache.cs:119-122` 建的就是单条），所以 `ThemeManager.cs:474` 的 `Keys.First()` 和生成器里的 `Enumerable.FirstOrDefault(kvp.Value.Keys)`（`Theme.cs:408`）都是「取那唯一一条」，不是真在挑。
- **`PropertyInfo` 是字典的键，所以同一个属性必须用等值的 `PropertyInfo` 才命中**。生成器两处都写 `typeof(X).GetProperty(nameof(P))`（`Theme.cs:241`、`:310`），依赖 BCL 的 `PropertyInfo` 等值语义（按元数据比较，非引用）。

**`GetStaticForType` 每次都新建一个合并字典**（`ThemeCache.cs:98-103`），不是返回缓存引用。合并规则是「递归基类在前、派生在后，且**整条名字覆盖**」（`:111` 与 `:119`）——所以派生类对**同一个属性名**的声明会**完全替换**基类那一条（含它的 `Values` 字典），不是合并两个主题集合。

**`Unregister` 不清静态表**（`ThemeManager.cs:89-93`）：它删 `_act_cache` 的实例项与 `activeThemes` 里的弱引用。静态表按 `Type` 键，本来就不该按实例删。

---

## 三、一次切换的完整流向

两个入口：`Transition(Type, effect)`（带动画，`:110`）与 `Jump(Type)`（瞬时，`:162`）。**两者开头完全一样** —— 校验、`CancelActiveSwitch()`、清死弱引用、对每个目标 `ExecuteThemeChanging(old, new)`（`:118-124` / `:170-176`）。

``` 
Transition(themeType, effect)          async void, 调用方接不住异常 (:110)
  ├ 校验: 同主题 或 不是 ITheme  → 只 Debug.WriteLine, 直接 return        :113-117
  ├ CancelActiveSwitch()                                                  :118
  ├ 清掉 _act_cache 里已死的目标                                          :119
  └ await RunSwitch(actives, themeType, effect)                           :129
       └ 返回 false(被取消/被顶替) → return, **不发 ExecuteThemeChanged** :138-141
       └ 返回 true             → 逐目标 ExecuteThemeChanged(old, new)     :143-146

RunSwitch (:200)
  1 groups = PrepareSamplers(actives, themeType)                          :202
  2 interpolator = _interpolator                                          :203
  3 if (interpolator is null || groups.Count == 0) → ApplyImmediately     :206-209   ← 退化, 不是错误
  4 逐个 CreateScheduler(group.Target, effect)                            :219
       任一返回 null 或抛 → **整场** ApplyImmediately                     :227-230
  5 timeline = TimerCore.CreateTimeSource<ITimeSourceControl>()           :236   ← 整场唯一一条
  6 逐组:
       WriteStartValues(group)                                            :246   ← 把起点写回目标
       run = new TransitionRun(timeline)                                  :248
       scheduler.Track(run)                                               :251   ← 必须先于 Execute
       记进 runs                                                          :252
  7 Interlocked.Exchange(ref _activeSwitch, runs)                         :255
  8 逐 run: scheduler.Execute(interpolator, state, effect, run.Cts)       :260
  9 await Task.WhenAll(tasks)   抛 → faulted = true, 吞掉                  :263-272
 10 finally: 逐 run Untrack + CompareExchange(_activeSwitch, null, runs)  :274-281
 11 stopped = faulted || 任一 run 被取消                                   :285
 12 逐 run Dispose()                                                       :286-289
 13 stopped → return false                                                 :291-294
 14 ApplyHeldValues(groups)                                                :297
 15 Current = themeType                                                    :298
 16 return true
```

**五条读代码才知道的理由（都写在注释里，也都能从调用序看出）：**

1. **所有 scheduler 先解析齐，再启动任何一个**（`:211-212` 的注释）：只让一部分目标动、另一部分瞬切，比整场都不动更糟；而答案只取决于平台与 effect，与目标无关，所以对整场一致。
2. **`WriteStartValues` 必须在 `Prepare` 之前**（`:244-245`）：`InterpolatorCore.Prepare` 只能从目标上**读**起点，等价于只实现了 `StartModel.Reflect`。要先按配置的模式把起点写回目标，默认的 `Cache` 档才成立 —— 否则起点是「目标上碰巧是什么」。
3. **`Track` 早于 `Execute`**（`:248-251` 的注释）：`Execute` 正是靠 `run.Cts` 在 scheduler 的活跃表里找回这个 run；找回失败，采样集合会拿一条**没人控制得住的私有时间轴**。这是 TransitionSystem 的不变量 A1，在 `memory/modules/TransitionSystem/architecture.md` §五。
4. **共用一条时间轴**（`:234-235` 的注释）：所有目标锚在同一个 `ITimeSourceControl` 上 ⇒ 对任一目标 `Pause`/`Seek`/`SetRate` 会同时移动全部目标。这就是「不新增控制面」能成立的全部原因。
5. **`stopped` 时提前返回，不推进 `Current`、不发 `ExecuteThemeChanged`**（`:285`、`:291-294`）。被取消的那一场**属性值停在半路**，`Current` 还是旧的主题 —— 「值已变一半、`Current` 说没变」是**有意的**，不是 bug：下一个切换的开始值会经由 `WriteStartValues` / `Reflect` 重新取得。

**无采样器的属性走的是「收尾跳变」而不是不动**：`PrepareSamplers` 里 `hasSampler` 只判一次（`ThemeManager.cs:562`，用 `InterpolatorCore.TryGetInterpolator(类型, out _)` —— **只看属性类型，不看值**）。没有采样器的属性在 `Prepare` 里被静默跳过（全程保持旧值），到 `ApplyHeldValues`（`:390-408`）才**直接写终值**。`ApplyHeldValues` 只在**落地**的那一场跑（`:297`）；被取消的那一场**不会**补写。

**`Jump` 与 `Transition` 的唯一差别**：`Jump` 不做动画，直接 `ApplyImmediately(PrepareSamplers(...), themeType)`（`:180`），并且**先 `CancelActiveSwitch()`**（`:170`）。所以 `Jump` 会打断正在跑的那一场。注释 `:178-180` 写明了它为什么能脱离平台：不带动画的切换就是「写终值」本身，不需要时间轴，也不需要平台的 effect —— 因此 **`Jump` 既不依赖 `SetPlatformInterpolator`，也不受平台 `ITransitionEffect<TPriority>` 类型约束**。

---

## 四、`Current` 的所有权

`Current`（`ThemeManager.cs:43`，默认 `typeof(Dark)`）是**唯一的全局主题状态**，`internal set`。它的写者**只有两处**：

| 位置 | 何时 |
|---|---|
| `ApplyImmediately` `:432` | 退化路径、`Jump`、`groups.Count == 0` |
| `RunSwitch` `:298` | 整场真的跑到终点之后 |

**两处都在「值已经全部落地之后」**。所以「属性值」与「`Current`」在**每次切换结束时**一致，但在一场**中途被取消**之后不一致（见 §三·5）。读 `Current` 的地方有三处：`PrepareSamplers` 的 `StartModel.Cache` 分支（取起点，`:510`、`:514`）、生成器的 `UpdatePropertyToCurrentTheme`（`Theme.cs:347`）、`InitializeTheme`（`Theme.cs:405`）。

**`InitializeTheme()` 是同步的、不走动画的**（`Theme.cs:388-414`）：懒注册 → `base.InitializeTheme()` → `ThemeManager.Register(this)` → 逐属性 `pi.SetValue(this, 当前主题的值)`。所以**新建一个对象时它会立刻带上当前主题的值**，与有没有平台 interpolator 无关 —— 必须**在构造之后**调（demo 的注释也这么写：`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:42`）。

---

## 五、不变量

1. **`Track` 必须早于 `Execute`**（`ThemeManager.cs:251` 前于 `:260`）。违反的后果见 TransitionSystem；本模块专门为它写了注释。
2. **整场一条时间轴**（`:236`）。绝不为每个目标各建一条 —— 那会让「Pause 一次」只停一个目标。
3. **`Track` ↔ `Untrack` 成对，且都在 `finally`**（`:278`）。`Interlocked.Exchange` ↔ `CompareExchange` 同理（`:255` / `:280`）—— 用 `CompareExchange` 而不是直接置 null，是为了不把**后来那一场**装进去的值覆盖掉。
4. **`EndValue is null` 表示「这个主题不管这个属性」**，三个地方都要跳过它：`BuildState`（`:370`）、`ApplyHeldValues`（`:396`）、`ApplyImmediately`（`:419`）。注释 `:368-369` 说得很直白：把 null 交给采样器就是「整场被丢在地上」。
5. **`StartValue` / `EndValue` 都是「原值」，不归一化**：归一化交给 `Prepare`，在这里做会**归一化两次**（`TransitionEntry` 的注释 `:611`、`:614`）。
6. **`StateCore` 里只放终点**（`BuildState` `:358-361`）：起点由 `Prepare` 从目标上读回。
7. **`PropertyInfo` 是键，所以要复用 `TransitionProperty.FromProperty(propertyInfo)`**（`:568`）：注释 `:374-375` 说复用 entry 自己那条路径而不是让 `StateCore` 从 `PropertyInfo` 再造一条，两者等值，但同一个实例（同一个键对象）更好，还省一次路径构造。
8. **注册表只会单向增长**：`RegisterType` 幂等（`ThemeCache.cs:58`）、`activeThemes` 只靠 `Unregister` 或入口处的死引用清理收缩（`ThemeManager.cs:119`、`:171`）。
9. **`RunSwitch` 要么整场动、要么整场瞬切**（`:227-230`）：没有「部分目标动」这一档。
10. **异常不逃逸**：`Transition` 是 `async void`，所以 `RunSwitch` 的异常在 `:131-136` 被吞成 `Debug.WriteLine`；各 stage 自己的小异常也逐处 `try/catch + Debug.WriteLine`（`:349`、`:376`、`:400`、`:423`、`:496`、`:562`…）。**看不到异常**是本模块的既定姿态，不是遗漏。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 一次切换怎么推进（计划、时间轴、Track/Execute、收尾） | `ThemeManager.cs` 的 `RunSwitch`（`:200-300`） |
| 起点怎么取（`StartModel` 的两档语义） | 同上 `PrepareSamplers` 的 `switch`（`:490-519`） |
| 「哪些属性参与一场切换」 | 同上 `PrepareSamplers`（`:436-586`）—— 由目标的 `GetStaticThemeCache()` 决定 |
| 声明语法的形状（arity 上限、converter 在哪） | `Src/Core/VeloxDev.Core/DynamicTheme/ThemeConfigAttribute.cs`（6 个泛型类，arity 3..8） |
| 生成出来的 `IThemeObject` 实现长什么样 | `Src/Generators/VeloxDev.Core.Generator/Theme.cs`（`InitializeTheme` `:388`、`SetThemeValue` `:302`、`UpdatePropertyToCurrentTheme` `:345`） |
| 字符串/画刷怎么解析成值 | 各家的 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` |
| **「主题切换为什么在这个平台不animate」** | 该家的 `Interpolator.CreateScheduler`。契约与各家的差异见 `memory/modules/TransitionSystem/architecture.md` §六末行与 `memory/modules/TransitionSystem/adapters/<平台>.md` |
| 「谁调了 `SetPlatformInterpolator`」 | 只调在 Examples 里，点见 `memory/modules/TransitionSystem/adapters/winforms.md`（Brush 采样器缺失与 `SetPlatformInterpolator` 调用点） |
| 缓存的两级与键形状 | `ThemeCache.cs`（静态 `:54`、活动 `:131`、合并 `:105-126`） |

---

## 七、陷阱（带依据）

1. **`StartModel.Reflect` 时活动表完全不参与起点**：`Reflect` 分支只有 `propertyInfo.GetValue(target)`（`ThemeManager.cs:496`），不查 active cache。所以「我用 `SetThemeValue` 覆盖过这个属性」在 `Reflect` 模式下**对起点无效**，只在 `Cache` 模式下被优先读取（`:508-517`）。
2. **`ThemeCache` 里有三处死代码**（声明存在、**全仓库零调用者**，已核对）：`RegisterConverter`（`ThemeCache.cs:73`）、`GetConverter`（`:86`）与它们背后的 `_converters` / `_converterIndex`（`:21-22`）、`RemoveActiveEntry`（`:148`）。生成器**不用**这条转换器缓存 —— 它在生成代码里**内联** `new` 一个转换器：`((IThemeValueConverter)Activator.CreateInstance(typeof(...))!).Convert(...)`（`Theme.cs:236`）。**所以每个属性、每次 `InitializeTheme` 都会新建一个转换器实例**（在懒注册守卫之内，因此每类型一次），不是单例。
3. **`ThemeManager._def_cache`（`:26`）是死字段**：声明后再无任何读写，也无任何注释解释它原本要做什么。
4. **`Theme.cs:191` 的 `converterKey` 算了不用**：局部变量赋值后从未被引用，`:190` 的注释自己承认「only placeholder—converter created inline」。
5. **7 个主题声明得出来、用不了。** Core 提供了 arity 8 的 `ThemeConfigAttribute<TConverter, TTheme1..TTheme7>`（`ThemeConfigAttribute.cs:97`），但生成器只注册了 arity `` `3 ``–`` `7 `` 五个 `ForAttributeWithMetadataName` 提供器（`Theme.cs:32-55`）。⇒ **arity 8 的标注不会让那个类进入生成管线**（`ForAttributeWithMetadataName` 不命中，连 `Transform` 都不跑），该类的 `IThemeObject` 实现**根本不会生成**。这与 `Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:34-35` 的注释「supports at most one Converter plus seven Themes」**不一致**：**以代码为准，实际上限是 1 个 converter + 6 个主题**。
6. **`RegisterType` 幂等 ⇒ 改静态表需要重启或改类型。** `InitializeTheme` 的懒注册以类型为守卫（`Theme.cs:391`），一旦注册过，**同一类型再调 `InitializeTheme` 不会重写静态表**。热重载场景下静态表不会刷新。
7. **`CollectStaticForType` 的「整条名字覆盖」不是合并**（`ThemeCache.cs:119-122`）：派生类声明了与基类**同名**属性时，基类那条的 `Values` 字典被整个丢换。想在派生类**追加**一个主题的同一属性做不到 —— 只能在派生类把该属性的全部主题重写一遍。
8. **`activeThemes` 的清理只发生在三个入口**（`Transition` `:119`、`Jump` `:171`、`Unregister` `:92`）。一个目标被 GC 之后，它的弱引用会一直躺在 `activeThemes` 里，直到下一次切换来临。`RunSwitch` 内部**不再**过滤 null（`actives` 在入口就过滤好了）—— 所以 `PrepareSamplers` 里的 `target == null` 分支（`:444-448`）在正常路径上到不了。
9. **`Validate` 失败时**只 `Debug.WriteLine` 并打一句 "jumping to current theme"（`:115`、`:167`），**什么都不做** —— 既不是 Jump、也不抛异常。传一个 `int` 当主题类型就是静默无操作。
10. **`Transition` 的 `landed == false` 也不发 `ExecuteThemeChanged`，但 `ExecuteThemeChanging` 已经发过了**（`:121-124` 先发，`:143-146` 后发）。所以成对回调在取消场景下**只有前半**。写 `OnThemeChanging/OnThemeChanged` 时要按「可能只有 changing」处理。
