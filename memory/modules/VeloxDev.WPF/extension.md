# VeloxDev.WPF — 扩展

> 读法：七角色契约、附着属性名、注册位置在 `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；
> 三条轴上「这家和别家不一样」的地方在 `memory/modules/{WorkflowSystem,TransitionSystem,Templates}/adapters/wpf.md` 与
> `memory/modules/DynamicTheme/architecture.md`。本文**只回答一件事**：在这个项目里加一个 X，动哪几处、按什么顺序、哪条看着能编译的捷径是错的。
> 目录与轴的对照、注册表所在、csproj 的取值见 `architecture.md`，本文不抄。

---

## 一、扩展点地图

| 我想加 | 官方挂点（具体成员） | 位置 |
|---|---|---|
| 让一个 WPF 类型可动画 | 实现 `ISampler`，再 `RegisterInterpolator(typeof(T), new XSampler())` | `PlatformAdapters/Interpolator.cs:12-28`（唯一的注册表，12 条注册） |
| 换线程句柄 / 优先级 / 解释器 | 改 `TransitionSchedulerCore<UIThreadInspector, TransitionInterpreter, DispatcherPriority>` 的类型实参 | `PlatformAdapters/TransitionScheduler.cs:5-11` |
| 让 DynamicTheme 在这家真动画 | 覆写 `InterpolatorCore.CreateScheduler` | `PlatformAdapters/Interpolator.cs:30-33` |
| 主题里的字符串/画刷怎么解析成值 | 实现 `IThemeValueConverter.Convert(Type targetType, string propertyName, object?[] parameters)` | `PlatformAdapters/ThemeValueConverters.cs`（7 个 public 类） |
| 一条过渡时长预设 | `TransitionEffects` 的三个静态属性（可 `set`，进程级） | `PlatformAdapters/TransitionEffects.cs:5` / `:9` / `:13` |
| 一个新的画布操作面 | `DependencyProperty.RegisterAttached("IsEnabled", …)` + 一个私有附着 `State` DP 存每元素状态 | `Attached/Workflow/` |
| 换七角色里某一个的实现 | 同名文件（七家同分法） | `Attached/Workflow/` |
| 小地图换皮 | 继承 `WorkflowMinimapOverlay`（全模块唯一非 `sealed`） | `Attached/Workflow/WorkflowMinimapOverlay.cs` |

**这里没有的扩展点（别去找）：**

- **没有程序集级入口，也没有 `Initialize()`**：全模块 `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造是 `Interpolator.cs:12`。所以「注册」这件事没有统一挂点 —— 要么落在那个静态构造里，要么由宿主自己调（见 §二·1）。
- **没有「注册一个 ViewModel 类型」的地方**：适配器不 `new` Tree/Node，只消费宿主给的模型（`WorkflowSystem/extension.md` 二·20）。

---

## 二、官方做法 vs 看着能编译、但错的捷径

**这一节是全文最重要的部分。** 下面每一条走捷径都能编译通过，其中几条跑起来还像是好的。

