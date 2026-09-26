# VeloxDev.Core.Generator — 架构

> 代码：`Src/Generators/VeloxDev.Core.Generator/`。**16 个 .cs、5079 行**（`Base/Analizer.cs` 948、`Writers/WorkflowWriter.cs` 1708、`Writers/MVVMWriter.cs` 945、`Theme.cs` 422、`Writers/WriterBase.cs` 224、`Writers/CommandWriter.cs` 191、`AopInterface.cs` 157、`Writers/MonoWriter.cs` 125、`Writers/AopWriter.cs` 89、`Base/AnalizeHelper.cs` 67、`AopProxy.cs` 41、`MVVM.cs` 38、`Command.cs` / `MonoBehaviour.cs` / `Workflow.cs` 各 37、`Base/ICodeWriter.cs` 13）。
> 打包成 NuGet 分析器包，不产出运行期程序集；`TargetFramework=netstandard2.0`（`VeloxDev.Core.Generator.csproj:6`）。

本文只写「读完这 16 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一个 Roslyn **增量**源生成器包：把「贴了某几个特性、且声明为 `partial` 的类」补写成能编译的完整实现。产物是**消费者项目里的 C# 源文本**（`AddSource`），不是 Core 的一部分，也不进任何 DLL。

它同时服务**五个消费模块**，一包一份实现：

| 消费模块 | 贴的特性 | 生成器 |
|---|---|---|
| WorkflowSystem | `WorkflowBuilder+TreeAttribute` / `+NodeAttribute` / `+SlotAttribute` / `+LinkAttribute` / `DefaultAnchorAttribute` / `DefaultSizeAttribute` | `Workflow.cs` |
| MVVM | `VeloxPropertyAttribute` / `VeloxCommandAttribute` | `MVVM.cs` + `Command.cs` |
| TimeLine | `MonoBehaviourAttribute` | `MonoBehaviour.cs` |
| AspectOriented | `AspectOrientedAttribute` | `AopInterface.cs` + `AopProxy.cs` |
| DynamicTheme | `ThemeConfigAttribute\`3..\`7`（5 个元数） | `Theme.cs` |

**这七家 GUI 适配器在本模块里是零代码 —— 这正是「契约不该重复七遍」的实例。** 适配器全都不做特性解析、不写生成逻辑，**源码里也一个生成器特性都不用**（`grep -rn 'VeloxProperty\|\[Velox' Src/Adapters/ --include=*.cs` 零命中），因此它们**一个都不引用这个分析器包** —— 七份 `Src/Adapters/*/*.csproj` 只引用 `VeloxDev.Core` 加各家自己的 GUI 包，而 §五 那张 9 对 `PackageReference`/`ProjectReference` 的表里**没有任何适配器**；`[VeloxProperty]` 在 WPF、Avalonia、WinUI、MAUI、WinForms、Razor、Jalium 上生成的东西**逐字相同**，因为生成器读的是符号语义，从不问平台（`Base/AnalizeHelper.cs` 全文没有平台概念，`VeloxDev.Core.Generator.csproj` 也没有任何 GUI 引用）。要改「生成的属性长什么样」，改这一处就同时改了七家；要改「某家在某个平台上怎么渲染」，与本模块无关 —— 那是 `memory/modules/<WorkflowSystem|TransitionSystem>/adapters/<平台>.md` 的事。**所以本模块没有 `adapters/` 子目录，也不该有。**

**不解决什么（这些边界常在别处被误以为在这里）：**

