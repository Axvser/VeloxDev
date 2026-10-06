# VeloxDev.Core.Generator — 架构

> 代码：`Src/Generators/VeloxDev.Core.Generator/`。**29 个 .cs、11072 行**（`Writers/WorkflowWriter.cs` 1722、`Base/VeloxJsonModel.cs` 1564、`Base/AIContextModel.cs` 1152、`Writers/MVVMWriter.cs` 1090、`Base/Analizer.cs` 1024、`Writers/AIContextTreeWriter.cs` 852、`Writers/CommandWriter.cs` 737、`Writers/VeloxJsonCodeWriter.cs` 562、`Theme.cs` 423、`AopSurface.cs` 397、`Writers/WriterBase.cs` 273、`Diagnostics.cs` 250、`Writers/TickWriter.cs` 131、`Base/AIContextNaming.cs` 104、`Writers/AopWriter.cs` 96、`AIContextTree.cs` 90、`VeloxJson.cs` 87、`Base/RequiredMembers.cs` 72、`Base/AnalizeHelper.cs` 67、`Base/LanguageVersionGuard.cs` 56、`MVVM.cs` / `Command.cs` 各 47、`Workflow.cs` / `Tickable.cs` / `LanguageVersionCheck.cs` 各 39、`AopProxy.cs` 38、`Base/TypeParameterWalk.cs` 31、`Base/AopNames.cs` 26、`Base/ICodeWriter.cs` 17）。
> 打包成 NuGet 分析器包，不产出运行期程序集；`TargetFramework=netstandard2.0`（`VeloxDev.Core.Generator.csproj:6`）。

本文只写「读完这 29 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一个 Roslyn **增量**源生成器包：把「贴了某几个特性、且声明为 `partial` 的类」补写成能编译的完整实现。产物是**消费者项目里的 C# 源文本**（`AddSource`），不是 Core 的一部分，也不进任何 DLL。

它同时服务**七个消费模块**，一包一份实现：

| 消费模块 | 贴的特性 | 生成器 |
|---|---|---|
| WorkflowSystem | `WorkflowBuilder+TreeAttribute` / `+NodeAttribute` / `+SlotAttribute` / `+LinkAttribute` / `DefaultAnchorAttribute` / `DefaultSizeAttribute` | `Workflow.cs` |
| MVVM | `VeloxPropertyAttribute` / `VeloxCommandAttribute` | `MVVM.cs` + `Command.cs` |
| TimeLine | `TickableAttribute` | `Tickable.cs` |
| AspectOriented | `AspectOrientedAttribute` | `AopSurface.cs`（接口 + 代理实现）+ `AopProxy.cs`（扩展方法） |
| AI（AIContextTree，2026-10-03 起） | `AgentContextAttribute` 等三个 Agent 特性 | `AIContextTree.cs` —— **全程序集遍历，一次 `AddSource`**，不被任何特性触发，见 §三·四 |
| DynamicTheme | `ThemeConfigAttribute\`3..\`7`（5 个元数） | `Theme.cs` |
| Serialization（VeloxJson） | `ArchivableAttribute`（根之一；另一个根是「实现了工作流组件接口」）+ 成员级 `ArchiveAttribute` | `VeloxJson.cs` —— **全程序集遍历，一次 `AddSource`**，不被任何特性触发，见 §三·四 |

**这七家 GUI 适配器在本模块里是零代码 —— 这正是「契约不该重复七遍」的实例。** 适配器全都不做特性解析、不写生成逻辑，**源码里也一个生成器特性都不用**（`grep -rn 'VeloxProperty\|\[Velox' Src/Adapters/ --include=*.cs` 零命中），因此它们**一个都不引用这个分析器包** —— 七份 `Src/Adapters/*/*.csproj` 只引用 `VeloxDev.Core` 加各家自己的 GUI 包，而 §五 那张 12 对 `PackageReference`/`ProjectReference` 的表里**没有任何适配器**；`[VeloxProperty]` 在 WPF、Avalonia、WinUI、MAUI、WinForms、Razor、Jalium 上生成的东西**逐字相同**，因为生成器读的是符号语义，从不问平台（`Base/AnalizeHelper.cs` 全文没有平台概念，`VeloxDev.Core.Generator.csproj` 也没有任何 GUI 引用）。要改「生成的属性长什么样」，改这一处就同时改了七家；要改「某家在某个平台上怎么渲染」，与本模块无关 —— 那是 `memory/modules/<WorkflowSystem|TransitionSystem>/adapters/<平台>.md` 的事。**所以本模块没有 `adapters/` 子目录，也不该有。**