| # | 错的捷径 | 为什么错 | 官方做法 | 依据 |
|---|---|---|---|---|
| 1 | 在 `Samplers/` 加一个 `ISampler` 类就以为生效了 | 注册表是唯一开关；没登记 = 该采样器**永不运行**，而库里没有任何东西会报错 | 同时在 `Interpolator` 的静态构造里加一行 | `PlatformAdapters/Interpolator.cs:12-28` |
| 2 | 把注册代码写在采样器自己的文件里 / `App` 里 / `Main` 里 | 没人保证它会跑在第一个 `new Transition<T>()` 之前；`RegisterInterpolator` 是**末位胜出**，谁先谁后直接改解析结果 | 只在静态构造里集中登记 | Core `Transition.cs:290` 的字段初始化器；`Src/Core/VeloxDev.Core.Test/TransitionSystem/InterpolatorCoreTests.cs:58`（`RegisterInterpolator_OverwritesExisting`） |
| 3 | 主题转换器做成单例 / `internal` / 带参构造 | 生成器**内联** `((IThemeValueConverter)Activator.CreateInstance(typeof(TConverter))!).Convert(...)`，所以「public + 无参」是**运行期**要求；`ThemeConfigAttribute` 只约束 `where TConverter : class, IThemeValueConverter`（没有 `new()`），错的写法**编译得过** | 每个转换器都写 `public class` + 隐式无参构造 | `Src/Generators/VeloxDev.Core.Generator/Theme.cs:236`；`Src/Core/VeloxDev.Core/DynamicTheme/ThemeConfigAttribute.cs` |
| 4 | 在 `VeloxDev.DynamicTheme` 命名空间里用短名 `BrushConverter` / `ColorConverter` / `ThicknessConverter` / `CornerRadiusConverter` | 这四个名字本文件里也有，短名解析到**本项目**那一个，且不报错，转换结果只是悄悄不对 | 要用 WPF 自带的那个就全限定：`new System.Windows.Media.BrushConverter()` | `PlatformAdapters/ThemeValueConverters.cs:156`、`:210`、`:246` |
| 5 | 新附着行为放进别的命名空间（哪怕更合理的名字） | 宿主 XAML 的 `xmlns` 写的是 `clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF`，换命名空间 = XAML 找不到，**且不报错** | 命名空间固定为 `VeloxDev.WorkflowSystem.AttachedBehaviors` | 该命名空间在 Core 里 0 次、七家适配器各声明一次（`git grep -l "namespace VeloxDev.WorkflowSystem.AttachedBehaviors"`）；宿主写法见 `Examples/Workflow/WPF Trimmed/Demo/Views/Workflow/TreeView.xaml:6` |
| 6 | 给 `WorkflowCanvasTransformBehavior` 的变更回调补逻辑 | 它是**刻意的空回调**，值由 `WorkflowSurfaceBehavior.ApplyLayout` 写到宿主上 | 改「画布怎么变换」去改写值处 | `Attached/Workflow/WorkflowCanvasTransformBehavior.cs:25-30` 对 `Attached/Workflow/WorkflowSurfaceBehavior.cs:571` |
| 7 | 让 `Attached/` 里的行为直接驱动一个 `Transition<T>`（或反过来） | 三条轴在程序集内互不引用是既成事实（`Attached/` 下零 `Transition`/`Interpolator`/`ThemeManager` 符号，`PlatformAdapters/` 下零 `Workflow` 符号）；跨一条就再也拆不开 | 想跨轴就在宿主侧接线，别在适配器里接 | `architecture.md` §一 的实测 |
| 8 | 用静态 `Dictionary<element, state>` 存每元素状态 | 会漏 `Detach` 时的清理，多个同类型元素之间还会串 | 私有附着 DP `"State"`（三个行为的既有形） | `WorkflowSurfaceBehavior.cs:70-74`、`WorkflowSlotLayoutBehavior.cs:55-59`、`WorkflowNodeDragBehavior.cs:38-42` |
| 9 | 以为重复置 `IsEnabled="True"` 会叠加订阅 | 每个 `OnIsEnabledChanged` 都**先 `Detach` 再 `Attach`**，并 `ClearValue(StateProperty)` 顺带丢状态 | 直接依赖这个幂等性 | `WorkflowSurfaceBehavior.cs:127-140`、`:155` |
| 10 | 把 `WorkflowSurfaceBehavior.Refresh(host)` 当「总是会重算」用 | 第一行就被 `IsEnabled` 挡掉：没打开开关时它**什么也不做、不抛** | 先确认开关已开，再调 | `WorkflowSurfaceBehavior.cs:97-109`（判断在 `:99-102`） |
| 11 | 改采样器类名（如 `PointSampler` → `WpfPointSampler`）只改文件 | 测试工程按**字符串**反射取类型（`GetType($"VeloxDev.Adapters.NativeSamplers.{samplerName}", throwOnError: true)`），编译不报错，测试运行时才炸 | 改名要同步那 12 个字符串 | `Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs:53-54` |
| 12 | 以为登记的先后有意义；或随手登记一个具体类型 | 顺序无意义（精确命中优先），但**登记基类型会接住整族**：`typeof(Brush)`（`:14`）、`typeof(Transform)`（`:18`）、`typeof(Effect)`（`:25`）就是这种「一登记一个家族」的写法。最后一条是 2026-09-20 补的 —— WPF 自己的 `UIElement.Effect` DP 声明成 `Effect`，注册具体类型会让它静默 `Unsampled` | 登记基类型时，把子类型差异在采样器内部写完：能插的插，插不了（异型效果）就**如实交出端点**而不是造替身（代价样貌见 `Samplers/TransformSampler.cs`、`Samplers/DropShadowEffectSampler.cs:44`） | `PlatformAdapters/Interpolator.cs:14`、`:18`、`:25` |
| 13 | 想「把两条 `NoWarn` 合起来」于是只改一行、或新增一行 | 同名 MSBuild 属性按序求值、**后者覆盖前者**，现在实际生效的只有 `1591` | 写成一条分号分隔的列表 | `VeloxDev.WPF.csproj:4` 与 `:5`；正确形见 `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:4` |