| 不在模块内 | 实际归谁 |
|---|---|
| 生成出来的代码**跑起来是什么行为** | 全在 Core。生成器只写声明与转发（例如 `OnWorkflowSlotAdded` 的**声明**由 `Writers/MVVMWriter.cs` 写，`CreateWorkflowSlot<T>()` 的**骨架**也由它写，但生命周期归 `WorkflowSystem`） |
| 「哪些类会被处理」 | `Base/Analizer.cs:82-94` 那张**硬编码 10 条**的 `TriggerAttributes` 表。自定义特性、第三方特性一律不认 |
| 编译错误 / 诊断 | 生成器**一个 `Diagnostic` 都不发**（全源 grep `Diagnostic` 零命中）。唯一的诊断 `VELOXCFG0001` 来自 MSBuild：`VeloxDev.Core.Generator.targets:16-19` |
| 依赖注入、服务定位、注册表 | 完全不生成。生成的是「这个类自己怎么把自己装起来」，不是容器配置 |
| 平台差异 | 零。见上 |
| 版本与发布 | 见 `extension.md` §四「改这里的代价」 |
| AOT / 裁剪元数据 | **本模块不产出任何裁剪元数据**。裁剪是 MSBuild 属性与 TFM 的事，见 §六 |

---

## 二、三个阶段的边界：筛选 → 解析 → 写

除 `Theme.cs` 外，6 个生成器的 `Initialize` 是同一行形状（`MVVM.cs:17-19`、`AopProxy.cs:17-19`、`Command.cs:17`、`MonoBehaviour.cs:17`、`Workflow.cs:17`）：

```csharp
context.RegisterSourceOutput(
    Analizer.Filters.Targets(context).Combine(context.CompilationProvider),
    GenerateSource);
```

### 阶段 1：筛选 —— `Base/Analizer.cs:107` `Targets()`

- 对 `TriggerAttributes` 的 **10 条**（`:82-94`）逐条 `ForAttributeWithMetadataName`（`:114`），各 `.Collect()`，再 `.Combine().Select(AddRange)` 折成**一条**流（`:119-126`），末尾 `Deduplicate`（`:187`）。
- 候选资格另有一道闸：`IsCandidateClass`（`:160-165`）**要求类是 `partial`**。非 `partial` 的类即使贴了特性也不会进入流程，且不报错。
- 特性是**按符号**解析而非按名字匹配的（`:97-105` 的 remarks），所以全限定写法与 `using` 别名都认。

### 阶段 2：解析 —— `Base/Analizer.cs:142` `Resolve()`

**中间这一步是本模块最容易被误解的地方，也是改它的人最容易改坏的地方。**

`GeneratorTarget`（`:35-73`）是 `readonly struct`，**刻意不持有 `ISymbol`**，只持三样：`ClassDeclarationSyntax Syntax`、`string TypeKey`（类型的全限定名）、`bool IsClassLevelAttribute`。理由写在它的 remarks（`:24-33`）：

> 缓存的 transform 结果里若留着 symbol，等到输出阶段它指向的是**陈旧的 `Compilation`**；而每个 writer 都要读目标文件之外的语义（基类链、引用程序集），改别的文件之后就会**静默**生成错的代码。

所以 symbol 一律在 `Resolve` 里对着**当前** `Compilation` 现取（`:150`），树已经离开编译的目标直接跳过（`:156-157`）。

**推论（照抄会付出代价的两条）：**
- 给某个 writer 加「读更多语义」的逻辑是**安全**的 —— 它本来就拿到的是新鲜 symbol。
- 给 `GeneratorTarget` 加 symbol 字段是**不安全**的，且不会立刻报错，只会在增量场景下偶发错码。要加信息就加 `TypeKey` 这类字符串。

`Deduplicate`（`:187`）**按 `TypeKey` 去重，不按 symbol 去重**：`remarks`（`:174-182`）说明按 symbol 去重会让同一个类型在两条缓存条目持不同 `Compilation` 的 symbol 时进来两次，第二次 `AddSource` 会因为 hint name 重复被拒。一个类拆成多个 partial、每个 partial 各贴一个触发特性时，只有**一个代表**进入 writer；谁当代表由 `IsClassLevelAttribute` 决定（类级特性优先，`:76-81` 说明理由：`Writers/MonoWriter.cs`、`Writers/AopWriter.cs`、`AopInterface.cs` 是从**拿到的那份声明**上读特性的）。**所以「代表是哪份声明」会直接影响这三个生成器的输出。**