**不解决什么（这些边界常在别处被误以为在这里）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 生成出来的代码**跑起来是什么行为** | 全在 Core。生成器只写声明与转发（例如 `OnWorkflowSlotAdded` 的**声明**由 `Writers/MVVMWriter.cs` 写，`CreateWorkflowSlot<T>()` 的**骨架**也由它写，但生命周期归 `WorkflowSystem`） |
| 「哪些类会被处理」 | `Base/Analizer.cs:95-107` 那张**硬编码 10 条**的 `TriggerAttributes` 表。自定义特性、第三方特性一律不认 |
| 编译错误 / 诊断 | 生成器自己发十三类（`Diagnostics.cs`；MVVM 侧 2026-10-02 起 ID 统一成 `VELOX_MVVM_*`，Serialization 侧统一成 `VELOX_JSON_*`）：`VELOX_LANGVERSION001`（项目 LangVersion 低于生成代码所需，Warning）、`VELOX_MVVM_CMD001`（不支持的 `[VeloxCommand]` 签名，Error）、`VELOX_MVVM_PROP001`（`[VeloxProperty]` 声明冲突，Error）、`VELOX_MVVM_PROP002`（名字推不出合法成员，Warning）、`VELOX_MVVM_PROP003`（`[VeloxProperty]` 属性没写 `partial`，Warning）、`VELOX_AI_TREE001`（Agent 上下文树里同名同参重载只能暴露一个，Warning，category 是 `VeloxDev.AI`）、`VELOX_JSON_ARCH001`（`[Archivable]` 点名的类型无法收录，Error）、`VELOX_JSON_HOOK001`（同一类型上多个同名序列化回调，Warning）、`VELOX_JSON_MEMBER001`（`[Archive]` 声明无法兑现，Error）、`VELOX_JSON_MEMBER002`（`[Archive(KeepField)]` 与旁边的属性冲突，Warning）、`VELOX_JSON_HOOK002`（序列化回调生成代码够不着或签名不可调，Error）、`VELOX_JSON_GENERIC001`（类型参数的约束解析不出，Warning）、`VELOX_JSON_INCLUDE001`（被闭包收进归档格式的类型，Info）。另有 MSBuild 侧的 `VELOXCFG0001`：`VeloxDev.Core.Generator.targets:16-19` |
| 依赖注入、服务定位、注册表 | 完全不生成。生成的是「这个类自己怎么把自己装起来」，不是容器配置 |
| 平台差异 | 零。见上 |
| 版本与发布 | 见 `extension.md` §四「改这里的代价」 |
| AOT / 裁剪元数据 | **本模块不产出任何裁剪元数据**。裁剪是 MSBuild 属性与 TFM 的事，见 §六 |

---

## 二、三个阶段的边界：筛选 → 解析 → 写

除 `Theme.cs`、`AIContextTree.cs`、`VeloxJson.cs` 外，6 个生成器的 `Initialize` 是同一行形状（`MVVM.cs:18-20`、`AopProxy.cs:18-20`、`AopSurface.cs:36-38`、`Command.cs:18-20`、`Tickable.cs:18-20`、`Workflow.cs:18-20`）：

```csharp
context.RegisterSourceOutput(
    Analizer.Filters.Targets(context).Combine(context.CompilationProvider),
    GenerateSource);
```

### 阶段 1：筛选 —— `Base/Analizer.cs:120` `Targets()`

- 对 `TriggerAttributes` 的 **10 条**（`:95-107`）逐条 `ForAttributeWithMetadataName`（`:127`），各 `.Collect()`，再 `.Combine().Select(AddRange)` 折成**一条**流（`:138-142`），末尾 `Deduplicate`（定义 `:200`，调用 `:142`）。
- 候选资格另有一道闸：`IsCandidateClass`（`:170-174`）**要求类是 `partial`**。非 `partial` 的类即使贴了特性也不会进入流程，且不报错。
- 特性是**按符号**解析而非按名字匹配的（`:113-119` 的 remarks），所以全限定写法与 `using` 别名都认。

### 阶段 2：解析 —— `Base/Analizer.cs:155` `Resolve()`

**中间这一步是本模块最容易被误解的地方，也是改它的人最容易改坏的地方。**

`GeneratorTarget`（`:43-87`）是 `readonly struct`，**刻意不持有 `ISymbol`**，只持三样：`ClassDeclarationSyntax Syntax`、`string TypeKey`（类型的全限定名）、`bool IsClassLevelAttribute`。理由写在它的 remarks（`:29-42`）：

> 缓存的 transform 结果里若留着 symbol，等到输出阶段它指向的是**陈旧的 `Compilation`**；而每个 writer 都要读目标文件之外的语义（基类链、引用程序集），改别的文件之后就会**静默**生成错的代码。

所以 symbol 一律在 `Resolve` 里对着**当前** `Compilation` 现取（`:165`），树已经离开编译的目标直接跳过（`:161`）。

**推论（照抄会付出代价的两条）：**
- 给某个 writer 加「读更多语义」的逻辑是**安全**的 —— 它本来就拿到的是新鲜 symbol。
- 给 `GeneratorTarget` 加 symbol 字段是**不安全**的，且不会立刻报错，只会在增量场景下偶发错码。要加信息就加 `TypeKey` 这类字符串。

`Deduplicate`（`:200`）**按 `TypeKey` 去重，不按 symbol 去重**：`remarks`（`:194-199`）说明按 symbol 去重会让同一个类型在两条缓存条目持不同 `Compilation` 的 symbol 时进来两次，第二次 `AddSource` 会因为 hint name 重复被拒。一个类拆成多个 partial、每个 partial 各贴一个触发特性时，只有**一个代表**进入 writer；谁当代表由 `IsClassLevelAttribute` 决定（类级特性优先，`:210`）。**AOP 这一侧现在已经不受它影响**：`AopSurface.cs:68` 与 `Writers/AopWriter.cs:23` 都走符号（`AnalizeHelper.Members` / `IsAopClass`），代表是哪份声明只决定产物的**文件名**。`TickWriter` 同理走符号（`Writers/TickWriter.cs:20`）—— 所以「代表」当前影响的是字段名与文件名这类表面，别再照抄旧记忆里「三个生成器都按声明读特性」的说法。

### 阶段 3：写 —— 各生成器的 `GenerateSource`

固定四步：`new XxxWriter()` → `Initialize(syntax, symbol)` → `CanWrite()` 闸门 → 写。

闸门是**静默**的：`CanWrite()` 返回 false 就不 `AddSource`，没有诊断、没有空文件。`Writers/MVVMWriter.cs:989`、`Writers/CommandWriter.cs:612`、`Writers/TickWriter.cs:54`、`Writers/AopWriter.cs:27` 的 `CanWrite()` 都是符号判定。

