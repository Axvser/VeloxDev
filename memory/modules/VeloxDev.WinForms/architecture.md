# VeloxDev.WinForms — 架构

> 代码：`Src/Adapters/VeloxDev.WinForms/`。**20 个 .cs、4215 行**（`Attached/Workflow/` 9 个 3387 行，最大 `WorkflowSlotLayoutBehavior.cs` 790、`WorkflowSurfaceBehavior.cs` 699、`WorkflowNodeDragBehavior.cs` 538；`PlatformAdapters/` 10 个 825 行，最大 `ThemeValueConverters.cs` 473、`Transition.cs` 138、`UIThreadInspector.cs` 86；顶层 `GlobalUsings.cs` 3 行）。同目录另有一份 `README.md`（194 行）。
> **计数写法**：`git ls-files 'Src/Adapters/VeloxDev.WinForms/*.cs' 'Src/Adapters/VeloxDev.WinForms/**/*.cs'` = **20**。只写 `'.../**/*.cs'` 得 **19** —— 这条 pathspec 不匹配目录**本级**的 `.cs`（漏掉 `GlobalUsings.cs`；七家各自都正好漏这 1 个）。
>
> 本文只写「读完这 20 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。
>
> **本模块没有 `adapters/` 子目录，也不该有**：模块名本身就是一个平台，不存在平台轴。它在三条轴上的平台差异分别落在
> `memory/modules/WorkflowSystem/adapters/winforms.md`、`memory/modules/TransitionSystem/adapters/winforms.md`、`memory/modules/Templates/adapters/winforms.md`，本文**指路不抄**。

---

## 一、一个项目、三条轴、三份契约

| 轴 | Core 契约 | 本项目落点 | 平台差异记在哪 |
|---|---|---|---|
| WorkflowSystem | 七个视图角色 | `Attached/Workflow/`（9 文件，其中 `NativeWindowStyleHelper.cs`/`ViewManager.cs` 是支撑件） | `WorkflowSystem/adapters/winforms.md` |
| TransitionSystem | 宿主 / 解释器 / 调度器 / 帧 pacer / 采样器 | `PlatformAdapters/`（8 个类型 + `Samplers/` 1 个） | `TransitionSystem/adapters/winforms.md` |
| DynamicTheme | `IThemeValueConverter` | `PlatformAdapters/ThemeValueConverters.cs`（13 个转换器） | DynamicTheme 模块记忆 |

三个只在读这个目录时才会发现的结构事实：

1. **三条轴互不引用，但没有任何编译期阻挡。** 实测 `Attached/` 下零 `Transition`/`Interpolator`/`ThemeManager` 符号、`PlatformAdapters/` 下零 `Workflow` 符号 —— 靠的是纪律：`GlobalUsings.cs` 已经把 `VeloxDev.TransitionSystem` 灌进了**每一个**文件（含 `Attached/`）。⇒ 「在这家写了一条跨轴的调用」不会被拦下，而七家都维持着零引用，改的人得自己守。
2. **命名空间全部寄生在 Core 上，唯一例外的命名空间还和 Core 撞了名。** 唯一的采样器在 `VeloxDev.Adapters.NativeSamplers`（`PlatformAdapters/Samplers/PaddingSampler.cs:1`），而 **Core 自己的 15 个采样器在 `VeloxDev.TransitionSystem.NativeSamplers`**（`Src/Core/VeloxDev.Core/TransitionSystem/NativeSamplers/ColorSampler.cs:3`）—— 两个不同命名空间。⇒ 写 Core 那一支的 `using` 引不到这家唯一自带的采样器（AUTO TEST 用的是前者：`Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs:1`）。
3. **`VeloxDev.WorkflowSystem.AttachedBehaviors` 在 Core 里不存在**：七家各声明一份同名命名空间。XAML 平台靠 `assembly=` 消歧，C# 宿主只能写全名或别名 —— demo 与两份模板都用别名 `using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;`（`Examples/Workflow/WinForms/Demo/Form1.cs:8`）。

