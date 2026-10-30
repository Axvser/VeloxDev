# Interfaces — 扩展

> 契约分层与归属规则在 `architecture.md`，本文只回答「我要加一个新契约，照哪套来」。
> 写法提醒：文件名与命名空间**不同步**（`Interfaces/Timing/ITimeSource.cs` 的命名空间是 `VeloxDev.Timing`），所以本文里凡是说「放哪个目录」都指**路径**，不是命名空间。

---

## 一、先做三问，决定它该不该进 `Interfaces/`

| 问题 | 走出去 | 留在原地 |
|---|---|---|
| 1. 有几个「实现方家族」？ | ≥2（Core 一份 + 应用/适配器/生成器各自一份） | 只有 1 个 → 跟实现同住（`Src/Core/VeloxDev.Core/WorkflowSystem/CompilerEx/*/Contracts/` 那一批就是这样） |
| 2. 它是不是某份 EventArgs / 某个具体类型的附属？ | 不是，可独立命名 | 是 → 与宿主同文件（`Src/Core/VeloxDev.Core/AI/AgentConfirmationEventArgs.cs` 的 `IAgentConfirmationNotifier`） |
| 3. 它的实现方里有没有**生成器**？ | 有 → 必须进 `Interfaces/`，因为生成器按**字符串全名**引用（`Src/Generators/VeloxDev.Core.Generator/Theme.cs` 的 `IThemeFullName` 等常量、`Writers/TickWriter.cs` 的 `GenerateBaseInterfaces`），放哪儿都得是稳定路径 | 没有 → 按 1、2 判断 |

三问都过，再选子目录：**子目录 = 它服务的模块**（`AspectOriented`/`DynamicTheme`/`MVVM`/`Tickable`/`Timing`/`TransitionSystem`/`WorkflowSystem`），不是按「契约种类」分。一个新模块的契约就新开一个子目录。

**判据的可执行版本**：`grep -rn "public interface" Src/Core/VeloxDev.Core/Interfaces/` 得到一批接口 —— 每一条都能指名它的 ≥2 个实现方家族；做不到的那些都在 `Interfaces/` 外（清单见 `architecture.md` §二）。分布上 `WorkflowSystem` 最多，`TransitionSystem`、`Timing`、`MVVM`、`DynamicTheme` 次之，`Tickable` 与 `AspectOriented` 各只有一处。

---

## 二、名字怎么起

从现有契约归纳出来的取名规矩，按「你在造什么」查：

| 你在造什么 | 名字形状 | 仓内实例 |
|---|---|---|
| 一个普通契约 | `I<名词>`，不加 `Core`/`Base`/`Abstract` | `ITheme`、`IFrameState`、`IEaseCalculator` |
| 带优先级的泛型契约（窄） | `I<名词><TPriorityCore>`，第一个（往往也是唯一一个）泛型参数**一定**叫 `TPriorityCore` | `ISampler<TPriorityCore>`、`ITransitionEffect<TPriorityCore>` |
| 上面那个的非泛型宽版 | **`I<名词>Core`**（`Core` 是后缀，不是前缀） | `ITransitionEffectCore`（`ITransitionEffect.cs`）、`ITransitionSchedulerCore`（`ITransitionScheduler.cs`） |
| 只做继承收窄、不加成员的空接口 | `I<名词>` 或 `I<名词>Core`，**成员一个都不加** | `ITransitionScheduler : ITransitionSchedulerCore`（`ITransitionScheduler.cs`）、`ITaskContext : IAccessContext`（`ITaskContext.cs`） |
| 约束载体（不关心成员，只用于 `where`） | `I<名词>`，空体 | `IAspectOriented`、`ITheme` |
| 视图模型 | `I<名词>ViewModel`，**继承 `INotifyPropertyChanging, INotifyPropertyChanged`** | 4 族：`IWorkflow{Tree,Node,Slot,Link}ViewModel` |
| ViewModel 的配套工具 | `I<名词>Helper` | `IWorkflowHelper` 与各族的 `I<族>ViewModelHelper`（`IWorkflowTreeViewModel` 的 `GetHelper()` 返回 `IWorkflowTreeViewModelHelper`） |
| 提供者（给一个集合/映射） | `I<名词>Provider` | `ISlotProvider`、`IConditionalSlotProvider<TSlot>`、`ISpatialBoundsProvider` |
| 平台要实现的「数据交换」契约 | `I<名词>Decorator` / `I<名词>Overlay` | `IWorkflowGridDecorator`、`IWorkflowMinimapOverlay` |
| 异步通知/回执 | 接口 `I<名词>Notifier`，**与 `Agent<名词>EventArgs` 同文件** | `Src/Core/VeloxDev.Core/AI/AgentSelectionEventArgs.cs` 的 `IAgentSelectionNotifier` |
| 事件参数 | `<名词>EventArgs`（`XXX` 不带 `I`） | `FrameEventArgs`、`TransitionEventArgs` |