`Theme.cs` 是**第一个**不走 `Targets/Resolve` 的（它自己建 5 条流，`:33-55`，按 `ThemeConfigAttribute` 的 5 个元数分别订阅），**且只对 `partial` 类发**（`:113-118`），没有可用属性注册时返回 `string.Empty`（`:262-265`）—— 同样是静默无输出。后来 `AIContextTree.cs` 与 `VeloxJson.cs` 也各自走全程序集（见下），所以「不走 `Targets`」不是 `Theme.cs` 独有的。

#### 三·四、`AIContextTree.cs` 与 `VeloxJson.cs`：另两个不走 `Targets` 的生成器

它订阅 `CompilationProvider`，自己走一遍**整个程序集**（`Base/AIContextModel.cs` 的 `EnumerateTypes`），一次 `AddSource` 出全部产物。理由有三处是独立的：`Targets` 的 `IsCandidateClass` 只接受 `ClassDeclarationSyntax` 且要求 `partial`，**接口与枚举根本进不来**；它的触发集 `TriggerAttributes` 不含三个 Agent 特性；而「是不是组件」由**实现了哪个接口**决定（`IWorkflowNode{Trees,Slots,Links}ViewModel` 四选一），特性触发表达不了。

两条守卫：`AIContextModelBuilder.Applies` 要求编译单元里有 `VeloxDev.AI.AgentContextAttribute`，否则连遍历都不做；MSBuild 属性 `VeloxAgentContextTree=false` 可整体关闭。**根由 `VeloxAgentContextTreeRoot` 决定**（Core 自己设成 `Framework`，其余默认 `Customer`）—— 这个属性必须在消费工程的 `<ItemGroup>` 里用 **`<CompilerVisibleProperty Include="…" />`** 声明，否则分析器读到的永远是默认值（MSBuild 属性默认不透给分析器）。

产物一份文件里两半：一个 `{程序集名}_AIContextFragment`（只含数据的目录，逐目录一个 `Lazy`）与每组 `{程序集名}_Accessor{i}`（`switch` 加转型，无反射），末尾一个 `[ModuleInitializer]` 自注册（`Writers/AIContextTreeWriter.cs:166`、`:416`、`:815`）。**它复现 `MVVMWriter` 与 `CommandWriter` 的命名规则**（见 `Base/AIContextNaming.cs`）—— 生成器之间看不见彼此的产物，只能复现规则。

**`VeloxJson.cs` 是第二个全程序集生成器**（归档序列化）。它同样订阅 `CompilationProvider`、检查 `VeloxJsonModelBuilder.Applies`（要求编译单元里有 `VeloxPropertyAttribute`，否则不遍历），受 MSBuild 属性 `VeloxJsonSerialization=false` 关闭。与另外两个不同的是它从**根类型**出发沿成员的声明类型做**传递闭包**（`Base/VeloxJsonModel.cs` 的 `Build`）：根是「实现了工作流组件接口的类型」、贴了 `[Archivable]` 的类型、带 `[WorkflowBuilder.*]` 的类型、或带 `[VeloxProperty]` 字段的类型；`[Archivable(typeof(A), typeof(B))]` 还能把别的类型点名成额外根（可链式，去重靠 `included`）。**2026-10-04 起这条闭包比「沿成员声明类型」宽三条**：向下展开派生类（预建的祖先索引 `BuildFamilyIndex`）、交出字典的键类型（`Reachable` 现在也 yield `KeyType`）、沿根形状开放泛型上的类型参数约束收其约束一族。收录的**全量清单**写在生成文件的文件头注释里，构建期只报两类：`VELOX_JSON_INCLUDE001`（Info，只报没有声明点名过的那些 —— 按 TFM 各跑一次，逐条报会变成 `TFM 数 × 类型数`）与 `VELOX_JSON_GENERIC001`（Warning，解析不出的约束）—— 细节见 `memory/modules/Serialization/architecture.md` §一。成员层面由 `[Archive(ArchiveOptions)]` 放行/改名/排除单个成员；生命周期钩子认 BCL 那四个特性（`[OnSerializing]` 等），不认自有名字。

**发工厂调用的两个生成器都要处理 `required` 成员**：`new T()` 在类型有 required 成员时**编不过**，必须带上对象初始化器。判定与初始化器在 `Base/RequiredMembers.cs`（判据是编译器 API `IsRequired`，**不是** `[RequiredMember]` 特性 —— 后者是 emit 阶段合成的，源码符号上看不到；为此 `Microsoft.CodeAnalysis.CSharp` 从 4.3.1 抬到 4.8.0）。VeloxJson 与 AIContextTree 曾经各有一个这样的洞。**诊断由调用方持有而非挂在返回值上** —— `Build` 返回 `null`（这个程序集什么也没产出）恰恰常是「被拒的声明」导致的，把 notices 放进返回值会在最需要它的时候丢掉（`VeloxJson.cs` 先报诊断再判空）。这一模块的三条实现约束记在 `memory/modules/Serialization/pitfalls.md` §七。产物每个程序集一份 `{程序集名}_VeloxJson.g.cs`，命名空间写死 `VeloxDev.Serialization.Generated`，逐个类型出一个 `{程序集名}_JsonWriter{i}` / `{程序集名}_JsonReader{i}`，末尾 `Register()` 把读写器与容器工厂登记进 `VeloxDev.Serialization.VeloxJsonRegistry`（`Writers/VeloxJsonCodeWriter.cs:290-299`）。

### 产物命名（hint name = 文件名）