**DynamicTheme 这条轴在这家没有消费者**：`Examples/Theme/` 下只有 Avalonia/WPF 两族四个目录（无 WinForms），`ThemeManager.SetPlatformInterpolator` 的 12 处调用点（`Examples/Theme/` 4 处 + `Src/Core/VeloxDev.Core.Test/` 8 处）无一处是本平台。473 行转换器没有任何 demo/测试碰过 —— 它是三条轴里唯一「有实现、零使用」。

---

## 二、这家的「少」—— 本模块最该记的东西

七家提供的是一套同名 API，这家在实现层少得最多。以下每条都可在树内复核，且**不是**从别家推出来的：

| 项 | 这家的实情 | 依据 |
|---|---|---|
| 创建控件的钩子 | **没有**。全模块唯一的控件创建是 `_selector.CreateView(item)` + `_host.Controls.Add(view)`，即把「造节点卡片的控件」完全委托给用户实现的 `IWorkflowTemplateSelector` | `ViewManager.cs:14-20`、`:161`、`:168` |
| 网格装饰器 / 小地图 / 连线视图的**实现类** | 模块内**一个都没有**：`IWorkflowGridDecorator`/`IWorkflowMinimapOverlay` 只作为**类型测试**出现（`is IWorkflowGridDecorator` / `is IWorkflowMinimapOverlay`），加上注释；无任何实现类型 | `WorkflowSurfaceBehavior.cs:454`、`:467` |
| 画布平移（空白按下起拖、`Capture`、指针跟踪） | **不在这家**：`WorkflowSurfaceBehavior` 全文件没有 `MouseDown`/`MouseMove`/`MouseUp`（只有一个 `OnZoomMouseWheel`），平移量是靠**反射读宿主**的 `PanOffset` 属性 / 私有 `_panOffset` 字段 | `ResolvePanOffset`：`WorkflowSurfaceBehavior.cs:614-637`；读值处 `:532`/`:567` |
| 只有一个自动化消费者 | `Examples/Transition/AUTO TEST/Samplers/`：`WinFormsEntries.cs` 只有 **1 条** entry（`PaddingSampler`）。Workflow 轴零测试 | `Examples/Transition/AUTO TEST/Samplers/WinFormsEntries.cs` |
| Win32 补偿 | 独有 `NativeWindowStyleHelper.cs`（222 行，`internal static`）：`WS_CLIPCHILDREN`/`WS_EX_COMPOSITED`/`SWP_FRAMECHANGED` + 两处 P/Invoke。别家没有这个文件 | `NativeWindowStyleHelper.cs:23-24`、`:32`、`:47-60` |
| netstandard2.0 | 不在 TFM 三元组里 ⇒ `Transition.cs:111` 的 `#if !NETSTANDARD2_0` 在这家**恒真**（空转守卫） | `VeloxDev.WinForms.csproj:4`；`Transition.cs:111` |
| XML 文档 | 不生成（csproj 无 `GenerateDocumentationFile`；WPF/Jalium/Core 三家有） | `VeloxDev.WinForms.csproj:3-16` |
| README | 有，七家里只有两家有（另一家 Razor） | `Src/Adapters/VeloxDev.WinForms/README.md` |

**这条链的因果值得记清**：没有创建钩子 ⇒ 装饰器/小地图/连线卡片只能由用户写类 ⇒ 适配器只能通过「名字 + 接口类型测试」把数据推过去 ⇒ 于是这家的宿主侧必须有名字、有自绘、有平移。`memory/modules/Templates/adapters/winforms.md` 里那个 335 行自绘小地图，就是被这一条逼出来的产物。

---

## 三、宿主怎么把它接起来：命令式 `Set*` + pull