### 阶段 3：写 —— 各生成器的 `GenerateSource`

固定四步：`new XxxWriter()` → `Initialize(syntax, symbol)` → `CanWrite()` 闸门 → 写。

闸门是**静默**的：`CanWrite()` 返回 false 就不 `AddSource`，没有诊断、没有空文件。`Writers/MVVMWriter.cs:845`、`Writers/CommandWriter.cs:118`、`Writers/MonoWriter.cs:70`、`Writers/AopWriter.cs` 的 `CanWrite()` 都是符号判定。

`Theme.cs` 是唯一不走 `Targets/Resolve` 的（它自己建 5 条流，`:32-55`，按 `ThemeConfigAttribute` 的 5 个元数分别订阅），**且只对 `partial` 类发**（`:112-118`），没有可用属性注册时返回 `string.Empty`（`:261-264`）—— 同样是静默无输出。

### 产物命名（hint name = 文件名）

| 生成器 | 文件名模板 | 依据 |
|---|---|---|
| AOP 接口 | `{类}_{命名空间下划线}_Aop` | `AopInterface.cs:35`、`AddSource` `:125` |
| AOP partial + 扩展（**同一 writer 写两份，两句 `AddSource`**） | `{类}_{命名空间下划线}_AOP.g.cs` / `{类}_{命名空间下划线}_AopExt.g.cs` | `Writers/AopWriter.cs:32`、`:50`；`AopProxy.cs:31/36` |
| Command | `{类}_{命名空间下划线}_Commands.g.cs` | `Writers/CommandWriter.cs:129` |
| MVVM | `{类}_{命名空间下划线\|Global}_MVVM.g.cs` | `Writers/MVVMWriter.cs:847-854` |
| Mono | `{类}_{命名空间下划线}_Mono.g.cs` | `Writers/MonoWriter.cs:61` |
| Theme | `{类}_{命名空间下划线}_ThemeConfig.g.cs` | `Theme.cs:99` |
| Workflow | 见 `Writers/WorkflowWriter.cs` | — |

**`Writers/MVVMWriter.cs:850-853` 是全部 writer 里唯一处理全局命名空间的一处**（`IsGlobalNamespace ? "Global" : ...`，`Writers/MVVMWriter.cs:851`）。其余全部用 `Symbol.ContainingNamespace.ToDisplayString().Replace('.', '_')` —— 而 `ToDisplayString()` 对**全局命名空间**返回的是字面量 `"<global namespace>"`。见 §六·1。

---

## 三、`Writers/WriterBase.cs`：所有 writer 的公共骨架

`Write()`（`:40`）的固定次序：`// <auto-generated>` → `#pragma warning disable` → `#nullable enable`（`:55-57`）→ namespace → 外层 partial 类（嵌套类才走，`:68-88`）→ 当前类声明（修饰符 + 名字 + 类型参数 + 基类型 + 约束）→ `GenerateBody()` → 补外层大括号。

**子类只有四个扩展点**（`Base/ICodeWriter.cs:8-11` 定接口，`Writers/WriterBase.cs:219-223` 定抽象）：`CanWrite` / `GetFileName` / `GenerateBaseTypes` / `GenerateBaseInterfaces` / `GenerateBody`。

两个不显眼但关键的机制：

1. **基础类型列表是「用户原有 + 生成器要加」去重后的并集**。`GetBaseTypes()`（`:134-174`）先放用户原本的接口（`:142-153`）和基类（`:156-164`，放进列表头，使基类排在接口前），再 `AddRange(GenerateBaseTypes())` 与 `AddRange(GenerateBaseInterfaces())`（`:167-168`），最后 `.Distinct()`。**所以「生成的类继承了谁」有两处来源，漏改一处会出现重复基类型或丢接口。**
2. **`FormatModifiers`（`:190-217`）把 `partial` 永远排到修饰符最后**。生成的声明必须与原声明修饰符集合一致，否则会重复定义。要加新修饰符就改这里，别在模板字符串里手写。