| 生成器 | 文件名模板 | 依据 |
|---|---|---|
| AOP 接口 | `{类}_{命名空间下划线}_Aop.g.cs` | `Base/AopNames.cs:15`（唯一算法）；`AopSurface.cs:163-166` 的 `AddSource` |
| AOP 代理实现（**与接口同一次遍历产出**） | `{同一个接口名}Proxy.g.cs` | `Base/AopNames.cs:18`；`AopSurface.cs:167-172` 的 `AddSource` |
| AOP 扩展方法 | `{类}_{命名空间下划线}_AopExt.g.cs` | `Writers/AopWriter.cs:54`；`AopProxy.cs:33` |
| AI 上下文树（**每个程序集一份**，不是每个类一份） | `{程序集名}_AIContextTree.g.cs` | `AIContextTree.cs:74-75` 的 `AddSource` |
| VeloxJson 归档序列化（**每个程序集一份**） | `{程序集名}_VeloxJson.g.cs` | `VeloxJson.cs:66-67` 的 `AddSource` |
| Command | `{类}_{命名空间下划线}_Commands.g.cs` | `Writers/CommandWriter.cs:615-623` |
| MVVM | `{类}_{命名空间下划线\|Global}_MVVM.g.cs` | `Writers/MVVMWriter.cs:992-995` |
| Mono | `{类}_{命名空间下划线}_Tick.g.cs` | `Writers/TickWriter.cs:57-64` |
| Theme | `{类}_{命名空间下划线}_ThemeConfig.g.cs` | `Theme.cs:100` |
| Workflow | `{类}.g.cs`（不带命名空间段） | `Writers/WorkflowWriter.cs:67-69` |

**全局命名空间的兜底现在统一在 `Writers/WriterBase.cs:40-43` 的 `NamespaceFileSegment()` 一处**：全局命名空间返回字面量 `"Global"`，真实命名空间返回 `Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')`。`MVVMWriter`/`CommandWriter`/`TickWriter`/`AopWriter` 都调它，`WorkflowWriter` 干脆只用符号名（`Symbol?.Name + ".g.cs"`）。AOP 的接口名/代理名另走 `Base/AopNames.cs` 的同一算法。见 §六·1。

---

## 三、`Writers/WriterBase.cs`：所有 writer 的公共骨架

`Write()`（`:90`）的固定次序：`// <auto-generated>` → `#pragma warning disable` → `#nullable enable`（`:105-107`）→ namespace（`AppendNamespace()`，`:78-89`）→ 外层 partial 类（嵌套类才走，`:117-...`）→ 当前类声明（修饰符 + 名字 + 类型参数 + 基类型 + 约束）→ `GenerateBody()` → 补外层大括号。

**子类只有五个扩展点**（`Base/ICodeWriter.cs:8-15` 定接口，`Writers/WriterBase.cs:264-272` 定抽象）：`CanWrite` / `GetFileName` / `GenerateBaseTypes` / `GenerateBaseInterfaces` / `GenerateBody`。

两个不显眼但关键的机制：

1. **基础类型列表是「用户原有 + 生成器要加」去重后的并集**。`GetBaseTypes()`（`:178-215`）先放用户原本的接口（`:186-199`）和基类（`:200-208`，放进列表头，使基类排在接口前），再 `AddRange(GenerateBaseTypes())` 与 `AddRange(GenerateBaseInterfaces())`（`:211-212`），最后 `.Distinct()`。**所以「生成的类继承了谁」有两处来源，漏改一处会出现重复基类型或丢接口。**
2. **`FormatModifiers`（`:234-261`）把 `partial` 永远排到修饰符最后**。生成的声明必须与原声明修饰符集合一致，否则会重复定义。要加新修饰符就改这里，别在模板字符串里手写。

**AOP 的产物在当前代码里由两个生成器分工。** `Writers/AopWriter.cs` 只写**扩展方法**一份：`GetExtensionFileName()`（`:51`）→ `WriteExtension()`（`:58`），出 `namespace VeloxDev.AspectOriented;` 下的 `Aop()` 扩展方法；它的 `GenerateBody()` 返回 `string.Empty`（`:46`），`CanWrite()` 仅作闸门（`:27`）。真正的**接口与代理实现**由 `AopSurface.cs` 在**同一次遍历**里各 `AddSource` 一次（`AopSurface.cs:163` 接口、`:167` 代理）—— 两个产物同源，签名天生一致。扩展方法文件名**不在** `ICodeWriter` 接口里，是 `AopProxy.cs` 直接调的（`:34`）——**给别的生成器加第二份产物时，接口里没有位置，得照这里的写法加一个自己的方法。**

---

## 四、writer 的分工与它们之间的耦合

| writer | 职责 | 它读的配置 | 需要注意 |
|---|---|---|---|
| `Writers/WorkflowWriter.cs`（1705 行，最大） | `TreeAttribute/NodeAttribute/SlotAttribute/LinkAttribute` 四种模型；`WorkflowType` 1..4 分派；另支持一个**没贴特性、只重声明节点默认值**的子类路径 | 四个特性的构造参数 | 节点布局/尺寸/`RuntimeId` 的初始化代码都在这里 |
| `Writers/MVVMWriter.cs`（1090 行） | 属性的 setter 体、通知事件、集合订阅、Workflow 槽位生命周期 | `VeloxPropertyAttribute` + `DetectSetterMode()`（`:55`）探测基类 | 见下 |
| `Writers/CommandWriter.cs`（754 行） | 懒建 `IVeloxCommand` 属性 + 可选 `CanExecute{名}Command` partial 钩子 | `VeloxCommandAttribute`，**位置参数先读、具名参数覆盖**；名字为 `"Auto"` 时取方法名去掉 `Async` | 构造选择 `BuildSpec` + `CommandConstruction` 枚举（`:33`、`:206`），转换 thunk 的构造已并入其中 |

