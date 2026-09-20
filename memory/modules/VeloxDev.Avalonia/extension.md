# VeloxDev.Avalonia — 扩展

> 契约在哪：`memory/modules/TransitionSystem/extension.md`（过渡/主题/采样器）、`memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3（七角色与附着属性注册位置）；平台形态与硬限制在两篇 `adapters/avalonia.md`。
> **本文只写在这家动手时改哪几个文件、按什么顺序、以及哪些路能编译但是错的。**

---

## 一、加一个采样器（这家的高频改动）

**先判该不该加。** Core 已经覆盖的是 `System.Drawing` / `System.Numerics` 那一组（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:10-27`）；Avalonia 的 `Point`/`Size`/`Color` 是**另外的类型**，所以本家必须各注册一份。判断式：这个类型是不是 Core 那张表里的类型？是 → 不要加；不是 → 加（见 `architecture.md` §六）。

步骤：

1. `PlatformAdapters/Samplers/XxxSampler.cs`：`public class XxxSampler : ISampler`，**命名空间必须写 `VeloxDev.Adapters.NativeSamplers`**（`Interpolator.cs:5` 的 using）。**必须是 public 且无参构造**：测试侧两条路都靠它 —— 独有类型直接 `typeof(X)`，撞名的走 `SamplerAssembly.GetType("VeloxDev.Adapters.NativeSamplers." + name)`（`AvaloniaEntries.cs:63-64`，`SamplerAssembly` 定义在 `:57`），两条路最后都落到 `Activator.CreateInstance(samplerType)`（`:80`）。
2. `PlatformAdapters/Interpolator.cs` 静态 ctor 加一行 `RegisterInterpolator(typeof(你的类型), new XxxSampler());`（现有 14 条在 `:13-26`）。注册接口（`IBrush`/`ITransform` 那种）也合法，且比注册基类覆盖面更大。
3. **可选**：想让它出现在 `Transition<T>` 的强类型重载表里，就在 `PlatformAdapters/Transition.cs:42-234` 加一条。`Property<TValue>(…)` 泛型重载已经能走通，所以「不加」不是遗漏。
4. **测试联动（硬闸）**：`Examples/Transition/AUTO TEST/Samplers/AvaloniaEntries.cs` 加一条 `Entry(...)`。**与别家撞名的用 `CrossAdapter("XxxSampler")` 反射取，独有类型直接 `typeof(XxxSampler)`**（判别法：`architecture.md` §五）。
5. 不做第 4 步**测试必红**（是测试期硬闸，不是编译错）：`SamplerCoverageTests.EveryShippedSampler_IsAccountedFor`（`Examples/Transition/AUTO TEST/Samplers/SamplerCoverageTests.cs:65-90`）反射产品程序集里每个 `ISampler`，与注册表 + `UnreachableSamplers` 对账。注册表本身在那里（`SamplerRegistry.cs:18-22`）。
6. 只有在**纯数据进程里造不出端点值**时才进 `UnreachableSamplers`：`SamplerCoverageTests.cs:90` 起会真的去构造那个值来证伪理由，且条目必须以类型名出现在 `AUTO TEST/Conformance/*Conformance.cs` 表里（`:175-193` 按名字对账）。**这家的 14 个采样器全部可在纯数据进程构造，名单里没有 Avalonia 条目。**
7. 接一家新平台时的额外联动：新 `<家>Entries.cs` → 挂进 `AdapterSamplerEntries.cs:10-19` → 程序集名加进 `SamplerCoverageTests.cs:23-33` 的 `ExpectedAdapterAssemblies`。**最后这一处的机制要说清，很容易反过来理解**：采样器是「反射所有**已加载**的 `VeloxDev.*` 程序集」数出来的（`SamplerCoverageTests.cs:36-47` 的 `ProductAssemblies`），所以只要引用在，忘了加表**照样数得到、套件照样绿**；`ExpectedAdapterAssemblies` 唯一的作用是在**引用被摘掉**（程序集根本没加载）时把「安静变瞎」变成红（`:50-62`）。⇒ 漏了它不会当场暴露，是等到有人摘引用时才发现这个家从来没被验过。
8. **不要动 Core 的注册表**去补缺口。反过来说：`Avalonia.Rect` / `Avalonia.Vector` 的缺口只能在**本家**补（Core 不可能注册 Avalonia 的类型），补不补是个决定，见 `TransitionSystem/adapters/avalonia.md` §二.7。