**没有属性系统、没有 `DataContext`、没有注册表。** 全模块 grep `ModuleInitializer` / `[assembly:` 零命中，唯一的静态构造是 `PlatformAdapters/Interpolator.cs:7-10`（注册采样器）。状态一律存在 `ConditionalWeakTable<Control, State>` 里，附着属性退化成静态 `Get`/`Set`。

**名字是唯一的接线语言。** 所有 `Set*Name` 立即解析，解析器是 `FindControlByName`（`WorkflowSurfaceBehavior.cs:658-669`）：**递归遍历宿主子树 + `Ordinal` 比较**，取第一个命中。⇒ 与 WPF 的 `NameScope` 不同：只管宿主子树、跨容器/非子孙一律找不到、**找不到不抛也不报**，只在那条数据推送处静默跳过。

**三个开关各做什么（这家最容易误判的一处）：**

| 调什么 | 真的做了什么 |
|---|---|
| `Set*Name` | 立即解析并接线；其中 `SetScrollViewerName`/`SetCanvasName`/`SetGridDecoratorName`/`SetMinimapOverlayName` 还会顺带对解析到的控件加 `WS_CLIPCHILDREN`（`EnsureClipChildrenForName`，`:642-656`） |
| `SetIsEnabled(true)` | 只做两件事：打开 `Refresh` 的门、给宿主加 `WS_CLIPCHILDREN` + `WS_EX_COMPOSITED`（`:146-153`）。**不订阅任何事件**，也没有早退（重复调用只重刷样式位） |
| `SetZoomEnabled(true)` | 挂 `element.MouseWheel` **加上** `Application.AddMessageFilter(state)`（`:190`）—— 一个**进程级** `IMessageFilter`；关掉时成对摘除（`:183-196`） |

- **`Refresh(Control)`（`WorkflowSurfaceBehavior.cs:422-494`）是唯一的重驱动入口**，第一行被 `IsEnabled` 门住（`:423-425`）；循环末尾 `PerformLayout()` + `Invalidate()`，而 `Update()`（同步重画）**只在 `host.Capture` 为真时**调（`:489-493`）。⇒ 「拖拽中要立刻看见」这件事由调用方保证，不在这家统一做（`WorkflowNodeDragBehavior.cs:222-229` 另有一处自己的 `Invalidate()+Update()+RedrawTree`）。
- **滚轮缩放是「先截获再决定」**：过滤器条件是 `m.Msg == WM_MOUSEWHEEL && Control.ModifierKeys != Keys.Control` 精确比较（`:42`）⇒ **Ctrl+Shift+滚轮不进这条分支**，交给原生滚动；命中条件是「消息目标控件沿父链能找到 zoom-enabled 宿主」（`ResolveSurfaceHost`，`:96-115`）；吞掉靠 `m.Result = IntPtr.Zero; return true;`（`:92-93`）。⇒ 过滤器是进程级但**按宿主收窄**的，这与「装了过滤器就全局吃滚轮」的直觉相反。
- **视图池由赋值触发**：`ItemsSource` 与 `TemplateSelector` **都**非空才起 `ViewManager`（`ViewPool.cs:84-97`），任一置 `null` 即停。
- **上下文是「反射写进去」的**：`Tag` 必写，另外反射找 `ViewModel`/`DataContext`/`BindingContext` 属性（`ViewManager.ApplyContext`，`:223-245`）；读的方向反过来 —— 沿**祖先**链找（`WorkflowSurfaceBehavior.ResolveTree`，`:496-519`）。写与读两套反射，互不相干。

---

## 四、`GlobalUsings.cs` 与 `VeloxDev.WinForms.csproj`

**`GlobalUsings.cs` 三行，与 WPF/WinUI 逐字相同**（`diff` 无输出）：`global using` `VeloxDev.TransitionSystem` / `VeloxDev.TransitionSystem.Abstractions` / `VeloxDev.Threading`。