**反例（都是仓内的，照抄会出事）**：

| 反例 | 为什么别照抄 |
|---|---|
| `ITransitionEffect.cs` 的 `InvokeCancled` | 拼写错（`Canceled` 事件是对的，`InvokeCancled` 少一个 `c`）。**改名是破坏性变更**：`Src/Core/VeloxDev.Core/TransitionSystem/Effects/TransitionEffect.cs` 的私有字段也叫 `_cancled`。新契约别复制这个拼写 |
| `IVeloxCommand`（`IVeloxCommand.cs`） | 它 `: System.Windows.Input.ICommand` —— 契约直接继承了一个 BCL 接口，于是实现方必须同时满足两边。只在「所有实现方本来就都要实现 BCL 接口」时才这么做 |
| `Interfaces/Tickable/ITickable.cs` | **当心不可见字符** —— 这个名字曾带着一个 U+200B（2026-10-01 已清除，见 architecture.md §八·9）。**新契约绝对不要**引入 Cf 类字符：编译器忽略它，所以能编译，但裸路径打不开、`grep -l` 漏、文档生成器可能崩 —— 而代码评审看不出来 |
| `IWorkflowTreeViewModel.cs` | 单个文件里声明多个接口（`ITransitionScheduler.cs`、四族 VM 文件都是这样）是既有做法，但代价是**按文件名找接口会失效**；新契约优先一文件一接口 |

---

## 三、形状怎么给：属性袋 / 方法 / 泛型

| 你的契约要表达 | 用 | 别用 | 仓内依据 |
|---|---|---|---|
| 一组可读（可能可写）的状态 | **属性袋**（`{ get; set; }` 一批） | 一个个 `GetX()`/`SetX()` | `IWorkflowTreeViewModel`（画布状态、集合、连接映射全是属性） |
| 有前置条件/可能失败的数据访问 | **方法 + 返回值**（`bool`、`TryX(out …)`） | 抛异常的属性 | `ITransitionProperty`：`SetValue(object target, object? value)` 返回 `bool`，注释明说读不到时返回 `null` |
| 需要由**外部**触发的状态迁移 | **方法**（`InitializeWorkflow()`、`OnPropertyChanged(string)`） | 可写属性 | `IWorkflowViewModel` 的 `InitializeWorkflow()`/`OnPropertyChanging(string)`/`OnPropertyChanged(string)` |
| 由提供方主动通知使用方 | **事件**（`event …EventHandler`） | 轮询属性 | `ITransitionEffectCore` 的 `Canceled` 事件、`IVeloxCommand`（一组 `CommandEventHandler` 事件） |
| 嵌套/开放的配置数据 | **嵌套字典属性袋** | 强类型 DTO | `IThemeObject` 的 `GetStaticThemeCache()`/`GetActiveThemeCache()`：`Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>>` 三层嵌套缓存 |
| 一份契约的输入输出**类型已知且稳定** | 具体类型入参 | `object` | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 全用具体类型 |
| 泛型参数 | 只在「同一个契约要服务 N 个平台的 N 套类型」时加，且参数名 `TPriorityCore` | 「以后可能有用」 | 7 家 `ISampler<TPriorityCore>` 是真需要：每家的 `TPriorityCore` 是自家的 UI 类型 |

**要从 Core 之外的命名空间取类型时**：先看 `architecture.md` §五的耦合表。当前允许的只有这几个仓内模块（`AI`/`MVVM`/`TimeLine`/`Threading`/`Lifetime`/`TransitionSystem.Abstractions`），且 `TransitionSystem.Abstractions` 那条（`ITransitionScheduler.cs` 与 `ITransitionInterpreter.cs` 的 `using VeloxDev.TransitionSystem.Abstractions` 分别引 `InterpolatorCore` 与 `SamplerSet<TPriorityCore>`）是**契约引用具体类**，属于已存在的不洁处，别再加新的。