**返回类型决定「值怎么变成 `Task`」，形参决定「走哪个构造入口」，两件事分开判**（2026-10-01 起）：

| 返回类型 | 生成物 | 说明 |
|---|---|---|
| `Task` / `Task<T>` | 方法组本身 | `Task<T>` 靠**委托协变**落进 `Func<..., Task>`，`T` 被包装 lambda 丢弃 |
| `void` | 方法组本身 | 1 参绑 `Action<object?>`、0 参绑 `Action` |
| `ValueTask` / `ValueTask<T>` | **`.AsTask()` 转换 thunk** | 见下 |

`ValueTask` 既不能隐式转 `Task`，也不像 `Task<T>` 那样能靠协变（协变要求返回类型之间本身有引用转换，而 `ValueTask` 是结构体），所以**必须显式 thunk**。

### 不支持的形态发 `VELOX_MVVM_CMD001`（2026-10-01；2026-10-02 由 `VELOXCMD001` 改名）

判定失败时**报诊断并跳过该方法**，不再让它落进产物：

- 报的是 `VELOX_MVVM_CMD001`（Error），位置是**用户那一行**，消息里点名方法并说明改法。
- 跳过是因为产物**注定编不过** —— 再冒一个 CS1503 只会把真正的错误埋掉。全部方法都被拒时 `CanWrite()` 为假，**整个文件都不生成**。
- 这与之前的行为差别很大：以前报的是生成文件里的 `CS1503 无法从"方法组"转换…`，作者看到的是一个自己没写过的方法组和构造签名。

被拒的四种（消息里的措辞就是「该怎么改」）：**类型参数不出现在参数类型里的**泛型方法、返回类型不认识、前导形参多于 **15 个**、`void` 带 `CancellationToken`。2026-10-02 起又加了两条同 ID 的理由：情形 2 的泛型方法撞上「接口已声明非强类型命令属性」（只能给方法、接口要属性，无法退让）、以及 `{名}Command` 已被同名成员占用（此前会静默产出 CS0102）。

`DiagnosticDescriptor` 在 `Diagnostics.cs`；`CommandWriter.Diagnostics` 收集，`Command.cs` 用 `context.ReportDiagnostic` 报出去。**这是本仓库第一个 Roslyn 诊断**（此前只有 `.targets` 里的 `VELOXCFG0001` 那条 MSBuild 警告）。

**形参这一维**（2026-10-01 起，后由 arity 族扩展）：前导形参支持 **0..15 个**（`Writers/CommandWriter.cs:180` 的 `MaxLeadingParameters = 15`），末尾可再跟一个 `CancellationToken`；多于 15 个报 `VELOX_MVVM_CMD001`。生成物按 `isTyped`（`Writers/CommandWriter.cs:284`）分两路：

| 前导形参 | 生成物 |
|---|---|
| 0 个 | 方法组原样落地（`Func<Task>` / `Action`），或 `() => Foo().AsTask()` thunk |
| 1 个 `object?` | 方法组原样落地（非强类型），或 `parameter => Foo(parameter).AsTask()` thunk |
| 1 个其它类型 `T` | 强类型属性 `IVeloxCommand<P>`；lambda 形参直接用 `value`，**代码里不再强转** |
| ≥2 个（全强类型） | arity 族 `VeloxCommand<...>` 属性（facade 与管线之间按 `ValueTuple` 打包），产物走结果通道 |

**多形参走 arity 族**（`Src/Core/VeloxDev.Core/MVVM/CommandArities.cs`）：调用方传的是实参，打包成元组藏在 facade 之后，公开面保持无元组。多形参、无返回值时只能包一层 `async` lambda 从结果入口走（arity 族没有无返回体的公开入口，见 `Writers/CommandWriter.cs:347-360`）。

**强转是运行期的**：传错类型不会静默，但也不是编译错误 —— 它变成一次 `Failed` 执行，`Exception` 是 `InvalidCastException`。机制已从「生成 lambda 里显式强转」变成「强类型属性派生自非强类型 `IVeloxCommand`，基接口的 `object?` 重载始终可达」。`ATypedParameterOfTheWrongType_FailsTheExecutionInsteadOfSilentlyDoingNothing`（`Src/Core/VeloxDev.Core.Test/MVVM/CommandSignatureTests.cs:243`）钉住了这个代价。

**thunk 的形参个数同时决定绑到哪个构造入口** —— 让四种形态与 `Task` 那四种**一一对称**（下表是**非强类型**那一路的 ValueTask thunk；强类型那一路走 `Typed*` / `TypedResult*` 构造）：

| 形参 | thunk | 构造入口 | `_isCtsNeeded` |
|---|---|---|---|
| `()` | `() => Foo().AsTask()` | `UntypedMainCtor` | false |
| `(object?)` | `parameter => Foo(parameter).AsTask()` | `UntypedParameterOnlyFactory` | false |
| `(CancellationToken)` | `ct => Foo(ct).AsTask()` | `UntypedTokenOnlyFactory` | **true** |
| `(object?, CancellationToken)` | `(parameter, ct) => Foo(parameter, ct).AsTask()` | `UntypedMainCtor` | **true** |

前两行是单参 lambda、后一行是双参，**这个差别是刻意的**：早先 1 参一律发双参 lambda，只能绑主构造，于是 `ValueTask (object?)` 每次执行白建一个命令体根本看不到的 `CancellationTokenSource`（72 B），与 `Task (object?)` 不一致。`AParameterOnlyBody_GetsNoCancellationTokenSource_ForEitherReturnType`（`CommandSignatureTests.cs:78`）钉住了这个对称性。