`Writers/AopWriter.cs` 是唯一写**两份**产物的 writer：`Write()` 出 partial 类（`GenerateBody()` 返回 `string.Empty`，`:44`，它只靠 `GenerateBaseInterfaces()` 挂上接口），`WriteExtension()`（`:53`）出 `namespace VeloxDev.AspectOriented;` 下的 `Aop()` 扩展方法。扩展方法文件名**不在** `ICodeWriter` 接口里，是 `AopProxy.cs` 直接调的（`:37`）——**给别的生成器加第二份产物时，接口里没有位置，得照这里的写法加一个自己的方法。**

---

## 四、writer 的分工与它们之间的耦合

| writer | 职责 | 它读的配置 | 需要注意 |
|---|---|---|---|
| `Writers/WorkflowWriter.cs`（1708 行，最大） | `TreeAttribute/NodeAttribute/SlotAttribute/LinkAttribute` 四种模型；`WorkflowType` 1..4 分派；另支持一个**没贴特性、只重声明节点默认值**的子类路径 | 四个特性的构造参数 | 节点布局/尺寸/`RuntimeId` 的初始化代码都在这里 |
| `Writers/MVVMWriter.cs`（945 行） | 属性的 setter 体、通知事件、集合订阅、Workflow 槽位生命周期 | `VeloxPropertyAttribute` + `DetectSetterMode()`（`:42-89`）探测基类 | 见下 |
| `Writers/CommandWriter.cs` | 懒建 `IVeloxCommand` 属性 + 可选 `CanExecute{名}Command` partial 钩子 | `VeloxCommandAttribute`，**位置参数先读、具名参数覆盖**（`:47-61`）；名字为 `"Auto"` 时取方法名去掉 `Async`（`:64-67`） | 三段构造器选择 `ParseConstructorType`（`:78-116`） |
| `Writers/MonoWriter.cs` | `InitializeMonoBehaviour` / `CloseMonoBehaviour` / 5 个 `partial void` 钩子 | `MonoBehaviourAttribute` 的 `(channel, fps)` | **只实现、不调用** —— 只贴特性而不调 `InitializeMonoBehaviour()` 等于什么都没发生 |
| `Writers/AopWriter.cs` | AOP 接口实现 + `Aop()` 扩展方法 | — | 见 §三 |
| `AopInterface.cs` | AOP 接口本身（`VeloxDev.AopInterfaces` 命名空间） | — | 它**不在 `Writers/` 下**，是唯一一个把生成逻辑直接写在生成器类里的 |
| `Theme.cs` | `IThemeObject` 实现、主题缓存、`SetThemeValue<T>` 一族 | 5 个 `ThemeConfigAttribute` 元数 | 只对 `partial` 类发 |

**两处会咬人的耦合：**

1. **`TriggerAttributes` 是 5 个消费模块共用的同一张表。** 加一条会影响**所有**生成器对「哪些类进入流程」的判断；`MVVM.cs` 与 `Command.cs` 对同一个类**都**会产出（各自再靠 `CanWrite()` 过滤），所以给 MVVM 加特性时两个生成器都会被喂到。这是本仓库最容易漏的联动点，见 `extension.md` §三·1。
2. **`Writers/MVVMWriter.cs` 的 `IsWorkflowComponent`（`:15`，初始化于 `:33`）让 MVVM 生成器为 Workflow 类补 `CreateWorkflowSlot<T>()` / `OnWorkflowSlotAdded` / `OnWorkflowSlotRemoved` 三件套**（`:895` 一带）。也就是说**这条契约的生成侧在 MVVM writer 里，而语义侧在 WorkflowSystem** —— 找「工作流槽位生命周期为什么长这样」不能只看 `Writers/WorkflowWriter.cs`。