- 这就是 `PlatformAdapters/` 里不写 `using` 却能用 `InterpolatorCore`/`ISampler` 的原因（它们在 `...Abstractions` 里）。
- **它不含 `VeloxDev.WorkflowSystem`** ⇒ `Attached/` 里 **6 个文件各自写**这一行（不需要的是 `NativeWindowStyleHelper.cs`、`ViewPool.cs`、`ViewManager.cs`；只有 `WorkflowSurfaceBehavior.cs:6` 多写了一条 `...StandardEx`）。
- **`global using` 是编译期的，不随包/`ProjectReference` 传给消费者** —— 宿主要用 `Transition<T>` 仍得自己写 `using VeloxDev.TransitionSystem;`。

**csproj 里影响代码本身的条件**（`VeloxDev.WinForms.csproj`）：

| 事实 | 行 | 后果 |
|---|---|---|
| `<TargetFrameworks>netframework4.6.1;net5.0-windows;netcoreapp3.0` | `:4` | 七家里只有这家与 WPF 是这个三元组 |
| `UseWindowsForms` + `SuppressTfmSupportBuildWarnings` | `:6`、`:9` | 三元组里 `netcoreapp3.0` 不带 `-windows`，靠后者压掉兼容告警（WPF 同形） |
| `Nullable` + `ImplicitUsings` + `LangVersion latest` | `:5`、`:7`、`:8` | 全模块开可空 |
| `GeneratePackageOnBuild` | `:10` | 与其余六家同；**没有** `GenerateDocumentationFile` |
| Debug → `ProjectReference`（`:23`）／非 Debug → `PackageReference VeloxDev.Core 9.0.0`（`:24`） | — | 与生成器那套双轨同形；包里唯一的依赖是 Core |
| 三个 TFM 一起编 | — | 新写的 BCL 调用必须在 netframework4.6.1 与 netcoreapp3.0 上都存在，否则只在对应的那一次编译里报错（`bin/` 下每个 TFM 一份产物） |

**别用 `bin/`/`obj/` 目录名推支持的框架**：这家的 `bin/Debug/` 下有 `net8.0-windows`/`net10.0-windows` 两个 **csproj 未声明**的目录（与 `memory/modules/VeloxDev.Core.Test` 记的同类现象）；而两个下游 demo 分别是 `net10.0-windows` 与 `net9.0-windows`，它们解析到的是 `net5.0-windows` 那一份资产。

---

## 五、陷阱（带依据）

1. **只写不读的公共面：这家 20 个文件里有 4 处「有 Get/Set、没读者」**，而且 README 把它们都写成可用。三条已写在 `WorkflowSystem/adapters/winforms.md` §4.1-4.3（`PointerPressSourceName`、`GetTransform`、`LayoutPropertyName` 链），本文只补两条**新的**，并给出这一族的总貌：

   | 成员 | 写出处 | 读取处 |
   |---|---|---|
   | `WorkflowSurfaceBehavior.SetPointerPressSourceName` | `:349`（写进 `:356`；`state.PointerPressSourceName` 在 `:22` 声明后**无第二个读者**） | 无（demo/模板照写：`Form1.cs:22`、`Controls/WorkflowCanvas.cs:183`、`workflow-tree-view/TemplateClass.cs:159`） |
   | `WorkflowCanvasTransformBehavior.GetTransform` | `:34` 公开 getter；写值走 `Apply`（`:65-74`），唯一调用点 `WorkflowSurfaceBehavior.cs:479` | 全仓零命中 |
   | `WorkflowSlotLayoutBehavior.Get/SetLayoutPropertyName`（`:210`/`:223`）、`Get/SetActualOffsetPropertyName`（`:237`/`:250`） | — | 唯一消费者是私有 `GetActualOffset`（`:758`），而它**自己零调用者** ⇒ 三个公共成员连成的整条链都是死的 |
   | **`WorkflowMinimapOverlay`（`public static class`，309 行）** ★ | 整个类 | 全仓 `WorkflowMinimapOverlay.(Set\|Get)` **零命中**；这个类不是「小地图」的正路 —— 正路是 `SetMinimapOverlayName` + 用户实现 `IWorkflowMinimapOverlay`（`README.md:49` 那一条），而 README 从头到尾**没有提到这个类** |