---

## 二、加一个主题值转换器

1. 类放 `PlatformAdapters/ThemeValueConverters.cs`（现有 7 个，`:11` 起，文件共 261 行），命名空间 `VeloxDev.DynamicTheme`（与 `IThemeValueConverter` 同命名空间，所以文件里一条 `using VeloxDev.*` 都不需要）。
2. **必须 public + 无参构造**：生成器发的是 `Activator.CreateInstance(typeof(global::…))`（`Src/Generators/VeloxDev.Core.Generator/Theme.cs:236`）。给它加构造函数**不会编译报错**，只在真的切到那个主题的那一次炸。
3. 接入方式是**特性的第一个类型参数**，没有名字约定也没有注册表：`[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]`（`Examples/Theme/Avalonia/Demo/ThemeTile.cs:17-18`）。
4. **不要用 `ThemeCache.RegisterConverter` / `GetConverter`**（`Src/Core/VeloxDev.Core/DynamicTheme/ThemeCache.cs:73/86`）。`grep -rn "RegisterConverter\|GetConverter(" Src Examples --include=*.cs`（排除 `obj/`）**只命中这两处定义**（其余命中都是无关的 `TypeDescriptor.GetConverter`）—— 生成器不走它，七家适配器也不调它。看着像「官方的注册入口」，实际是没人用的 API。
5. 内部实现照现有 7 个的样子：**三条路都有实例，没有一条统一优先级链** —— 工具自己的解析 API（`ThemeValueConverters.cs:43` 的 `Point.Parse`、`:71` 的 `Thickness.Parse`、`:104` 的 `CornerRadius.Parse`、`:134`/`:194` 的 `Color.TryParse`、`:18`/`:234` 的 `TypeUtilities.TryConvert`）、资源查找（`:183` 的 Brush、`:227` 的 Object 走 `Application.Current.TryFindResource`）、`.NET TypeConverter` 兜底（`:247-250` 的 `TypeDescriptor.GetConverter`，只有 Object 用）。挑哪条取决于「这个值在 XAML 里写成什么」，抄相邻那条最省事。
6. **存疑（别按本文推断）**：生成器把 `converterType.ToDisplayString()` 写进 `typeof(global::{…})`（`Theme.cs:169`、`:236`），但仓库里**没有任何生成产物**（未开 `EmitCompilerGeneratedFiles`，`find` 全仓无 `*_ThemeConfig.g.cs`）⇒ 这个名字最终限定到什么形式、为什么能编译，我核不到。要改这一块，先真跑一次 Theme demo 并打开 `EmitCompilerGeneratedFiles` 看产物。

---

## 三、加一个工作流附着行为 / 一个被解析的具名控件

契约与注册位置**不在这里**（`WorkflowSystem/extension.md` §3.9 / §4.3）；「为什么必须 `public sealed class X : AvaloniaObject`、回调第一参是宿主控件」在 `WorkflowSystem/adapters/avalonia.md` §二.1 / §二.2。这家只剩**挂载**这一步要守：

1. 新行为 = `RegisterAttached<X, THost, TValue>` + 静态 ctor 里 `AddClassHandler<THost>`。`THost` 取**最小够用的控件类型**（现状只有四种：`UserControl` / `Control` / `InputElement` / `Panel`）。
2. 开关一律是附着属性 `IsEnabled`，回调里**先 `Detach` 再 `Attach`**（`WorkflowSurfaceBehavior.cs:113-115`，`Attach` 的第一句就是 `Detach`）。重挂不先解绑，事件会翻倍 —— 不报错。
3. 需要宿主「推数据进来」的具名控件（小地图那种）走 `…NameProperty` + `FindControl<T>`（`WorkflowSurfaceBehavior.cs:162-203`）。**加一个名字属性 = 改 4 处**：`RegisterAttached` 声明、`Get`/`Set`、`ResolveNamedControls` 里解析、`UnsubscribeResolvedControls`（`:205-225`）里解绑。漏第 4 处的症状是：把控件换掉后旧订阅还挂着，刷新重复执行。
4. demo 与模板同步：`Examples/Workflow/Avalonia Trimmed/Demo/Demo/Views/Workflow/*.axaml`（以及其他 demo）要写上属性；**另一个模块** `Src/Templates/VeloxDev.Avalonia.Templates/working/content/` 下对应的 item 模板（`workflow-tree-view/` / `workflow-node-view/` / `workflow-slot-view/` … 的 `TemplateClass.axaml`）也要跟着改，否则新项目生成出来的视图缺这一段。
5. 数据/尺寸变化后要重解析时，用公开的 `WorkflowSurfaceBehavior.Refresh(host)`（`:90`），不要自己再走一遍名字解析，也不要自己写 `viewModel.Layout.ViewportOffset` / `ScrollViewer.Offset`（那两处在 `Refresh` 路径上，见 `architecture.md` §八.3）。