---

## 三、步骤清单

### A. 让一个新的 WPF 类型可动画（加一个采样器）—— 最常见的路径

1. **建文件** `PlatformAdapters/Samplers/XxxSampler.cs`，命名空间 `VeloxDev.Adapters.NativeSamplers`，实现 `ISampler`。端点约定：`t == 0` / `t == 1` 原样交出调用方给的起点/终点实例（范式见 `PlatformAdapters/Samplers/TransformSampler.cs`）。
2. **登记**：`PlatformAdapters/Interpolator.cs:12-28` 加一行 `RegisterInterpolator(typeof(X), new XxxSampler());`。
3. **补验证表**（**最容易漏的一步**）：`Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs` 加 ① 私有 `Target` 类上的一条属性（`:30-44`），② 一条 `Entry(CrossAdapter("XxxSampler"), SamplerRule.Xxx, start, end, selector, t => …)`，③ 放进 `All`（`:182-263`）。漏了这三步不会在库里报错，但 `EveryShippedSampler_IsAccountedFor` 会红 —— 判定是**反射**所有 `VeloxDev.*` 程序集里的 `ISampler`（`Samplers/SamplerCoverageTests.cs:44-47`）再与 `SamplerRegistry.Entries` 求差（`:65-87`），而 `Entries` 的传递链是 `SamplerRegistry.cs:20-21` → `AdapterSamplerEntries.cs:11` → `WpfEntries.All`。
4. **命名空间不能换**：新类型的名字若与 Core 或别家适配器撞名，`VeloxDev.Adapters.NativeSamplers` 是唯一能让它编得过去的位置（七家都用这个名字，所以同时引用两家的工程必然 CS0433，绕法就是 `WpfEntries.cs:49-54` 那条反射）。
5. **同步 `memory/modules/TransitionSystem/adapters/wpf.md`** 的条数与清单。

> 判断「我登记对了没」：`RegisterInterpolator` 返回什么也不告诉你，`TryGetInterpolator` 才告诉你（`ISampler.cs` 的 `<see cref="…RegisterInterpolator"/>` 指向的就是它）。**没有「列出全部登记项」的公开 API**。

### B. 加一个主题值转换器

1. `PlatformAdapters/ThemeValueConverters.cs` 里加 `public class XxxConverter : IThemeValueConverter`，命名空间 `VeloxDev.DynamicTheme`，**public + 无参构造**（理由见 §二·3）。
2. `Convert` 的三个入参语义：`targetType` 是**目标属性的类型**，`propertyName` 是属性名（做兜底/诊断用），`parameters` 是宿主在 `ThemeConfig` 里写的那串 `object?[]`。
3. 解析失败一律 `return null`（本文件的既定姿态：`catch { return null; }`，异常不逃逸）。用 WPF 自带转换器时全限定（§二·4）。
4. **宿主侧**：把 `XxxConverter` 填进 `[ThemeConfig<XxxConverter, TTheme1, TTheme2, …>(…)]` 的第一个类型参数 —— 位置参数的形状见 `Src/Core/VeloxDev.Core/DynamicTheme/ThemeConfigAttribute.cs`（arity 3..8 的泛型类，`where TConverter : class, IThemeValueConverter`）。
5. 目标主题数的**实际上限是 6 个**（不是 7）：生成器只订阅了 arity 3..7，arity 8 的标注不进管线 —— 见 `memory/modules/DynamicTheme/architecture.md` §七·5。

### C. 加一个附着行为（这家独有的操作面）