---

## 五、引用这个包：Debug/Release 双轨

`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:30-39` 把规则写成了注释：

> Debug 用本地生成器源码，Release 用包。analyzer 不随 ProjectReference 传递，所以用到生成器特性的项目都要各自重复这一对。

配对形状（`Debug` 走 `ProjectReference` + `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`，非 Debug 走 `PackageReference`）：

| 项目 | ProjectReference | PackageReference |
|---|---|---|
| `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` | `:33` | `:37` |
| `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj` | `:32` | `:33` |
| `Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj` | `:26` | `:30` |
| `Examples/Workflow/Directory.Build.props`（整棵树一次） | `:6` | `:10` |
| `Examples/AOP/WPF/Demo/Demo.csproj` | `:17` | `:21` |
| `Examples/AOP/Avalonia/Demo/Demo.csproj` | `:35` | `:39` |
| `Examples/MVVM/WPF/Demo/Demo.csproj` | `:17` | `:21` |
| `Examples/MVVM/Avalonia/Demo/Demo.csproj` | `:35` | `:39` |
| `Examples/MonoBehaviour/WPF/Demo/Demo.csproj` | `:17` | `:21` |

**共 9 处 `PackageReference`、9 处 `ProjectReference`，全部 `Version="9.0.0"`；而包自己的 `<Version>` 是 `9.0.228`（`VeloxDev.Core.Generator.csproj:11`）。两者不相等是刻意的，见 [extension.md](extension.md) §四。** （任务书里说的「11 处」在树里复核不到：全仓 grep `VeloxDev.Core.Generator` 命中引用点就是上表 18 条，另有 `VeloxDev.Core.Generator.csproj:10/37/38` 是包自己；`9.0.153` 在任何构建文件里都不存在，只出现在 `Src/Generators/VeloxDev.Core.Generator/bin/Release/netstandard2.0/VeloxDev.Core.Generator.deps.json:10` 这个构建产物里。以代码为准。）

**「两者必须互斥」是被注释明确写下的硬约束**（`Examples/Workflow/Directory.Build.props:4`）：两条同时生效会**生成器执行两次、报重复成员**。另外 `Examples/Workflow/Directory.Build.props:5` 提醒路径基准是导入方项目目录，必须走 `MSBuildThisFileDirectory`。

---

## 六、陷阱（带依据）

1. **全局命名空间会生成出非法 namespace。** `Writers/WriterBase.cs:63` 与 `:72` 无条件写 `namespace {Symbol.ContainingNamespace};` —— 而 `INamespaceSymbol.ToDisplayString()` 对全局命名空间返回字面量 `"<global namespace>"`，于是产物里出现 `namespace <global namespace>;`。同理，`Writers/AopWriter.cs:32/39/50`、`AopInterface.cs:35`、`Writers/CommandWriter.cs:129`、`Writers/MonoWriter.cs:61` 都直接用 `ToDisplayString().Replace('.', '_')`，不含全局命名空间分支 —— 全局命名空间的类会得到 `{类}_<global namespace>_Aop` 这种文件名/接口名。**全模块只有 `Writers/MVVMWriter.cs:850-853` 处理了这一情形**（`IsGlobalNamespace ? "Global"`）。**行为已存在，不要以为某处有统一的守卫。**
2. **生成器不发任何诊断。** 特性名拼错、类忘了写 `partial`（`Base/Analizer.cs:160-165`）、`CanWrite()` 为 false、`Theme.cs` 没注册属性 —— 四种情况都表现为「编译通过、什么都没生成」。排查时先看 `obj/<配置>/<TFM>/generated/...` 下有没有产物，别指望错误列表。
3. **`TriggerAttributes` 只有 10 条且硬编码**（`Base/Analizer.cs:82-94`）。新特性不进去，生成器对该类型**完全无感且不报错**。
4. **`AopProxy.cs` 一个类连着两次 `AddSource`**（`:31`、`:36`）。加第三份产物必须自己保证 hint name 不撞。
5. **`Theme.cs` 只对 `partial` 类发**（`:112-118`），且无属性注册时返回空串（`:261-264`）。
6. **`Writers/WriterBase.cs:190-217` 的修饰符重排是「不报重复定义」的依赖**，不是格式化洁癖。
7. **`Generators.AgentCatalog` 不存在于当前源码。** `Src/Core/VeloxDev.Core/obj/Debug/net10.0/generated/VeloxDev.Core.Generator/VeloxDev.Generators.AgentCatalog/VeloxAgentCatalog.g.cs`（149 KB）是 `obj/` 里的**陈旧产物**：当前源码 16 个 `.cs` 里没有任何 AgentCatalog，当前 Debug 产物 DLL 里 `AgentCatalog` 命中 0 次。别按它去推现在的生成器有几个。
8. **裁剪/AOT 元数据与本模块无关。** 全部 writer 都不产出 `IsTrimmable` / `IsAotCompatible` / trim 注解；引擎侧也没有生成任何 `DynamicDependency` 之类的裁剪提示（全源 grep 无命中）。裁剪这条轴的开关在 csproj 与 MSBuild 属性上，见 §七。