---

## 四、看着能编译、但错的捷径

| 捷径 | 会怎样 | 正解 |
|---|---|---|
| 测试工程里直接写 `new PointSampler()` | CS0433（WPF/WinUI/Jalium 也有同名同命名空间的类型） | `CrossAdapter("PointSampler")`（`AvaloniaEntries.cs:63-64`） |
| 采样器放进自建命名空间 | 注册能过，但覆盖校验取不到、`VeloxDev.Adapters.NativeSamplers.X` 反射路径也断 | 命名空间照 §一.1 |
| 给主题转换器加带参构造 | 编译通过，切主题那一次才炸（`Activator.CreateInstance`） | 无参构造；要在内部建状态就懒建 |
| 用 `ThemeCache.RegisterConverter` 注册转换器 | 编译通过、永不生效（生成器不走它） | 转换器由消费方在 `[ThemeConfig<>]` 里点名 |
| 只在 `Attach` 里加订阅、`Detach` 里忘解绑 | 重挂后事件翻倍，静默 | 每个订阅在 `Detach` 里配对（§三.2） |
| 看到 `Rect` 就以为「Avalonia 有采样器」 | Core 注册的是 `System.Drawing.Rectangle`，与 `Avalonia.Rect` 是两个不同的类型，两个都不覆盖 `Avalonia.Rect` | 见 `architecture.md` §六的缺口 |
| 在 `Attached/` 里引一次 `VeloxDev.TransitionSystem`（或反过来） | 能编译，三条轴再也不能独立演进 | 现在零交叉引用（`architecture.md` §一），保持 |

---

## 五、联动清单

| 你加/改什么 | 必须同步的位置 |
|---|---|
| 一个采样器 | `PlatformAdapters/Samplers/XxxSampler.cs` + `PlatformAdapters/Interpolator.cs` 静态 ctor +（可选）`PlatformAdapters/Transition.cs` 重载 + `AUTO TEST/Samplers/AvaloniaEntries.cs` |
| 一个主题转换器 | `PlatformAdapters/ThemeValueConverters.cs` + **消费方**的 `[ThemeConfig<>]`（适配器侧无需登记） |
| 一个附着行为 | 本模块新类 + demo XAML + `Src/Templates/VeloxDev.Avalonia.Templates/` 的 item 模板 |
| 一个被解析的具名控件 | §三.3 的 4 处 + demo XAML |
| 接一家新平台 | `Src/Adapters/VeloxDev.<家>/` + `VeloxDev.slnx` + `AUTO TEST/Samplers/<家>Entries.cs` + `AdapterSamplerEntries.cs:10-19` + `SamplerCoverageTests.cs:23-33` 的 `ExpectedAdapterAssemblies` |
| Avalonia 版本升级 | `VeloxDev.Avalonia.csproj:14` 一处（三个 `PackageReference` 共用）；升完要重判两篇 `adapters/avalonia.md` 里标了「11.1.0 实测」的结论 |

---

## 六、什么时候**不**该在这家动手

- 「生成的属性长什么样」「`[ThemeConfig]` 支持几个主题」→ 生成器（`VeloxDev.Core.Generator/architecture.md`）。
- 三条轴的通则、契约、七角色职责 → `TransitionSystem/extension.md` / `WorkflowSystem/extension.md`。
- 「这家在缩放/连线/插槽时序上的偏差」→ `TransitionSystem/adapters/avalonia.md` §三 / `WorkflowSystem/adapters/avalonia.md` §三。
- Avalonia 版本行为（`DispatcherPriority` 是 struct、`DispatcherTimer` 没有 `IsRepeating`、`LayoutUpdated` 是整棵树一次）→ 那些是那两篇里的实测结论，改代码前先读它们，不要在这里重新推断。