1. **建文件** `Attached/Workflow/<Name>Behavior.cs`，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors`，`sealed class : DependencyObject`。
2. **开关**：`IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(X), new PropertyMetadata(false, OnIsEnabledChanged))` —— 默认值必须是 `false`（四个既有开关都是），回调里判宿主类型后 `Attach` / `Detach`。
3. **宿主类型门槛**照 `architecture.md` §3.1 的表选：要 `FindName` 找部件就只能挂 `UserControl`；要跨 `DataTemplate` 找部件得走 `ItemContainerGenerator` + 视觉树下钻（`WorkflowSlotLayoutBehavior.cs:328-333`、`:413-423`）；只要 `DataContext` 就放宽到 `UIElement`（但要先想清是「自己」还是「自己或任一祖先」）。
4. **每元素状态**放私有附着 DP `"State"`（`state` 类型是个私有类），`Detach` 里 `ClearValue(StateProperty)`。
5. **订阅**一律在 `Attach` 开头先 `Detach` 一次，保证重挂幂等。
6. **驱动重算**：改了模型几何要让宿主重算时，调 `WorkflowSurfaceBehavior.Refresh(host)` —— 但它被 `IsEnabled` 挡着（§二·10）。
7. **联动**见 §四·3。

### D. 换 / 加一个视图角色的实现

七角色各自要暴露什么成员、`PART_*` 约定、`DataTemplate` 根绑定的写法，全在 `memory/modules/WorkflowSystem/extension.md` §3.9 与 `skills/veloxdev-create-workflow/references/view-layer.md`；这家的逐文件对应关系见 `architecture.md` §六。文件增删后走 §四·3 的联动清单。

### E. 改 TFM / 引用方式

1. `VeloxDev.WPF.csproj:7` 的三元组会改 `#if` 的取值：现在没有 `netstandard2.0`，所以 `PlatformAdapters/Transition.cs:197-222` 那 4 个 `System.Numerics` 重载**恒成立**，加回 `netstandard2.0` 会让它们消失（而且 `UseWPF` 与它不相容）。
2. `:26` / `:27` 是一对**互斥**的双轨（Debug `ProjectReference` / 非 Debug `PackageReference`），改一条要同时看另一条 —— 两者同时生效会报重复成员（理由与生成器那套同形，见 `memory/modules/VeloxDev.Core.Generator/architecture.md` §五）。

---

## 四、联动清单（加一个 X 时必须同步修改的所有位置）

**漏一处通常不报错，只是静默少一个能力。**

### 4.1 加一个平台采样器

- [ ] `Src/Adapters/VeloxDev.WPF/PlatformAdapters/Samplers/XxxSampler.cs`
- [ ] `Src/Adapters/VeloxDev.WPF/PlatformAdapters/Interpolator.cs:12-28` 登记（**不登记 = 永不运行**）
- [ ] `Examples/Transition/AUTO TEST/Samplers/WpfEntries.cs`：`Target` 属性 + 一条 `Entry` + 放进 `All`（`:182`）—— **漏了直接红**
- [ ] 与 Core / 别家撞名时，命名空间留在 `VeloxDev.Adapters.NativeSamplers`
- [ ] `memory/modules/TransitionSystem/adapters/wpf.md`（采样器条数与清单；同时引用两家的探针工程要不要加别名）
- [ ] 这条类型在别家也该有时：各家的 `Interpolator.cs` 各加一份 —— **条数本来就不等**（Avalonia 14 / WPF 12 / MAUI 12 / WinUI 10 / Jalium 9~10 / WinForms 1 / Razor 1，见 `memory/modules/TransitionSystem/adapters/wpf.md`），别按 WPF 的数去猜

### 4.2 加一个主题值转换器

- [ ] `Src/Adapters/VeloxDev.WPF/PlatformAdapters/ThemeValueConverters.cs`
- [ ] 使用方的 `[ThemeConfig<…>]` 标注处（不在本仓库，在宿主工程）
- [ ] 若这条转换器该跨平台通吃：另外几家的同名文件各加一份 —— **七份本来就不等**（WPF / Avalonia / MAUI / WinUI 各 7 个类，WinForms 13 个，Razor 4 个，Jalium 根本没有 `ThemeValueConverters.cs`）
- [ ] `memory/modules/DynamicTheme/architecture.md` §六 的入口表（「字符串/画刷怎么解析」指向各家这份文件）

### 4.3 加/换一个工作流视图角色（或加一个附着行为）

