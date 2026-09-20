# VeloxDev.MAUI — 扩展

> 代码：`Src/Adapters/VeloxDev.MAUI/`。本文只讲**怎么在这家正确地加东西**。
> 契约本身的扩展点（要实现的接口、注册方式、生命周期钩子）在 `memory/modules/TransitionSystem/extension.md` 与
> `memory/modules/WorkflowSystem/extension.md` §3.9 / §4.3；本家的平台差异在 `memory/modules/{TransitionSystem,WorkflowSystem}/adapters/maui.md`。
> 结构与本家的 30 个文件见 `architecture.md`。

---

## 一、扩展点地图

| 想加的东西 | 落点 | 必须同步的地方 |
|---|---|---|
| 一个可动画类型 | `PlatformAdapters/Samplers/` 一个新类 + `Interpolator.cs:10-21` 一行注册 | §三·1 |
| 表达式形式的 `Property(...)` 重载 | `PlatformAdapters/Transition.cs:31-217`（Core 没有这张表，各家自写） | §三·1 |
| 一个附着行为（第 8 个角色之类） | `Attached/Workflow/` 新文件，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors` | §三·2 |
| 链接层的视觉/几何 | `Attached/Workflow/WorkflowLinkOverlay.cs` | §三·3 |
| 小地图的外观 | `Attached/Workflow/WorkflowMinimapOverlay.cs`（**继承它**，别改它） | §三·3 |
| 主题字符串 → 值 | `PlatformAdapters/ThemeValueConverters.cs` | §三·4（**仓库内无消费者**） |
| 视图池的模板选择 | 宿主侧 `ViewPool.TemplateSelector`（本模块只提供挂点） | §三·2 |
| Windows 专属的一条能力 | `Attached/Workflow/*` 里的 `#if WINDOWS` 块 | §四·4 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 2.1 「给 `Interpolator.cs` 加一行 `using System.Drawing;`」

**看着**：`Transition.cs:149-185` 那 7 个 `System.Drawing.*` 重载要写一长串全限定名，加个 using 就短了。

**错在哪**：`Interpolator.cs` 里所有裸名 `PointF`（`:13`）、`SizeF`（`:18`）会**静默改指** `System.Drawing` —— 这两个简单名在 MAUI 与 `System.Drawing` 里**都有**。于是 `:13`/`:18` 变成 `RegisterInterpolator(typeof(System.Drawing.PointF), new PointFSampler())`，而右边那个 `PointFSampler` 是**本家**的（`using VeloxDev.Adapters.NativeSamplers`，解 `Microsoft.Maui.Graphics.PointF`）。两件事同时发生，**都是 §五·1 那个 bug 的翻版**：

- 它经 `AddOrUpdate` **顶掉 Core 早已注册对的** `System.Drawing.PointF/SizeF`（Core 的 `Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:2,17,19` 用的是 `using System.Drawing` 的一份实现）⇒ 这两个类型的属性开始抛 `InvalidCastException`、整条 run 被取消；
- `Microsoft.Maui.Graphics.PointF/SizeF` 从此**没有任何键指向**。

编译期一声不响。`RectF`（`:20`）不会跟着变（`System.Drawing` 里没有这个名字），修复它更不能靠这一行。

**官方做法**：需要两套同名类型共存时，**逐字全限定**（`Transition.cs:149-185` 就是这么写的），并在文件头一句话说明「同名类型一律显式取 MAUI 的那一侧」（仓库里的既定写法：`Examples/Transition/MAUI/Demo/SamplerSubject.cs:3`、`Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs:6`）。

### 2.2 「`RectF` 那条注册是坏的，我把它注册对了就行」——已按此修（2026-09-20）

**当时的形状**：`Interpolator.cs:20` 注册 `typeof(RectF)`（= `Microsoft.Maui.Graphics.RectF`）却给了个解 `System.Drawing.RectangleF` 的采样器（旧 `Samplers/RectFSampler.cs:1,14`）。

**修法是改体，不是改键**：`RectFSampler` 现在解 Maui `RectF`，与自己的注册键一致（`Samplers/RectFSampler.cs:12-13`）。`System.Drawing.RectangleF` 的覆盖本来就**不归它** —— 那是 Core 的 `RectangleFSampler`（`Src/Core/VeloxDev.Core/TransitionSystem/Interpolator.cs:22`），纯数据套件里也一直有自己的表项。

**为什么不能反过来改键**：`RegisterInterpolator` 是 `AddOrUpdate`、**last-writer-wins**（`Interpolator.cs:89-95`），把键写成 `typeof(System.Drawing.RectangleF)` 会**顶掉 Core 的 `RectangleFSampler`** —— 一个适配器版本静默替换 Core 的实现，之后两家各自演化。**同名不冲突**（注册键就是 `Type`，同名而不同程序集是两个键），**同一个 `Type`** 才冲突。

**下次遇到同形状的错**（注册键的 `Type` 与采样器解箱的类型不是同一个）：`dotnet build` **不会报错** —— 但**采样器套件现在会红**（2026-09-20 补的网）：`Examples/Transition/AUTO TEST/Samplers/SamplerKeyTests.cs` 把本家 `Interpolator` 的静态构造真跑起来，再拿**真实注册表**问「条目声明的类型解析到谁」，与条目写的采样器逐字比（`EveryEntry_ValueTypeResolvesToTheSamplerItNames` `:73`）。依据是条目新增的 `ValueType` 字段（= 属性的**声明类型**，也就是注册键；`Samplers/SamplerEntry.cs:43`）。**新加采样器时不用额外写什么** —— 那个字段由 `EntryFactory.Create` / 各家 `Entry(...)` 从 `property.PropertyType` 自动填。

完整的后果链（哪个属性炸、哪个仍解析、为什么长期没露头）在 `architecture.md` §五·1。

### 2.3 「`WorkflowSlotConnectionBehavior` 也要 `CoordinateHostName`」

**看着**：另两个行为都有 `CoordinateHostName` + `CoordinateHostType`，插槽连接没有，像是漏了，补上就一致了。

**错在哪**：`WorkflowSlotConnectionBehavior` 的 DP 只有 `IsEnabled`（`:34`）和私有的 `State`（`:41`）。它在 XAML 里**没有这两个附着属性可绑**，所以给它加上 `CoordinateHostName="PART_Canvas"` 是**静默无效**（XAML 不报错、不解析）。它的坐标宿主是**写死**的规则：最近的祖先 `AbsoluteLayout`（`FindCoordinateHost` `:487-497`），画布是最靠近的、`WorkflowSurfaceBehavior.GetIsEnabled` 为真的祖先 `ContentView`（`FindSurface` `:500-511`）。

**官方做法**：连接行为的宿主约定是「把插槽放进 `AbsoluteLayout` 里，且它上溯到的那块画布真的开了 `IsEnabled`」。要改这条约定，改的是 `:487-511` 两个方法，不是加附着属性 —— 而且这是**跨七家**的行为（删掉任何一家的 `CoordinateHostType` 属性的解析路径前先看别家）。

### 2.4 「把 `DataTemplate` 放进 `Resources`，让视图池按资源键找」

**看着**：`ViewManager.FindDataTemplate` 有第三条路 `TryFindTemplateByResourceKey`（`:444-475`，看起来会遍历 `_layout` 祖先与 `Application.Current.Resources`），所以「按 key 放模板」应该能用。

**错在哪**：那段是**死代码** —— `:447` 写死 `var resourceKey = (string?)null;`，`:449-451` 立刻 `return false`，`:454-475` 永远不可达。视图池的模板**只能**来自 `TemplateSelector`（这与 `ViewPool.EnsureManager` `:96` 要求 `GetTemplateSelector(layout) is not null` 正好互证）。

**官方做法**：写一个 `DataTemplateSelector` 子类并赋给 `ViewPool.TemplateSelector`（`Src/Templates/VeloxDev.MAUI.Templates/working/content/workflow-template-selector/` 就是这件事的模板）。**别把「按资源键找模板」当成一个待接的能力** —— 要接它得先把死代码救活，而 `resourceKey` 从哪来本身就是个没答案的问题。

### 2.5 「给 `ViewPool` 补个 `IsEnabled` 开关，和其它行为统一」

**错在哪**：`ViewPool` 的挂载语义是**赋值即生效**：`OnTemplateSelectorChanged`（`:58-67`）无条件 `CleanupManager` → `EnsureManager`，且 `EnsureManager` 在「集合不是 `INotifyCollectionChanged`」或「没有 `TemplateSelector`」时**直接返回**（`:96`）—— 开关就是这两个前提。再加一个 `IsEnabled` 会变成第三个前提，而**模板包与 demo 都不会设它**（`TreeView.xaml` 上只有 `ItemsSource` 与 `TemplateSelector`），于是所有人看到的都是「没反应」。

### 2.6 「把 Attached 文件挪出 `VeloxDev.WorkflowSystem.AttachedBehaviors`」

**看着**：为了躲开跨适配器的 CS0433，给本家换一个命名空间就好了。

**错在哪（两层）**：

1. 那 8 个文件**靠命名空间嵌套**白拿 Core 的类型（`architecture.md` §1.1）。挪走会一次性打断对 `IWorkflowTreeViewModel`、`Viewport`、`WorkflowSurfaceMath` 的全部引用，报错是一整片「找不到类型」，看不出是命名空间的事。
2. 宿主的 XAML `assembly=VeloxDev.MAUI` 与这个命名空间是**一对**约定，七家共享。只改一家 = 七家不再同形，而 `memory/modules/WorkflowSystem/extension.md` §3.9 的七角色表、`skills/veloxdev-create-workflow/references/view-layer.md` 都按这个同形写。

**官方做法**：CS0433 是**已知代价**，用别名/反射绕（`Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs:6-15,45-61`）。

### 2.7 「给 `WorkflowNodeDragBehavior` 补一个 `PointerGestureRecognizer`」

**看着**：MAUI 的拖拽惯例是 `PointerGestureRecognizer` + `PanGestureRecognizer` 一起加。

**错在哪**：本家在**非 Windows** 上**刻意只加 `PanGestureRecognizer`**（`:93-97` 的注释：`PointerGestureRecognizer` 的 `PointerMoved/Released` 与 Pan 的生命周期打架、没有增益）。加了不会报错，只会让拖拽在非 Windows 上出现两次起停与丢帧。**Windows 上走的是第三条路**：原生 `PointerRoutedEventArgs`（`:87-91`、`:129-257`），两套实现必须都维护。

### 2.8 「`ApplyVisibleRegion` 里写请求的目标偏移不也一样吗」

**错在哪**：必须写 `ScrollViewer.ScrollX/ScrollY`（**已发生的原生偏移**），不能写 `ScrollToAsync` 的**请求值** —— 请求值可能落不到（`architecture.md` §三·5 与 `WorkflowSystem/adapters/maui.md` §二·6）。这里最容易「优化」错的一步是把两个来源合并成「目标值」，症状是松手一格跳。

### 2.9 「`GlobalUsings.cs` 是给宿主的便利」

**错在哪**：`global using` 是编译期、不随包/`ProjectReference` 传递。宿主要用 `Transition<T>` 仍得自己 `using VeloxDev.TransitionSystem;`。加一行到 `GlobalUsings.cs` 影响的只有本模块自己的 30 个文件。

---

## 三、新增一个 X 的步骤清单

### 3.1 新增一个「MAUI 类型可动画」

1. **写采样器**：`PlatformAdapters/Samplers/XxxSampler.cs`，照 `PointFSampler.cs` 的形状 —— `NormalizeStart`/`NormalizeEnd` 原样返回，`InsertFrame` 里解**MAUI**的类型。值类型是引用型或带内部结构时，草稿放 `ref object? working`（`ShadowSampler.cs:27-37` 的复用形状），**不要把状态挂到采样器字段上**（注册表是进程级的）。
2. **注册**：`PlatformAdapters/Interpolator.cs:10-21` 加一行。**这一步唯一要核的是「键的类型 == 采样器里解的类型」** —— 本文件没有 `using System.Drawing`，所以短名天然取 MAUI 那一侧；要用 `System.Drawing` 的类型就**逐字全限定**（见 §2.1、`architecture.md` §五·1）。
3. **（可选）表达式重载**：需要 `anim.Property(x => x.Xxx, value)` 有编译期类型约束时，在 `PlatformAdapters/Transition.cs:31-217` 加一段；不需要就靠 `:31` 的泛型兜底。除 `Transform`（`:44`）外，体都是「`state.SetValue` + 可选 `state.SetOptions`」两行。
4. **验收登记**：`Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs` 里 `Entry<...>("XxxSampler", rule, start, end, selector, expected)` 补一条闭式解，并在私有 `Target` 类（`:31-42`）上补一条同类型属性。**不登记会红**：`SamplerCoverageTests.EveryShippedSampler_IsAccountedFor`（`:64-87`）反射产品程序集里所有 `ISampler` 类，要求每个都在 `SamplerRegistry.Entries` 或 `UnreachableSamplers.All` 里。
5. **驱动不起来的**（端点值要真 MAUI 运行时）：登记到 `Samplers/UnreachableSamplers.cs`（`:38-63` 的形状：`SamplerType` + `RepresentativeValue` + `Reason`），再到 `Examples/Transition/AUTO TEST/Conformance/MauiConformance.cs` 补一条。**这条名册是可证伪的**：`EveryUnreachableSampler_NeedsAValueThatCannotBeBuiltHere` 会真去造那个值，造得出来测试就失败（`UnreachableSamplers.cs:20-25` 明写）。
6. **只在 MAUI 有的类型**（`PointF`/`SizeF`/`RectF`/`Shadow` 这一族）：只有本家要加这几步，别家不动。判据是**键的类型**，不是类名。

### 3.2 新增一个附着行为

1. `Attached/Workflow/` 新文件，`namespace VeloxDev.WorkflowSystem.AttachedBehaviors;`（**不要**写 `using VeloxDev.WorkflowSystem;` —— 见 `architecture.md` §1.1）。
2. 声明 `IsEnabledProperty`（`typeof(bool)`、默认 **`false`**、`propertyChanged: OnIsEnabledChanged`）+ 一个**私有** `StateProperty`（放这个行为的每元素状态）。
3. `OnIsEnabledChanged` 的门槛：`if (bindable is not <宿主类型> control) return;`，然后 `true → Attach` / 否则 `Detach`。宿主类型按需要选最窄的：`ContentView`（要给子树做坐标/尺寸的）、`View`（只碰自己的）。
4. `Attach` 的第一行 **`Detach(control)`**；`Detach` 必须幂等（先 `-=` 所有事件，再清 `State`，最后 `ClearValue(StateProperty)`）。
5. 坐标宿主的两种约定**二选一，不要新发明第三种**：`CoordinateHostName` + `CoordinateHostType` 附着属性（`WorkflowSlotLayoutBehavior.cs:53-63` 的形状，解析照 `WorkflowNodeDragBehavior.cs:369-388`），或者「最近的祖先 `AbsoluteLayout`」（`WorkflowSlotConnectionBehavior.cs:487-497`）。后者**没有附着属性**，别给它的使用者留一个不存在的开关。
6. 需要从控件名找部件时用 `FindByName<T>(name)`（`WorkflowSurfaceBehavior.cs:294-320` 的 `ResolveNamedControls`），并考虑 `Loaded` 时重跑一次（`:246-254`）—— 数据绑定晚到或换模板重建后名字作用域会重来。
7. **要动画/要重绘的属性必须带 `propertyChanged` 并把一帧内的多次写入合并**（`WorkflowLinkOverlay.cs:306-312` 与 `:585-600`、小地图 `MarkDirty`/`FlushInvalidate` `:375-399`）：`ApplyVisibleRegion` 一帧就连写 6 个 DP，不合并就是一帧六次 `Invalidate`。
8. **要在 Windows 上做原生的事**：`#if WINDOWS` + `control.Handler?.PlatformView`，并遵守三条时序纪律（平台元素没有 `DataContext`；`HandlerChanged` 早于平台视图创建，所以延后重试**必须有界**；换 handler 必须在 `OnHandlerChanging` 里摘钩子）—— 见 `WorkflowSystem/adapters/maui.md` §二·8。
9. **宿主/模板联动**：`Src/Templates/VeloxDev.MAUI.Templates/working/content/` 下 7 个模板、以及它要挂的那个 demo 页面，都要跟着加附着属性；第 3 步的宿主类型门槛决定了它挂在哪个元素上（弄错 = 静默不生效）。

### 3.3 换掉链接层的绘制 / 加一条视觉 DP

1. 加 DP：`WorkflowLinkOverlay` 的视觉 DP 统一 `propertyChanged: OnVisualPropertyChanged`（`:37-58`），由 `:306-312` 落到 `:585-600` 的合流 `ScheduleInvalidate()`；小地图用 `MarkDirty()`。
2. 画法：`WorkflowLinkOverlay` 的绘制在它内嵌的 `LinkOverlayDrawable` 里。**这一层是满屏尺寸、内容是整图**，所以裁剪不能省（`CullMargin = 24`，`:29`；判定在 `TryGetEndpoints` 出口还要判 NaN）。
3. 本版 `ICanvas` **只有 `SetFillPaint`、没有描边 paint**（`:231`）：渐变描边不可表达，光带是 `FlowSegments = 24`（`:221`）段 `DrawLine` 按链上里程逐段取色（`:260-290`）。想换画法先确认新画法不需要描边 paint。
4. 光带动画：链是 `static readonly Transition<WorkflowLinkOverlay> Flow`（`:134-161`），三段 550/650/550ms 结尾 `Repeat(int.MaxValue)`；宿主侧的开关是 `LinkFlowEnabled`，**默认 false**（`:61-62`），`StartFlow` 在它为 false 时提前返回（`:164-175`）。**仓库里只有完整版 demo 打开它**（`Examples/Workflow/MAUI/Demo/Controls/Workflow/WorkflowView.xaml:204`），参考实现 `Examples/Workflow/MAUI Trimmed/.../TreeView.xaml` 没设 ⇒ 光带在 Trimmed demo 里不跑。
5. 小地图**要继承、不要改基类**：`WorkflowMinimapOverlay` 是全模块唯一非 `sealed` 的类，模板 `workflow-minimap-overlay` 就是它的空壳子类（`Src/Templates/VeloxDev.MAUI.Templates/working/content/workflow-minimap-overlay/TemplateClass.cs` 只设 4 个颜色）。它自己订阅 Core 模型（`:298-371`），别再订阅一遍。

### 3.4 新增一个主题转换器

在 `PlatformAdapters/ThemeValueConverters.cs` 里实现 `IThemeValueConverter`（照 `DoubleConverter` `:9` 的形状），并记住：**本仓库没有任何消费者**（`architecture.md` §八）—— 加它、改它都跑不出症状，也没有 MAUI 主题 demo 可以验。别顺手把它当成「DynamicTheme 的 MAUI 适配器已就绪」的证据。

---

## 四、联动清单（漏一处通常不报错）

1. **改任何一个公开类型名**（`ViewManager`、`ViewPool`、`WorkflowSurfaceBehavior`、`WorkflowMinimapOverlay`、`WorkflowLinkOverlay`、`WorkflowSlotLayoutBehavior`、`WorkflowNodeDragBehavior`、`WorkflowSlotConnectionBehavior`）→ **七家各有一份同名类型，同在一个命名空间**（`architecture.md` §1.2）。漏改的症状只在「同时引用两家」的项目里出现（CS0433）。至少检查：`Src/Templates/VeloxDev.MAUI.Templates/working/content/` 下 4 个带 `assembly=VeloxDev.MAUI` 的 XAML、`Examples/Workflow/MAUI*/` 的 XAML、`skills/veloxdev-create-workflow/references/gui/maui.md`。
2. **改公开附着属性名** → 同上：模板包（7 个模板）、两个 MAUI workflow demo 的 XAML、`ViewPool` 的 `ItemsSource`/`TemplateSelector` 用法，以及其它六家的同名属性（七角色表在 `WorkflowSystem/extension.md` §3.9 / §4.3）。
3. **加/删一个采样器类** → `Examples/Transition/AUTO TEST/Samplers/MauiEntries.cs`（闭式解）或 `UnreachableSamplers.cs`（造不出端点值）+ `Conformance/MauiConformance.cs`（真 app 侧）。**覆盖率测试读的是产品程序集里的 `ISampler` 类型集合**，所以新类不被登记就一定红；反过来，登记表按**类名**取类型（`MauiEntries.cs:61` 的程序集限定反射），**键的类型没人核**（`architecture.md` §五·1）。
4. **动 `#if WINDOWS`** → 记住本家有两个 TFM（中立 + windows），31 处分支会编出**两份不同的实现**；而 demo 的 Windows TFM 是**条件的**（`Examples/Workflow/MAUI Trimmed/Demo/Demo.csproj:5` 的 `IsOSPlatform('windows')`），非 Windows 机器上这 31 处根本不参与编译。改完至少在一台 Windows 上编一次。
5. **动 `MauiVersion`（`:32`）或 Windows TFM 的下限（`:8`）** → 会整体决定「`#if WINDOWS` 的代码能不能编进库」（`:30-31` 记着 `10.0.0` 那版就是这么丢的）。同时 `Microsoft.WindowsAppSDK` 的版本（`:49`）要与消费 app 里 MAUI 解析到的那个对齐。
6. **不需要联动的**：生成器/分析器（本项目不引用它，`VeloxDev.MAUI.csproj:40-49`）；`Docs/`（那套文档站点**不在本仓库**）。