**要和 Agent 框架对话的契约**：只在 `WorkflowSystem/` 下有先例（若干文件）。规则是**成对**标注（`AgentLanguages.Chinese` / `AgentLanguages.English`，一一对应）：

```csharp
[AgentContext(AgentLanguages.Chinese, "画布布局上下文，记录画布尺寸与偏移信息")]
[AgentContext(AgentLanguages.English, "Canvas layout context, recording canvas size and offset information")]
```

方法入参另标 `[AgentCommandParameter(typeof(Offset))]`（`IWorkflowNodeViewModel` 的 `MoveCommand`），参数类型不明显时可以裸写 `[AgentCommandParameter]`（`IWorkflowLinkViewModel` 的 `DeleteCommand`）。语义与读取规则在 `memory/modules/AI/`。

---

## 四、要不要做 `ITimeSourceControl` 式的读写拆分

**全仓只有一处**这么拆：`ITimeSource`（读）/ `ITimeSourceControl : ITimeSource`（写，`ITimeSourceControl.cs` 的声明）。理由写在它的 `<remarks>`。所以这不是默认姿势，是**有条件的**：

| 条件（都成立才拆） | `ITimeSourceControl` 的实况 |
|---|---|
| 同一个对象会**被交给不该写它的那一方**（订阅者、采样器、消费者） | 动画管线自己拿写侧（`Src/Core/VeloxDev.Core/TransitionSystem/Runtime/TransitionRun.cs` 的 `Timeline`），sampler 拿读侧（`ITimeSource` 的 remarks `This is the read-only face`） |
| 写操作是**少数几个**、可以穷举 | 写侧只加少数几个成员的规模（`Pause`/`Resume`/`SetRate`/`Seek`/`Wake`） |
| 拆开能让「只读方」的签名自证其意图 | `ITimeSampler` 拿的是只读面（`ITimeSource` 的 remarks `This is the read-only face`） |

**不成立就别拆**，仓内其余契约都没拆。判断口诀：**看有没有一个"永远不该 `Wake`/`Seek`"的接收方**；没有就一个接口到底。

**千万别这样拆**：不要为了「以后可能要只读」把每个契约都配一个 `Control` 双胞胎 —— `Control` 后缀在全仓**只出现过一次**，是强信号，滥用会让「哪个是该实现的」变成猜谜。

---

## 五、官方做法 vs 看着能编译、但是错的捷径

### 1. 新增平台能力：官方是改契约 + 七家补齐，不是让某一家 `dynamic`/反射绕开

- **官方**：契约进 `Interfaces/`，七家各自实现（规模不必相同：`ISampler` 七家都有，但 Razor 与 WinForms 只有极少数采样器文件，Avalonia 则有很多）。
- **错的捷径**：在 Core 里写「如果实现类型没实现 X 成员就跳过」的回退。**本仓明文拒绝**这种做法：`TimerCore.CreateTimeSource<TContract>()`（`Src/Core/VeloxDev.Core/Timing/TimerCore.cs` 的 `CreateTimeSource<TContract>`）按**精确契约类型**查注册表，XML 同方法 remarks 明说不做宽/窄回退 —— 拿不到就抛，而不是回退到别的契约。

### 2. 平台差异：官方是七家各写一份，不是 Core 里 `#if` 平台宏

`Interfaces/` 里唯一的条件编译是 `IAspectOriented.cs` 的 `#if NET`/`#endif`，那是**框架**差异（该接口与整个 AOP 运行期绑在一起，`netstandard2.0`/`net461` 上这个命名空间不存在），不是**平台 GUI** 差异。GUI 差异一律落在 `Src/Adapters/<平台>/`，所以契约里**不该**出现任何平台枚举、`RuntimeInformation`、`#if WINDOWS` 之类。

### 3. 别在契约里放「哪些平台支持」的知识

`IThemeValueConverter` 六家实现（Jalium 不接 DynamicTheme），`IWorkflowGridDecorator` 只有 Razor 与 WinForms 2 家把实现放进适配器本体，Avalonia/MAUI/WinUI/WPF/Jalium 5 家在模板/demo 里（Jalium 的模板直接实现接口，没有可继承基类）。契约不记录这些 —— 想确认某契约某平台到底有没有，只能按 `architecture.md` §三·B 的表去各家目录数。