- thunk 产出 `Func<object?, CancellationToken, Task>` —— **这个签名在四个 TFM 上都存在**，所以生成代码不依赖运行时的 ValueTask 入口，`netstandard2.0` / `net461` 的生成目标照样编得过。**不要**给生成器加 TFM 感知或 MSBuild 属性管线，那是多余的。
- **不要**把 `Foo(...)` 提到 lambda 外面再 `AsTask()`：`IValueTaskSource` 只能消费一次，第二次执行会抛 `InvalidOperationException`。
- 零参形态 `() => Foo().AsTask()` **实测无二义性**（红队曾断言它会 CS0121，**是错的**）：它绑到 `Func<Task>`，命令体确实被 await，且拿到 `_isCtsNeeded = false`。

### 全局命名空间：`ContainingNamespace` 有两个陷阱

`Symbol.ContainingNamespace.ToDisplayString()` 在全局命名空间下返回的是字面量 `"<global namespace>"` —— 那个尖括号既是**非法文件名字符**也是**非法标识符字符**。2026-10-01 之前有**两处**会因此炸，而且报错都指向别处：

1. **文件名**（`GetFileName`）—— 拼进 hintName 会让生成器整个抛 `ArgumentException`，宿主只报一句 `CS8785 生成器"Command"未能生成源`，跟命名空间毫不相干。`WriterBase.NamespaceFileSegment()` 统一兜底成 `"Global"`；**五个调用点**（`CommandWriter`/`MVVMWriter`/`AopWriter` ×2/`TickWriter`）都改用它。`MVVMWriter` 原先自己处理过，现在是同一份。
2. **生成文件内容**（`WriterBase.Write`）—— 无条件写 `namespace {ContainingNamespace};`，全局命名空间下产出 `namespace <global namespace>;`，**非法语法，产物编不过**。`WriterBase.AppendNamespace()` 在全局命名空间时什么都不写。

AOP 还有第三处：接口与代理实现的**类型名**里也拼命名空间片段 —— 那两处不走 `NamespaceFileSegment()`，走 `Base/AopNames.cs:21-24` 的 `Segment()`（算法等价，同样把全局命名空间写成 `"Global"`）。代理类必须与接口**同名同命名空间**，所以 AOP 这套名字只能有一个算法（`AopNames.InterfaceFor` / `ProxyFor`，`:15-19`）。

回归守卫：`Src/Core/VeloxDev.Core.Test/MVVM/GlobalNamespaceCommandViewModel.cs` 故意不写命名空间，它一存在，上面两处任何一处回退都会让构建立刻失败。

### 外层类必须原样带上类型形参

`WriterBase.Write()` 生成嵌套类时会重写外层类声明。2026-10-01 之前它只写 `class {标识符}` —— **把 `<T>` 丢了**。`partial class Outer` 与 `partial class Outer<T>` 是两个 arity 不同的类型，不会合并；编译器另造一个空的 `Outer`，于是内层类的方法全成了「当前上下文中不存在该名称」（**CS0103**），报错指向生成文件，极难反推。现在由 `OuterClassHeader()` 统一带上 `TypeParameterList` 与 `ConstraintClauses`。

**泛型类本身与普通嵌套类都没问题**（实测已可用），坏的只有「泛型外类 + 嵌套类」这一个组合。

**泛型方法只在「类型参数不出现在参数类型里」时才拒绝**（2026-10-02 放宽）：那种情况下生成的方法组 `Foo` 无法从 `(object?, CancellationToken)` 推断出 `T`（实测 CS0411 + CS0029）。`M<T>(T value)` 这类现在**支持** —— 生成的访问器 `Get{名}Command<T>()` 自带类型参数，见 [MVVM 架构](../../MVVM/architecture.md) §六。
| `Writers/TickWriter.cs` | `InitializeTickable` / `CloseTickable` / 5 个 `partial void` 钩子 | `TickableAttribute` 的 `(channel, fps)` | **只实现、不调用** —— 只贴特性而不调 `InitializeTickable()` 等于什么都没发生 |
| `Writers/AopWriter.cs` | 只出 `Aop()` 扩展方法（接口与代理在 `AopSurface.cs`） | — | 见 §三 |
| `AopSurface.cs` | AOP 接口**与代理实现**（`VeloxDev.AopInterfaces` 命名空间） | — | 它**不在 `Writers/` 下**（`Theme.cs`、`AIContextTree.cs`、`VeloxJson.cs` 也把生成逻辑写在生成器类里）；两个产物从同一次遍历渲染，所以接口与实现的签名不会漂移 |
| `Theme.cs` | `IThemeObject` 实现、主题缓存、`SetThemeValue<T>` 一族 | 5 个 `ThemeConfigAttribute` 元数 | 只对 `partial` 类发 |

**两处会咬人的耦合：**