---

## 五、几个「以为能改、其实不该改」

1. **`WorkflowNodeDragBehavior.IsDraggingNode` 与 `WorkflowSlotConnectionBehavior._activeConnection` / `IsDraggingConnection` 是刻意的进程级静态**。前者被**平移逻辑**读（`WorkflowSurfaceBehavior.cs:854`），删它会连累平移的锚点策略；后者保证「同一时刻只有一次连线拖拽」，`TryBeginConnection` 的第一件事就是 `CancelActiveConnection()`（`:226`）。
   顺带一条：`WorkflowSlotConnectionBehavior.SetIsDraggingConnection`（`:47`，`public`）在**全仓库没有任何调用点** —— 它由 `:254` 置真、`:421` 置假，那两处都是内部写。别以为外部有人在用它。
2. **两个 overlay 的不对称是有意的**：小地图**被推**（实现 `IWorkflowMinimapOverlay`，由 `UpdateMinimapOverlay` 写 6 个值），链接层**自己拉**（不实现任何 Core 接口，偏移从装饰器实例上绑）。给链接层加 Core 接口实现会牵动宿主 XAML 的绑定形态；把装饰器的值改成推给链接层则要新增一条写路径。**改之前先读 `architecture.md` §三·3。**
3. **`WorkflowLinkOverlay.InputTransparent = true`（`:75`）与它在 XAML 里的位置都是承重的**：这一层满屏盖在节点上，去掉 `InputTransparent` 会吞掉全部节点交互；z 序靠 XAML 位置（在装饰器内、`ScrollView` 之前，`TreeView.xaml`），挪位置等于改层序。
4. **`ViewManager` 把隐藏视图留在 `_layout.Children` 里**（只 `IsVisible=false` + `ZIndex=-100`，`:260-293`，理由 `:279-282`：移除会触发昂贵的 MAUI 重排）。想「清理一下」把它们 `Remove` 掉是在改性能特征，不是在整理。
5. **`Refresh` 的两道闸不能为了「让它一定生效」拆掉**：`IsEnabled` 那道保证没开开关时静默；`IsRefreshing` 那道记着「每次画布扩张级联 2-3 次 Refresh，正反馈减速螺旋」（`:134-137`）。
6. **`ApplyVisibleRegion` 的 NaN 提前返回**（`:1095-1100`）不是防御性编程，是必需 —— 放进去会让整批 `VisibleItems` 被清空（`:1081-1085` 的注释）。任何一处 NaN 守卫都不要删。