2. **最小化的开关会静默清掉树绑定。** `WorkflowMinimapOverlay.SetIsEnabled` 先 `Detach` 再 `Attach`（`:69`），而 `Detach` 里 `state.Tree = null`（`:126-134`）。⇒ **先 `SetWorkflowTree` 再 `SetIsEnabled(true)` 的顺序会把绑定丢掉**（`Attach` 里 `SubscribeTree` 拿到的树已是 null）；`SetWorkflowTree` 开头有 `ReferenceEquals` 短路（`:101`），所以只有**再绑一次**才恢复。安全顺序：先 `SetIsEnabled` 再 `SetWorkflowTree`。注意这个类目前零调用者，所以这是**潜在**坑（见 §五·1）。
3. **未建句柄时的同步请求是被「丢掉」而不是「推迟」。** `ScheduleSync`（`:442-468`）先置 `SyncPending = true`，但若 `!control.IsHandleCreated` 就把它复位成 `false` 后返回（`:467`）—— 请求消失，不是排队。⇒ 在节点卡 ctor（句柄未建）里调 `Refresh`/`SetIsEnabled` 不会立刻量锚点；要同步量必须走 `SyncNow`（`:288-317`，两份模板与 demo 都在用：`workflow-node-view/TemplateClass.cs:453`、`workflow-tree-view/TemplateClass.cs:934`）。README 的措辞是「throttled with a pending flag」（`:131`），只对建了句柄那条路成立。
4. **坐标宿主的两档解析方向相反，而且没有校验。** `ResolveCoordinateHost`（`:613-638`）：按**名字**时是在 `parentHost` 的**子树**里找（`ResolveParentHost` 也只是在控件子树里找 [父宿主名]，没设名字就是控件自己，`:640-647`）；按**类型**时是沿 `parentHost` 的**祖先**链上溯（默认 `typeof(Panel)`）；最后的兜底又是 `parentHost` 本身。⇒ 名字档漏了会静默落到类型档；类型档是**就近匹配**，节点卡与画布之间只要有一个 `Panel`，锚点就会按错的坐标系写下去而**不报错**（demo 因此显式传 `typeof(WorkflowCanvas)`：`Examples/Workflow/WinForms/Demo/Controls/WorkflowNodeCard.cs:82`）。
5. **`SyncSlot` 的 `SlotAnchorFromNode` 兜底不可达。** 那一段（`:607-610`）只在 `coordinateHost` 为 `null` 时执行，而所有调用点传进来的都是 `ResolveCoordinateHost` 的返回值 —— 那个方法**永不返回 null**（末行 `return parentHost`）。⇒ 这家事实上只有 `SlotAnchorFromCanvasLocal` 一条路（这也是唯一正确的路：见 `WorkflowSystem/adapters/winforms.md` §4.4）。
6. **`OnTreePropertyChanged` 的第一个分支进不去。** `if (sender is Control control)`（`WorkflowMinimapOverlay.cs:259`）—— 这个事件是**树模型**发的 `PropertyChanged`，`sender` 永远不是 `Control`，于是每次都走 `else` 的 `InvalidateBound(sender)`（`:277-294`）：**重画所有受管控件**。多张小地图时是 O(控件数)。
7. **`ThemeValueConverters.cs` 的类名与 `System.Drawing` 撞名，写短名解析到自己。** 文件里凡要用 GDI+ 那个必须全限定：`new System.Drawing.ColorConverter()`（`:317`、`:430`、`:440`）、`new System.Drawing.FontConverter()`（`:447`）；`ObjectConverter` 那条通用路径才用 `TypeDescriptor.GetConverter`（`:452`）。⇒ 在这个命名空间下新写转换器时写短名**不报错**，只是转换结果悄悄不对。
8. **`NativeWindowStyleHelper` 里 `WS_CLIPCHILDREN` 与 `WS_EX_COMPOSITED` 是同一个数值 `0x02000000`**（`:23-24`），却被喂给 `SetWindowLong` 的两个**不同 index**（普通样式 vs 扩展样式）。⇒ 改这两个常量时把两者当成「可互换的字面量」会静默改错样式位；`ApplyStyle` 只在对应位缺失时才调 `SetWindowPos(SWP_FRAMECHANGED)`（`:197-215`），所以症状是「有时生效」。`CompositedMaxControlCount = 100`（`:40`，判据 `CountDescendants(top)`，`:139`）不是防御性代码 —— 超了就不合成，坑的机制见 `WorkflowSystem/adapters/winforms.md`。

