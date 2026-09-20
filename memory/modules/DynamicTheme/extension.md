# DynamicTheme — 扩展

> 面向「我要加一个主题 / 一个转换器 / 一个平台」和「我要让这个属性跟着主题动」。架构见 [architecture.md](architecture.md)。
> 依据只写树里的 `文件:行`。
> **下文不带路径的 `Theme.cs` 指 `Src/Generators/VeloxDev.Core.Generator/Theme.cs`**（生成的 `Theme` / `InitializeTheme`），不在本模块目录下。

---

## 一、扩展点在哪

| 扩展点 | 具体成员 / 位置 | 谁实现 |
|---|---|---|
| 声明「哪个属性、哪些主题、什么值」 | `[ThemeConfig<TConverter, TTheme1..Tn>(属性名, [主题1的构造参数], [主题2的...], ...)]`（`ThemeConfigAttribute.cs`，arity 3..8） | 用户 |
| 主题是什么 | 实现空标记 `ITheme`（`Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/ITheme.cs`）。Core 只带 `Dark.cs` / `Light.cs` | 用户 / Core |
| 字符串怎么变成值 | `IThemeValueConverter.Convert(Type targetType, string propertyName, object?[] parameters)`（`Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/IThemeValueConverter.cs:5`） | **适配器**（6 家，见 §二·2） |
| 主题对象的运行期面 | `IThemeObject`（`Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/IThemeObject.cs`）—— 生成器**全部实现**，手写时才需要自己写 | 生成器 |
| 「这个平台画得出来吗」 | 该家 `Interpolator.CreateScheduler`（返回 `null` = 画不出来） | 适配器 |
| 让平台接缝生效 | `ThemeManager.SetPlatformInterpolator<T>(T)`（`ThemeManager.cs:61`），**每进程一次** | 用户 |
| 起点的取法 | `ThemeManager.StartModel`（`ThemeManager.cs:48`，默认 `Cache`） | 用户 |
| 运行期覆盖单个属性 | 生成的 `SetThemeValue<T>(propertyName, newValue)` / `RestoreThemeValue<T>(propertyName)`（`Theme.cs:302`、`:322`） | 用户 |
| 主题切换的成对回调 | `partial void OnThemeChanging(Type?, Type?)` / `OnThemeChanged(Type?, Type?)`（`Theme.cs:297-298`） | 用户实现 |
| 「现在该显示什么」 | 生成的 `UpdatePropertyToCurrentTheme(string)` / `UpdateAllPropertiesToCurrentTheme()`（`Theme.cs:345`、`:377`） | 用户调用 |
| 接入主题系统 | `InitializeTheme()` —— 生成的 | 用户**必须**调 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. 忘记调 `InitializeTheme()` ⇒ 对象对切换完全免疫

`ThemeManager.Register` 在 `Src/` 里**唯一的调用者是生成的 `InitializeTheme()`**（`Theme.cs:403`；另一个调用点是测试 `Src/Core/VeloxDev.Core.Test/DynamicTheme/ThemeTransitionTests.cs:118`，那里的 `Subject` 是手写的 `IThemeObject`）。`ThemeManager.RegisterType` 同样只在生成的 `InitializeTheme` 里（`Theme.cs:393`）。

**错的捷径**：写了 `[ThemeConfig<...>]`、编译通过、属性也有值（`InitializeTheme` 里那句 `pi.SetValue` 会立即写），但切换主题时**一点反应都没有** —— 因为 `activeThemes` 里从来没有它。

**官方做法**：在构造完成后调一次（demo 的注释明说必须在 `InitializeComponent()` 之后：`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:42`）。

### 2. 用动画切换却不调 `SetPlatformInterpolator`

`RunSwitch` 的第二个分支就是 `interpolator is null → ApplyImmediately`（`ThemeManager.cs:206-209`）。所以**不调它不会报错**，只是「有动画的切换」静默退化成瞬切。