---

## 七、验证线在哪（以及它不在哪）

**裁剪/AOT 的验证探针不在本仓库。** `Src/Verification/VeloxDev.TrimProbe/` 在工作区里**只剩 `bin/` 与 `obj/`**：没有 `.cs`、没有 `.csproj`；`git ls-files Src/Verification/` 为空（整目录无被跟踪文件）；`VeloxDev.slnx` 里没有 `TrimProbe`。**所以本记忆给不出探针本体的可复核路径 —— 它在仓库外。**

仓库内能锚住的只有这条轴的**输入侧**：

| 事实 | 依据 |
|---|---|
| `VeloxDev.Core` 显式声明不可裁剪 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:12` `<IsTrimmable>false</IsTrimmable>` |
| `VeloxDev.Core.Extension` 同上 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:8` |
| 裁剪 demo 必须**显式 root 住** `VeloxDev.Core.Extension`，理由是「ProjectReference 不导入包的 buildTransitive props」 | `Examples/Workflow/Avalonia Trimmed/Directory.Build.props:7-16`（`<TrimmerRootAssembly Include="VeloxDev.Core.Extension" />` 在 `:16`） |
| `VeloxDev.Core` 只声明 4 个 TFM | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5`（`netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0`） |

**一个反直觉的观察（依据是产物，不是 csproj）：** `obj/` 下存在 `net8.0` / `net10.0` 两个 **csproj 未声明**的 TFM 的构建产物，且**只有这两个**带裁剪元数据 —— `Src/Core/VeloxDev.Core/obj/Debug/net8.0/VeloxDev.Core.AssemblyInfo.cs:13` 与 `obj/Debug/net10.0/VeloxDev.Core.AssemblyInfo.cs:13` 都有 `[assembly: AssemblyMetadata("IsTrimmable", "True")]`（net10.0 的 `:14` 还多一条 `IsAotCompatible`），而四个被声明的 TFM（netstandard2.0 / netframework4.6.1 / net5.0 / netcoreapp3.0）**一条都没有**。

⇒ **csproj 里的 `false` 不是最终值**：那两个 TFM 的元数据只能来自外部构建用 MSBuild 属性覆盖（`-p:IsTrimmable=true` / `IsAotCompatible=true`），属性覆盖优先于 csproj。**「csproj 写了 false 所以不可裁剪」是错的推论。** 与本条互补的边界说明在 `memory/modules/VeloxDev.Core.Test/architecture.md` 与 `extension.md`（那边讲的是「测试覆盖不到它，它有自己的验证线」），两处不冲突。