- [ ] `Src/Adapters/VeloxDev.WPF/Attached/Workflow/` 的文件
- [ ] `Src/Templates/VeloxDev.WPF.Templates/working/content/` 下 7 个条目里对应的那个（`workflow-tree-view` / `-node-view` / `-slot-view` / `-link-view` / `-grid-decorator` / `-minimap-overlay` / `-template-selector`）；XAML 里的 `;assembly=VeloxDev.WPF` 与 `PART_*` 名字都要跟着看
- [ ] 两套 demo 都要过：`Examples/Workflow/WPF/Demo/` 与 `Examples/Workflow/WPF Trimmed/Demo/`（取舍见 `memory/modules/WorkflowSystem/adapters/wpf.md` 坑 1）
- [ ] `skills/veloxdev-create-workflow/references/gui/wpf.md`
- [ ] `memory/modules/WorkflowSystem/adapters/wpf.md`
- [ ] `memory/modules/Templates/adapters/wpf.md`（模板侧形状变了才动）
- [ ] `VeloxDev.slnx` **只在新增项目时**才动（适配器本身早就在里面）

### 4.4 以 WPF 为模板搬一家新平台

**逐文件搬运只在 WinUI 上成立**：只有它的 `Attached/Workflow/` 与 WPF 是**同一组 8 个文件名**。其余五家各有硬差异，搬之前先认：

| 家 | 与 WPF 的差异（`git ls-files 'Src/Adapters/VeloxDev.<家>/Attached/Workflow/'`） |
|---|---|
| WinUI | 8 个文件名逐个相同 —— 唯一可直接对照的 |
| WinForms | 多一个 `NativeWindowStyleHelper.cs` |
| Avalonia | 多一个 `PlatformDetection.cs` |
| MAUI | **没有 `WorkflowCanvasTransformBehavior.cs`**（换成 `WorkflowLinkOverlay.cs`） |
| Jalium | 多 `IWorkflowTemplateSelector.cs` / `WorkflowGridDecorator.cs` / `WorkflowTreeView.cs` |
| Razor | `Attached/Workflow/` 下 **17 个文件 = 7 个 `.razor` + `.razor.cs` 对，另加 3 个独立 `.cs`**（`WorkflowCanvasTransformBehavior.cs` / `WorkflowGeometryScope.cs` / `WorkflowRuntimeIds.cs`），**完全没有 `ViewManager`**（`git grep -ln "class ViewManager" -- Src/Adapters/VeloxDev.Razor` 零命中） |

另外两条与「搬」有关的既有事实：**WPF 与 WinForms 的 csproj TFM 三元组逐字相同**；`WorkflowGridDecorator` 只有 Jalium 与 Razor 放在适配器里，WPF 把它放在模板包里（`memory/modules/Templates/adapters/wpf.md` §一）。

其余注册位置（`VeloxDev.slnx`、7 个模板条目、两套 demo、skill 平台页、`adapters/<平台>.md`）见 `memory/modules/WorkflowSystem/extension.md` §4.3，不重复。

---

## 五、几个「以为能改、其实不该改」的地方

1. **`TransitionEffects` 的三个时长**（`PlatformAdapters/TransitionEffects.cs:5/9/13`：`Empty` 0s、`Theme` 0.46s、`Hover` 0.32s）**六家逐字相同** —— 改这里等于改所有平台的默认观感，不是 WPF 一家的事。
2. **不要给 `UIThreadInspector.ThreadFor` 加「总是取 UI 线程」的兜底**。这家的语义是「从 target 上取它自己的 dispatcher」，这是它与 Jalium 之外六家的区别所在（`PlatformAdapters/UIThreadInspector.cs:10-24`；对照 `Src/Adapters/VeloxDev.Avalonia/PlatformAdapters/UIThreadInspector.cs:9`）。
3. **`WorkflowCanvasTransformBehavior.OnTransformChanged` 的空实现**（§二·6），以及 `WorkflowMinimapOverlay.RulerBand => 0`（`WorkflowMinimapOverlay.cs:132`）—— 后者是「标尺避让在模板里做」的产物，理由见 `memory/modules/WorkflowSystem/adapters/wpf.md` 坑 2。
4. **`Interpolator.cs:12-28` 的静态构造不要动顺序、也不要删注册**：删一行不会报错，只会让某个类型的动画静默失效（§二·1）。