调用点只有 4 个 Examples + 测试：`Examples/Theme/WPF/Demo/App.xaml.cs:16`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:47`、`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`、`Examples/Theme/Avalonia Trimmed/Demo/Views/MainWindow.axaml.cs:46`。**`Src/` 与 `Src/Adapters/` 里一个都没有** —— 这是纯用户责任。

**官方做法**：`ThemeManager.SetPlatformInterpolator(new Interpolator());`（每家适配器各有一个 `Interpolator` 类型）。它同时也是「平台的 sampler 注册」被触发的时机。

### 3. 让「这个平台画不出这个属性」变成「整个平台画不出主题」

`CreateScheduler` 只返回 `null` 一种「不行」的答案，而 `RunSwitch` 的处理是**整场退化**（`ThemeManager.cs:227-230`），不是「跳过这个目标」：

> 只让一部分目标动起来、另一部分瞬切，比整场都不动更糟（`:211-212` 的注释）

**错的捷径**：为了「某家平台没有 Brush 采样器」而在那个平台让 `CreateScheduler` 返回 null —— 结果是**那次切换里所有属性、所有目标全部瞬切**，包括本来能动的 `Double`。要表达「画不出来」的正确定位是**采样器注册**（缺少某个类型的 sampler ⇒ 那个属性被 `Prepare` 静默跳过、由 `ApplyHeldValues` 收尾跳变），不是 scheduler 接缝。各家的 sampler 覆盖差异见 `memory/modules/TransitionSystem/adapters/<平台>.md`（例如 `winforms.md` 的 Brush 采样器缺失）。

### 4. 手写 `IThemeObject` 而不是用生成器

可以（测试就是这么做的：`ThemeTransitionTests.cs:48-92`），但必须自己补三件生成器代做的事：`ThemeCache.RegisterType(...)`、`ThemeManager.Register(this)`、以及 `InitializeTheme` 里那次「按 `Current` 写初值」。少一件就是静默失效。

**其余情况一律用生成器**：`[ThemeConfig]` 标在**非 partial 类**上时 `GenerateThemeConfigClass` **直接返回空串**（`Theme.cs:115-118`），零产物、零诊断。`[ThemeConfig]` 标在**属性名写错**时逐条 `continue`（`:185-186`），全部写错则 `configRegistrations.Count == 0` 再返回空串（`:261-264`）—— 同样是零产物、零诊断。

---

## 三、新增一个主题对象的步骤清单

1. 类上标 `[ThemeConfig<TConverter, 主题1, 主题2, ...>(nameof(属性), [主题1的构造实参], [主题2的构造实参], ...)]`，**一个属性一行**，可叠加多行。最少 1 个转换器 + 2 个主题（arity 3），最多 **1 个转换器 + 6 个主题**（见 §四·4）。
2. 类必须是 **`partial`**（`Theme.cs:115-118`）。
3. 属性必须是**可读写的实例属性**，且生成器要能在 `classSymbol` 或它的基类链上找到它（`:172-186`）—— 属性可以是继承来的。
4. 调 **`InitializeTheme()`**（构造之后）。
5. 要用动画：调 `SetPlatformInterpolator(...)`，并按需设 `StartModel`（默认 `Cache`）。
6. 切换：`ThemeManager.Transition<Light>(effect)` 或 `ThemeManager.Jump<Light>()`。`effect` 必须是**该平台自己的** `ITransitionEffect<TPriority>` 实例，且切换期间不要改它（`ThemeManager.cs:104-108` 的注释；共享实例的坑见 `memory/modules/TransitionSystem/architecture.md` §七·2）。
7. 需要「切换中/切换后」做事：实现 `partial void OnThemeChanging/OnThemeChanged`。
8. 需要局部覆盖：`SetThemeValue<Light>(nameof(属性), value)` / `RestoreThemeValue<Light>(nameof(属性))`。

范本：`Examples/Theme/WPF/Demo/ThemeTile.cs:17-18`、`Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:36-37`、`Examples/Theme/Avalonia/Demo/App.axaml.cs:25`。

---

## 四、联动清单（改这里 = 必须同步改那几处）

### 1. 加一个转换器（平台侧）

> **本模块没有 `adapters/` 子目录，这是有意的。** 转换器这条平台轴只影响「哪些属性类型能被声明」，覆盖面窄且六家高度同形（同名的 `DoubleConverter` / `PointConverter` / …），不值得一家一篇。真正需要逐家展开的是**动画侧**的平台差异（`Interpolator.CreateScheduler`、sampler 覆盖），那些已在 `memory/modules/TransitionSystem/adapters/<平台>.md` 里，本模块**指路而不重复**。若将来转换器的差异真的分化到需要逐家说明，再建 `adapters/`。

| # | 做什么 | 依据 |
|---|---|---|
| 1 | 在**该家**的 `Src/Adapters/VeloxDev.<GUI>/PlatformAdapters/ThemeValueConverters.cs` 加一个实现 `IThemeValueConverter` 的类 | 六家都在 `namespace VeloxDev.DynamicTheme`（同名类各写一份） |
| 2 | **必须有无参构造** | 生成器用 `Activator.CreateInstance(typeof(...))` 内联 new（`Theme.cs:236`） |
| 3 | 类名要能作为特性泛型实参直接写 | 用户在 `[ThemeConfig<BrushConverter, ...>]` 里裸写类名（demo `:36-37`），所以命名空间必须能被 `using VeloxDev.DynamicTheme;` 覆盖 |

**Jalium 没有 `ThemeValueConverters.cs`** —— 这家要么不用主题、要么把转换器写在别处。核对时以 `Src/Adapters/VeloxDev.Jalium/` 为准。

### 2. 加一个主题类型（例如 `Solarized`）

**arity 是硬上限，加主题 = 升级 arity。**

| # | 做什么 | 依据 |
|---|---|---|
| 1 | 加一个 `ITheme` 空标记类（Core 的 `Dark.cs` / `Light.cs` 就是范本） | `Src/Core/VeloxDev.Core/Interfaces/DynamicTheme/ITheme.cs` |
| 2 | 该主题要覆盖的**每一条** `[ThemeConfig]` 都要改：arity 加 1、并补一个构造实参数组 | 泛型参数个数 = 1 转换器 + N 主题 |
| 3 | 主题数 > 6 就**做不到** | 见 §4 |

注意**不需要**在 `Theme.cs` 里为它注册任何东西 —— 主题类型是运行期的 `Type` 键，生成器不认识具体主题。`Current` 的默认值是 `typeof(Dark)`（`ThemeManager.cs:43`），换默认主题要改 Core。

### 3. 加一家平台（主题 + 动画都支持）

1. 该家要有 `Interpolator`（含 `CreateScheduler` 与 sampler 注册）—— 契约见 `memory/modules/TransitionSystem/extension.md`。
2. 该家要有 `ThemeValueConverters.cs`，覆盖它想支持的属性类型。
3. 用户侧调 `SetPlatformInterpolator`。
4. **不要**为了「某类型画不出来」让 `CreateScheduler` 返回 null（§二·3）。

### 4. 让 arity 8（7 个主题）真的可用 —— **三处必须同改**

现状：Core 声明了 arity 8（`ThemeConfigAttribute.cs:97`），但生成器只注册到 arity `` `7 ``（`Theme.cs:32-55`）。补上需要：