---

## 六、入口：我要改 X 先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 滚轮缩放 / Ctrl 判定 / 消息过滤器 / 平移量的反射读取 | `Attached/Workflow/WorkflowSurfaceBehavior.cs`（数学在 Core `WorkflowSurfaceMath`） |
| 名字解析、`Refresh` 推给装饰器/小地图的偏移 | 同上（`FindControlByName` `:658-669`；`Refresh` `:422-494`） |
| 节点拖拽的落点、坐标宿主、拖拽期重画 | `Attached/Workflow/WorkflowNodeDragBehavior.cs` |
| 插槽锚点写回与同步时机（含唯一的同步入口 `SyncNow`） | `Attached/Workflow/WorkflowSlotLayoutBehavior.cs` |
| 插槽两阶段连接手势、全局消息过滤器 | `Attached/Workflow/WorkflowSlotConnectionBehavior.cs` |
| 视图池 / 物品→控件的工厂 / 上下文注入 | `Attached/Workflow/ViewManager.cs`（挂点 `ViewPool.cs`） |
| 小地图的数据推送（正路） | `WorkflowSurfaceBehavior.Refresh` 的 `:467-475`；**实现类在用户工程里**（模板：`workflow-minimap-overlay/TemplateClass.cs`） |
| 画布变换的写值处 | `Attached/Workflow/WorkflowCanvasTransformBehavior.cs:65-74`（**写进去没人读**，见 §五·1） |
| Win32 样式、100 个控件的阈值 | `Attached/Workflow/NativeWindowStyleHelper.cs` |
| 线程、优先级、pacer、调度器、采样器 | `PlatformAdapters/` —— 差异与坑见 `TransitionSystem/adapters/winforms.md` |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs` |
| 「宿主怎么把这些接起来」的最小可读样本 | `Examples/Workflow/WinForms/Demo/Controls/WorkflowCanvas.cs:180-185` + `Controls/WorkflowNodeCard.cs:81-84`（**比任何模板都短**；模板在 `Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs`） |
| 包结构、TFM、双轨引用 | `VeloxDev.WinForms.csproj` |

---

## 七、这份文件没写的东西

- 七个角色各自要暴露什么成员、附着属性名、`PART_*` 命名约定 —— `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3。
- 这家的平台硬限制（无透明分层、两套平移模型、同步重画、反射接缝…）与逐条坑 —— `memory/modules/WorkflowSystem/adapters/winforms.md`。
- TransitionSystem 侧：`NonPriority`、16 个手写 `Property` 重载、`CreateFramePacer` 条件、`"WindowsFormsSynchronizationContext"` 字符串比较、`PaddingSampler` 的截断 —— `memory/modules/TransitionSystem/adapters/winforms.md`。
- 模板侧：七个条目产出什么形状、`IWorkflowMinimapScrollSource` 的 CS0246 耦合、六份 `ParseColor` —— `memory/modules/Templates/adapters/winforms.md`。
- 「怎么用这套模板搭一个 WinForms 工作流视图」（人面向）—— `skills/veloxdev-create-workflow/references/gui/winforms.md`。