1. **`TriggerAttributes` 是多个消费模块共用的同一张表。** 加一条会影响**所有**生成器对「哪些类进入流程」的判断；`MVVM.cs` 与 `Command.cs` 对同一个类**都**会产出（各自再靠 `CanWrite()` 过滤），所以给 MVVM 加特性时两个生成器都会被喂到。这是本仓库最容易漏的联动点，见 `extension.md` §三·1。
2. **`Writers/MVVMWriter.cs` 的 `IsWorkflowComponent`（`:15`，初始化于 `:46`）让 MVVM 生成器为 Workflow 类补 `CreateWorkflowSlot<T>()` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved` 三件套**（`:1040-1065`）。也就是说**这条契约的生成侧在 MVVM writer 里，而语义侧在 WorkflowSystem** —— 找「工作流槽位生命周期为什么长这样」不能只看 `Writers/WorkflowWriter.cs`。

---

## 五、引用这个包：Debug/Release 双轨

`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:41-42` 把规则写成了注释：

> Debug 用本地生成器源码，Release 用包。analyzer 不随 ProjectReference 传递，所以用到生成器特性的项目都要各自重复这一对。

配对形状（`Debug` 走 `ProjectReference` + `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`，非 Debug 走 `PackageReference`）：

| 项目 | ProjectReference | PackageReference |
|---|---|---|
| `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` | `:44` | `:48` |
| `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj` | `:30` | `:31` |
| `Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj` | `:29` | `:33` |
| `Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj`（**不分 Debug/Release**，见下） | `:28` | 无（只钉 `Microsoft.CodeAnalysis.CSharp` `:31`） |
| `Examples/Workflow/Directory.Build.props`（整棵树一次） | `:6` | `:10` |
| `Examples/Theme/Directory.Build.props`（整棵树一次） | `:6` | `:10` |
| `Examples/AOP/WPF/Demo/Demo.csproj` | `:17` | `:21` |
| `Examples/AOP/Avalonia/Demo/Demo.csproj` | `:35` | `:39` |
| `Examples/MVVM/WPF/Demo/Demo.csproj` | `:19` | `:23` |
| `Examples/MVVM/Avalonia/Demo/Demo.csproj` | `:37` | `:41` |
| `Examples/MVVM/Common/Lib/Lib.csproj` | `:19` | `:23` |
| `Examples/Tickable/WPF/Demo/Demo.csproj` | `:17` | `:21` |

**共 12 处 `ProjectReference`、11 处 `PackageReference`。** 包自己的 `<Version>` 是 `10.0.0`（`VeloxDev.Core.Generator.csproj:11`）；11 处引用已全部对齐 `10.0.0`（含两个 `Examples/*/Directory.Build.props:10`）。版本落差的历史与「要不要补齐」的判断见 [extension.md](extension.md) §四。

`VeloxDev.Core.Test.csproj` 是**唯一不分 Debug/Release** 的一处（`Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj:23-31` 的注释）：它测的对象就是生成器源码本身，测一个已发布的快照等于没测，所以无条件走 `ProjectReference`，且因为要直接驱动 `Command` 生成器断言诊断，**保留程序集引用**（`OutputItemType="Analyzer"`，`ReferenceOutputAssembly` 默认 true）。

**「两者必须互斥」是被注释明确写下的硬约束**（`Examples/Workflow/Directory.Build.props:4`）：两条同时生效会**生成器执行两次、报重复成员**。另外 `Examples/Workflow/Directory.Build.props:5` 提醒路径基准是导入方项目目录，必须走 `MSBuildThisFileDirectory`。

---

## 六、陷阱（带依据）

1. **全局命名空间过去会生成出非法产物；现在有两条统一的守卫，走错一条等于没有。**
   - **文件名段**：所有 writer 都走 `Writers/WriterBase.cs:40-43` 的 `NamespaceFileSegment()`（全局命名空间返回 `Global`）。
   - **`namespace` 声明那一句**另有守卫（`Writers/WriterBase.cs:78-89` 的 `AppendNamespace()`）：全局命名空间时整个 namespace 块不写。
   - **AOP 不走上面两条**，走 `Base/AopNames.cs` 的 `Segment`（`:21-24`，同样返回 `Global`）—— 因为代理实现类必须与接口**同名同命名空间**，这两处名字只能有一个算法。
   新增一个产物名字时，挑一种走，**不要再手写 `ToDisplayString().Replace('.', '_')`**：`INamespaceSymbol.ToDisplayString()` 对全局命名空间返回字面量 `"<global namespace>"`，尖括号与空格都是非法的标识符与文件名。
2. **特性名拼错、类忘了写 `partial`（`Base/Analizer.cs:170-174` 的 `IsCandidateClass`）、`CanWrite()` 为 false、`Theme.cs` 没注册属性 —— 这四种情况仍然是「编译通过、什么都没生成」，不报错。** 注意生成器**确实会发诊断**（见 §一、§四），但只对「已进入流程却被拒的声明」（不支持的 `[VeloxCommand]` 签名、冲突的 `[VeloxProperty]`、Agent 树的歧义重载）；`TriggerAttributes` 之外的东西、没写 `partial` 的类根本进不了流程，也就没有诊断可发。排查时先看 `obj/<配置>/<TFM>/generated/...` 下有没有产物，别指望错误列表。
3. **`TriggerAttributes` 只有 10 条且硬编码**（`Base/Analizer.cs:95-107`）。新特性不进去，生成器对该类型**完全无感且不报错**。
4. **`AopSurface.cs` 一个生成器连着两次 `AddSource`**（`:163` 接口、`:167` 代理）；`AopProxy.cs` 只一次（`:33`）。加第三份产物必须自己保证 hint name 不撞。
5. **`Theme.cs` 只对 `partial` 类发**（`:113-118`），且无属性注册时返回空串（`:262-265`）。
6. **`Writers/WriterBase.cs:234-261` 的修饰符重排是「不报重复定义」的依赖**，不是格式化洁癖。
7. **`Generators.AgentCatalog` 在当前源码里不存在，`obj/` 下的陈旧产物也已复核不到。** 当前源码 29 个 `.cs` 无任何 AgentCatalog，`Src/Core/VeloxDev.Core/obj/Debug/net10.0/generated/VeloxDev.Core.Generator/` 下也不再留着那份 `VeloxAgentCatalog.g.cs`。别再按旧记忆去找它。
8. **裁剪/AOT 元数据与本模块无关。** 全部 writer 都不产出 `IsTrimmable` / `IsAotCompatible` / trim 注解；引擎侧也没有生成任何 `DynamicDependency` 之类的裁剪提示（全源 grep 无命中）。裁剪这条轴的开关在 csproj 与 MSBuild 属性上，见 §七。

9. **「另一个生成器加上的接口」要在每个地方各自兜底，漏一处就是一整条功能坏掉。** 这是本模块最容易复发的坑，因为它**不报错**：`[WorkflowBuilder.Slot<T>]` / `Node<T>` / `Link<T>` 类型的 `IWorkflow*ViewModel` 是 Workflow 生成器在**同一编译趟**注入的，而另一个生成器扫 `AllInterfaces` 时看不见它 —— 生成器之间看不见彼此的产物。所以凡是「这个类型算不算组件/槽」的判断，都不能只查接口，要**同时认作者写下的那个特性**。已有的三处：

   | 判据 | 在哪 | 认什么 |
   |---|---|---|
   | `RootReason` | `Base/VeloxJsonModel.cs:588` | `[WorkflowBuilder.*]`（任一）—— 决定闭世界的根 |
   | 槽类型兜底 | `Writers/WorkflowWriter.cs:1531` | 沿基类链找 `[WorkflowBuilder.SlotAttribute]` |
   | `ComponentKindOf` | `Base/AIContextModel.cs` | 四个特性 → 四个目录段（2026-10-05 补上） |
   | `IsSingleSlotType` | 同上，经 `WorkflowBuilderComponentKind == "Slots"` | 同上（2026-10-05 补上） |

   后两处共用 `AIContextModelBuilder.WorkflowBuilderComponentKind`，**别再各写一份** —— 它们本来就是同一条规则。

   **这一轮补的是后两处，代价各不相同。** `IsSingleSlotType` 漏最久：消费方声明的槽属性拿不到 `AIContextFlags.IsSingleSlot`，于是 `ListSlotProperties` 不列它、`BuildSlotPropertyMap` 不认它（按属性名解析的连接工具全部报错）、`ComponentPatcher` 也不拒绝对它直接赋值。`ComponentKindOf` 漏的是**整类组件**：只写了 `[WorkflowBuilder.Node<T>]` 的节点类型不进目录，`CreateNode` / `GetTypeSchema` 对它一律答「不在目录里」。详见 [`AI/architecture.md`](../AI/architecture.md) §七·五、§七·六 与 [`WorkflowSystem/architecture.md`](../WorkflowSystem/architecture.md)。

   ⚠ 补 `ComponentKindOf` 会**加宽消费方的目录**（此前只有带标注的组件才进），这是要的方向，但 prompt 体积会涨；回退点就是那一行。

   写这条判据时**按包含类型判、不按名字前缀**：`WorkflowBuilder.Slot<T>` 是泛型嵌套特性，`ToDisplayString` 把嵌套类型渲染成 `.` 而不是元数据里的 `+`，前缀匹配永远匹配不上。**新增这类判据时先 grep 上面这张表**，照着已有那处的写法写。

---

## 七、验证线在哪（以及它不在哪）

**裁剪/AOT 的验证探针不在本仓库。** `Src/Verification/VeloxDev.TrimProbe/` 在工作区里**只剩 `bin/` 与 `obj/`**：没有 `.cs`、没有 `.csproj`，也没有被跟踪文件（`git ls-files` 对它零命中）；`VeloxDev.slnx` 里没有 `TrimProbe`。（注意 `Src/Verification/` 这一层另有 5 个被跟踪的 `.ps1` 脚本 —— `agent-ui-harness.ps1`、`agent-web-harness.ps1`、`verify-jalium-item-templates.ps1`、`verify-workflow-item-templates.ps1`、`verify-workflow-item-templates-all.ps1`，所以「整目录无被跟踪文件」不成立 —— 缺文件的是 `TrimProbe/` 本身。）**所以本记忆给不出探针本体的可复核路径 —— 它在仓库外。**

仓库内能锚住的只有这条轴的**输入侧**：

| 事实 | 依据 |
|---|---|
| `VeloxDev.Core` 把 `net8.0` 这一档声明为 AOT 兼容 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:15` `<IsAotCompatible Condition="'$(TargetFramework)' == 'net8.0'">true</IsAotCompatible>`；`:12-14` 的注释写明「net8.0 那一档是裁剪/AOT 契约的载体」 |
| `VeloxDev.Core.Extension` 同样只在 `net8.0` 上声明 AOT 兼容 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:4`（两目标 `netstandard2.0;net8.0`）、`:9` |
| `VeloxDev.Core` 声明 **5 个** TFM | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5`（`netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`） |
| 裁剪 demo 现在**不再**显式 root `VeloxDev.Core.Extension` | `Examples/Workflow/Avalonia Trimmed/Directory.Build.props:6-11` 的注释：该树过去 root 过它，「it no longer needs to」—— 反射面已改走编译期的 Agent 上下文树与归档序列化器，不再有 trim 警告，再 root 只会保留没人读的元数据 |

**注意：`IsTrimmable=false` 这个声明已经从 `VeloxDev.Core.csproj` 里消失。** 产物侧的证据（`Src/Core/VeloxDev.Core/obj/Debug/net8.0/VeloxDev.Core.AssemblyInfo.cs:14` 是 `[assembly: AssemblyMetadata("IsTrimmable", "True")]`）说明 `net8.0` 那档带裁剪元数据；这与「csproj 写死不可裁剪」的旧说法相反，**以代码为准**。与本条互补的边界说明在 `memory/modules/VeloxDev.Core.Test/architecture.md` 与 `extension.md`（那边讲的是「测试覆盖不到它，它有自己的验证线」），两处不冲突。