| # | 位置 | 改什么 |
|---|---|---|
| 1 | `Src/Core/VeloxDev.Core/DynamicTheme/ThemeConfigAttribute.cs` | 已存在，无需改 |
| 2 | `Src/Generators/VeloxDev.Core.Generator/Theme.cs:32-55` | 加第 6 个 `ForAttributeWithMetadataName("...ThemeConfigAttribute\`8", ...)` |
| 3 | `Theme.cs:57-83` 的 `Combine` 链 | **必须跟着改**：那一串是**定长元组**（`var ((((classes3, classes4), classes5), classes6), classes7) = combined;`），加一个 provider 就要加一层 `.Combine(...)` 并改解构，同时把新集合加进 `allItems`（`:66-71`） |
| 4 | `Examples/Theme/WPF Trimmed/Demo/MainWindow.xaml.cs:34-35` 的注释 | 改掉「at most one Converter plus seven Themes」—— 代码支持之后它才成立；在那之前它是**错的** |

漏第 3 步的后果是**编译错误**（元数不匹配），不是静默 —— 这一处比其它联动安全。

### 5. 改「一场切换包含哪些属性」

不在这里改。位置的判定在目标的 `GetStaticThemeCache()`（生成的，返回 `ThemeCache.GetStaticForType(this.GetType())`）与 `ThemeManager.PrepareSamplers` 的过滤条件（`:467-571`：起点取不到就跳过、终点取不到就跳过、`EndValue is null` 在 `BuildState` 里跳过）。要加「某类属性默认不参与」的规则，改 `PrepareSamplers`；要改「声明出哪些属性」，改生成器。