### 4. 改契约名/挪命名空间 = 改一个**字符串常量**，编译器不会提醒你

生成器侧按硬编码全名匹配（`Theme.cs` 的 `IThemeFullName` 三条常量、`Writers/TickWriter.cs` 的 `GenerateBaseInterfaces`、`AopSurface.cs` 的 `interfaceDeclaration`、`Writers/CommandWriter.cs` 的 `CanUpcastTo`、`Writers/WorkflowWriter.cs` 的 `GetWorkflowInterfaceName`）。改名后 Core 编译通过、**生成器静默不生成**，症状是「类型上少了个属性/方法」，报错点离病因很远。所以：改名必须同时 `grep -n "<旧全名>" Src/Generators/`。

### 5. 异步成员：官方是「换行 + 取消参数放最后」，别自己造 `AsyncResult`

`IVeloxCommand` 的 `ExecuteAsync`/`LockAsync`/`UnlockAsync`/`ClearAsync`/`InterruptAsync`/`ContinueAsync`/`ChangeSemaphoreAsync` 等多个 `Task` 成员，取消参数的习惯是**放最后、有默认值就写 `= default`**（`ITimeSource.WaitWhileStalledAsync`、`ITransitionScheduler` 两个 `Execute` 重载的 `externCts`），也有明确不给默认值、要求调用方显式传的（WorkflowSystem 的 `ReceiveAsync`/`BroadcastAsync`/`ReverseBroadcastAsync`/`AccessAsync`，`IWorkflowNodeViewModelHelper`）。新增时**跟同子目录的邻居保持一致**，别发明第三种。

### 6. 别把实现细节写进契约的 XML

契约的 XML 注释应当只写「契约方约定」；本仓有反例（`ITransitionProperty` 的方法注释里描述具体实现的取值路径），照抄会把实现约束冻结成 API 文档。判断法：这条注释在**换一个实现方之后还成立吗**。

---

## 六、新增/改名/移动一个契约的联动清单

| # | 联动点 | 漏了会怎样 |
|---|---|---|
| 1 | `Src/Generators/VeloxDev.Core.Generator/` 里的**字符串全名**（`Theme.cs`、`AopSurface.cs`、`Writers/{TickWriter,CommandWriter,WorkflowWriter}.cs`） | 改名后静默不生成，症状远离病因 |
| 2 | 七家适配器 `Src/Adapters/<平台>/PlatformAdapters/`（`UIThreadInspector` / `Samplers/` / `ThemeValueConverters` / `Attached/Workflow/*`） | 编译失败（好情况）或漏一家（见 §五·1 的规模差异） |
| 3 | Core 侧的**默认实现**：`Src/Core/VeloxDev.Core/WorkflowSystem/Templates/ViewModels/*DefaultViewModel.cs`、`TransitionSystem/{Runtime/TransitionInterpreter,Runtime/TransitionScheduler,Effects/TransitionEffect,Binding/TransitionProperty}.cs` | 编译失败 |
| 4 | `Src/Templates/*/working/content/<契约名>/` 与 `Examples/`（`IWorkflowGridDecorator` 不在适配器本体的那几家在这里补；`IWorkflowMinimapOverlay` 现已七家全在适配器本体） | 该平台的示例/模板缺能力 |
| 5 | 若是注册表键：`TimerCore.RegisterTimeSource<…>` 的所有调用点（`Src/Core/VeloxDev.Core/Timing/TimerCore.cs` 定义，`TransitionSystem/Sampling/SamplerSet.cs` 的 `Run`、`TransitionSystem/Effects/Transition.cs` 的 `TimerCore.CreateTimeSource<ITimeSourceControl>()` 调用） | 运行期抛「未注册」，编译期无感 |
| 6 | `Tests`：`Src/Core/VeloxDev.Core.Test/` 下的手写实现（`DynamicTheme/ThemeTransitionTests.cs` 的 `Subject`、`TimeLine/TickableBusTests.cs` 的 `PushCounter` 是 `IThemeObject`/`ITickable` 仅存的手写实现） | 测试编译失败 |
| 7 | **不必**动：`Interfaces/` 内的目录结构（挪文件不改命名空间，见 `architecture.md` §一），也不影响任何 `using` | — |