### 6. 与别的模块的联动

- **动画语义、时间轴、scheduler 契约**：全部在 TransitionSystem。本模块只是它的「入口 B」，见 `memory/modules/TransitionSystem/architecture.md` §三·入口 B 与 `extension.md` 的手工驱动范本。
- **平台能不能动、各家的 sampler 覆盖**：见 `memory/modules/TransitionSystem/adapters/<平台>.md`。**不要在这里重复那七份差异。**
- **`ThemeCache` 里的死成员**（`RegisterConverter` / `GetConverter` / `RemoveActiveEntry`，`ThemeCache.cs:73`、`:86`、`:148`）与死字段 `ThemeManager._def_cache`（`ThemeManager.cs:26`）：它们是「曾经打算做、现在没做」的遗迹（生成器改成内联 `Activator.CreateInstance`，`Theme.cs:236`）。**扩展时不要以为它们已经接上** —— 直接实现新路径，或顺手删掉。
- **`Theme.cs:191` 的 `converterKey` 是算了不用的局部变量**：注释自己写着 "only placeholder—converter created inline"。加转换器缓存要从这里重新设计，不是复用。

---

## 五、只能存疑的地方

1. **`PropertyInfo` 作为字典键的等值语义**：`ThemeCache` 的二级键与三级键都是 `PropertyInfo`，生成器两处都写 `typeof(X).GetProperty(nameof(P))`（`Theme.cs:241`、`:310`）。这能命中**依赖 BCL 的 `PropertyInfo` 按元数据（而非引用）比较**。这条我**没有在仓库内验证**（没有测试专门覆盖「两个 `GetProperty` 调用返回的实例互相命中」），只是从「这套代码在 demo 与测试里跑通」反推。若某天出现「覆盖值不生效」，这是第一个要查的地方。
2. **`ThemeConfigAttribute` 各 arity 的构造签名差异**：`Examples/Theme/WPF Trimmed` 用的是 `[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]`（`MainWindow.xaml.cs:36`），即「属性名 + 每主题一个参数数组」；而生成器按 `attribute.ConstructorArguments[0]` 取属性名、`TypeArguments[0]` 取转换器、`TypeArguments[1..]` 取主题（`Theme.cs:162`、`:166`、`:222-229`）。**未逐 arity 核对 6 个类的构造参数形状是否一致**。
3. **`UpdatePropertyToCurrentTheme` 会绕过 setter**：它直接 `propertyInfo.SetValue(this, value)`（`Theme.cs:372`），所以对 `[VeloxProperty]` 属性而言**会绕过生成的通知**。这是从代码直读的事实，但「是不是有意的」没有注释说明，也**没有测试覆盖**这一交叉场景（`VeloxProperty` + `ThemeConfig` 同属性）。
4. **`SetPlatformInterpolator` 的「每进程一次」是注释里的约定**（`ThemeManager.cs:52`「This method only needs to be called once」），代码本身不阻止重复调用 —— `_interpolator` 是普通静态字段，最后一次赋值胜出。重复调用是否有代价（例如 sampler 注册表被重复写）**未核实**。
