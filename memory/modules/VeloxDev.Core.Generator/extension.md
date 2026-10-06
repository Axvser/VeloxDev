# VeloxDev.Core.Generator — 扩展

> 代码：`Src/Generators/VeloxDev.Core.Generator/`。**本文件里不写全路径的文件名都指这个目录**（`Base/Analizer.cs`、`Writers/MVVMWriter.cs`、`Theme.cs` …）。
> 架构与流向见 [architecture.md](architecture.md)。

---

## 一、先分类：你要加的是哪一种

| 你想做的事 | 去哪一节 | 一次要动几处 |
|---|---|---|
| A. 让一个新的**特性**触发代码生成（给某个消费模块加能力） | §二·A | **至少 3 处**（候选清单 + writer + 生成器类），详见 §五 |
| B. 给已有生成器加一种**产物形状**（多生成一个成员/多写一份文件） | §二·B | 1–2 处 |
| C. 加一个**全新生成器**（新的一类契约） | §二·C | 4 处 |
| D. 加一个**特性元数**（如 `ThemeConfigAttribute\`8`） | §二·D | 2 处 |
| E. 改「谁算 AOP 类」之类的**判定** | §二·E | 1 处，但会静默改变行为 |

**判据：** 先问「这个新能力是『哪些类会被处理』变了，还是『处理出来的东西』变了」。前者必须动 `Base/Analizer.cs:116-128`；后者才动 writer。**只动 writer 不动清单 = 新特性永远不触发，且不报错。**

---

## 二、扩展点逐个说

### A. 加一个新的触发特性

三个位置，缺一不可：

1. **`Base/Analizer.cs:116-128` 的 `TriggerAttributes`**（10 条 `readonly string[]`）—— 加一条**元数据名**（`Namespace.Type+嵌套名`，泛型特性带 `` `1 `` 这类元数后缀，照 `:118-121` 的 `WorkflowBuilder+TreeAttribute\`1` 写法）。这是**所有生成器共用的同一张表**。
2. **写或复用 writer**：实现 `Base/ICodeWriter.cs:8-15` 的四个成员（`Initialize` / `CanWrite` / `Write` / `GetFileName`），或继承 `Writers/WriterBase.cs` 只实现它的五个抽象成员（`Writers/WriterBase.cs:264-272`）。
3. **一个生成器类**：`[Generator(LanguageNames.CSharp)]` + `IIncrementalGenerator`，`Initialize` 写 `context.RegisterSourceOutput(Analizer.Filters.Targets(context).Combine(context.CompilationProvider), GenerateSource)`，`GenerateSource` 走 `Filters.Resolve` → `new writer` → `CanWrite()` → `AddSource(writer.GetFileName(), writer.Write())`。照 `MVVM.cs:12-45` 抄最短。

**候选闸门也是硬性的**：`Base/Analizer.cs:191-195` 的 `IsCandidateClass` 要求类是 **`partial`**。不 `partial` ⇒ 该类型不进入流程，**没有诊断**。

### B. 给已有生成器加一种产物形状

- **多生成一个成员**：改对应 writer 的 `GenerateBody()`，或改 `Base/Analizer.cs` 里那组 `MVVM*` 类（它是 MVVM 侧绝大多数模板的所在地：`SetterMode` 的 setter 体在 `MVVMPropertyFactory.GetSetterBodyLines()`（`:546` 起）、集合订阅与 `Enumerate{名}Items` 合成在 `GenerateCollectionMembers()`（`:830` 起）。注意槽位生命周期**不在**这里：`GenerateWorkflowSlotMembers()`（`:825`）现在只返回 `string.Empty`，三件套已移到 `Writers/MVVMWriter.cs:1040-1065`）。
- **多写一份文件**：照 `Writers/AopWriter.cs` 的第二产物写法 —— 额外的 `GetExtensionFileName()`（`:51`）/`WriteExtension()`（`:58`）对**不在 `ICodeWriter` 接口里**，是在生成器类里直接调（`AopProxy.cs:33-35` 的 `AddSource`）。**加第二份产物必须自己保证 hint name 不与第一份撞。** （`AopSurface.cs` 更进一步：接口与代理两个产物由同一个生成器各 `AddSource` 一次，`:163`/`:167`。）
- **加基础类型/接口**：`GenerateBaseTypes()` 或 `GenerateBaseInterfaces()` 返回数组即可，`Writers/WriterBase.cs:211-212` 会把它们并入用户原有基类型后 `.Distinct()`。
- **改修饰符**：只能改 `Writers/WriterBase.cs:234-261` 的 `FormatModifiers` —— `partial` 必须排在最后，否则生成的声明与原声明被视为不同而报重复定义。

### C. 加一个全新生成器

在一个类里做四件事：`[Generator(LanguageNames.CSharp)]`（`MVVM.cs:12` 等 10 处同形）→ `IIncrementalGenerator` → `Initialize` 注册 → `GenerateSource` 里循环。**不要写 `ISourceGenerator`**（`architecture.md` §二）。

**引用点不用改**：`VeloxDev.Core.Generator.csproj:41` 是按 `bin\$(Configuration)\$(TargetFramework)\$(AssemblyName).dll` 整包进 `analyzers/dotnet/cs`，新增的生成器类自然进包。

### D. 加一个特性元数（以 Theme 为例）

`Theme.cs:33-55` 是 **5 条并列的 `ForAttributeWithMetadataName`**（`ThemeConfigAttribute\`3` … `` `7 ``），`.Collect()` 后逐级 `.Combine(...)`（`:59-62`）。加第 6 个元数要改**两处**：新增一条流 + 把新流 `.Combine` 进 `allClasses`。漏掉 Combine 不会报错，只会让该元数的类**永不生成**。

### E. 改「谁算 AOP 类」这类判定

`Base/AnalizeHelper.cs` 里有两个同名重载：

- `:43-46` `IsAopClass(INamedTypeSymbol)` —— **实际被用的**（`AopSurface.cs:65`、`Writers/AopWriter.cs:23`），走 `Members(symbol)` 扫**所有 partial 声明**。
- `:12-17` `IsAopClass(ClassDeclarationSyntax)` —— **没有任何调用者**（全源 grep 只命中定义本身）。见 §七。

判定用的名字是**裸字符串**比较：`HasAspectOriented`（`:48-53`）判 `attribute.Name.ToString() == NAME_ASPECTORIENTED`（`NAME_ASPECTORIENTED = "AspectOriented"`，`:10`）—— **不是全限定名**。所以任何**恰好叫 `AspectOriented`** 的成员特性（哪怕来自别的命名空间、别的库）都会把类判成 AOP 类。**改用全限定名比较会收紧行为，属于会改变现有输出的改动。**

---

## 三、官方做法 vs 看着能编译、但错的捷径

| 场景 | 官方做法 | 看着能编译、但错的捷径 | 错了会怎样 |
|---|---|---|---|
| 让某个特性触发生成 | 把元数据名加进 `Base/Analizer.cs:116-128` | 只在 writer 里支持该特性 | 类从不进入流程，**不报错、不生成** |
| 让某一家 GUI 生成不同的东西 | **不做** —— 平台差异去改适配器（`Src/Adapters/VeloxDev.<GUI>/`） | 在 writer 里判断平台 / `#if WPF` | 生成器是 `netstandard2.0` 且不引用任何 GUI 程序集，写不出来；就算写出来也违背「契约只写一次」 |
| 把语义信息带进 writer | writer 里用 `Initialize` 收到的**新鲜** `INamedTypeSymbol` 现读 | 给 `Base/Analizer.cs:64-108` 的 `GeneratorTarget` 加 symbol 字段 | 增量下 symbol 指向**陈旧 `Compilation`**，偶发生成错码；只改别的文件才复现 |
| 从 writer 里读 `partial` 的其它声明 | `Base/AnalizeHelper.cs:23-36` 的 `Declarations`/`Members` | 只看 `Initialize` 拿到的那一份 `ClassDeclarationSyntax` | 拆成多个 partial 文件时**静默少生成**；`Writers/AopWriter.cs:20` 的注释就是为这条写的 |
| 命名产物 | `GetFileName()` 返回 `{类}_{命名空间}_X.g.cs`，命名空间段走 `WriterBase.NamespaceFileSegment()` | 用 `ToDisplayString().Replace('.','_')` 拼完事 | 全局命名空间会生成出 `{类}_<global namespace>_Aop`；`Writers/WriterBase.cs:78-89` 若回退更会写出非法的 `namespace <global namespace>;`。**统一样板是 `Writers/WriterBase.cs:40-43`** |
| 加基础接口 | 返回 `GenerateBaseInterfaces()` 数组 | 在 `GenerateBody()` 或模板串里手写 `: IFoo` | 类型声明由 `Writers/WriterBase.cs:137` 一处拼出，模板串里写的会出现在类体里 ⇒ 语法错误 |
| 让生成器报错 | 在 `Diagnostics.cs` 加一条 `DiagnosticDescriptor`，照 `CommandWriter.cs:100`（`List<Diagnostic> Diagnostics`）+ `Command.cs:33-35` 那一对（writer 收集，生成器类在 `CanWrite()` **之前**逐条 `ReportDiagnostic`） | 随手 `context.ReportDiagnostic(...)` | 诊断号要按模块+种类命名（`VELOX_MVVM_CMD…` / `VELOX_MVVM_PROP…`，2026-10-02 起；Agent 树那条是 `VELOX_AI_TREE001`）。`EnforceExtendedAnalyzerRules`（`VeloxDev.Core.Generator.csproj:5`）已开但当前**零告警** —— 因为分析器是 `IIncrementalGenerator`，不需要 `SupportedDiagnostics`；**而 RS2008（分析器发布跟踪）要的两份文件是齐的** —— `AnalyzerReleases.Shipped.md` / `Unshipped.md` 由 csproj `:31-32` 以 `AdditionalFiles` 纳入，文件本身也在树里。**零告警正是它们齐了的结果，不是「没被要求」** |
| 发版 | 改 `VeloxDev.Core.Generator.csproj:11` 的 `<Version>` **并且**改 §四那张表的 11 处引用 | 只改 csproj | Debug 走源码仍是对的，Release **静默**还原旧包 —— 本地怎么调都复现不出来 |

---

## 四、改这里的代价（版本锁：11 处引用与包版本）

**当前状态：包版本 `10.0.0`（`VeloxDev.Core.Generator.csproj:11`）。** 11 处 `PackageReference` 的 `Version=` 需逐一核对 —— 多数已对齐 `10.0.0`，**个别 demo 树里的仍停在旧值，不要「顺手修平」**。Debug 走 `ProjectReference`，包版本号根本不参与；Release 才会解析到 NuGet 上已发布的生成器，即**不含这条线上后续本地改动的那一版**。看到源码版本与某处引用不相同时，第一反应不该是补齐，而是先确认「这一轮是不是仍然只要 Debug」。（`Examples/*/Directory.Build.props:10` 仍有两处未对齐到 `10.0.0`；其余引用已对齐。）

**版本历史**（提版说明只存在于提交信息里 —— **不可复核**；能复核的只有当前 csproj 里的 `10.0.0`）：
- 2026-09-26：Core 与其余库统一停在 `9.0.x`，只保证 Debug。
- 2026-10-01：`9.0.228` → `9.1.0`（`ValueTask` / `ValueTask<T>` 命令体支持）。
- 2026-10-02：`9.1.0` → `9.2.0`（`[VeloxProperty]` 字段与 partial 属性可同时声明）。
- 2026-10-02：`9.2.0` → `9.3.0`（`[VeloxCommand]` 强类型命令）。
- 2026-10-02：`9.3.0` → `9.4.0`（命令结果通道 + 校验器形参名跟随源方法；**含两处破坏性变更**：校验器改名报 CS8826，`Task<T>` 命令属性类型变成 `IVeloxCommand<T, R>`、参数类型不可见时报 CS0053）。
- 2026-10-02：arity 族一版（命令支持最多 15 个形参，按前导形参个数选元数族）。
- 此后提到 `10.0.0`（当前值）。

下面 11 处 `PackageReference`（`Version=` 以工作区各 csproj 为准）：

| # | 文件:行 |
|---|---|
| 1 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:48` |
| 2 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:31` |
| 3 | `Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj:37` |
| 4 | `Examples/Workflow/Directory.Build.props:10` |
| 5 | `Examples/Theme/Directory.Build.props:10` |
| 6 | `Examples/AOP/WPF/Demo/Demo.csproj:21` |
| 7 | `Examples/AOP/Avalonia/Demo/Demo.csproj:39` |
| 8 | `Examples/MVVM/WPF/Demo/Demo.csproj:23` |
| 9 | `Examples/MVVM/Avalonia/Demo/Demo.csproj:41` |
| 10 | `Examples/MVVM/Common/Lib/Lib.csproj:23` |
| 11 | `Examples/Tickable/WPF/Demo/Demo.csproj:21` |

（`Src/Core/VeloxDev.Core.Test/VeloxDev.Core.Test.csproj` 不在这张表里：它**不分 Debug/Release**，无条件走 `ProjectReference`，只钉一个 `Microsoft.CodeAnalysis.CSharp` 包，没有生成器 `PackageReference` —— 见 [architecture.md](architecture.md) §五。）

**为什么每处都要重复写**：Debug 走 `ProjectReference`、Release 走包，而**分析器不随 `ProjectReference` 传递**（`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:41-42` 的注释原文：「Debug 用本地生成器源码，Release 用包。analyzer 不随 ProjectReference 传递，所以用到生成器特性的项目都要各自重复这一对。」）。`Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:28` 又重复了一遍同样的理由。

**两条引用必须互斥**（`Examples/Workflow/Directory.Build.props:4`）：「两者必须互斥，否则生成器执行两次、报重复成员」。改这一对时不要图省事让它们同时生效。

**升版本的完整动作**：`VeloxDev.Core.Generator.csproj:11` → 上表 11 处 `Version=` 全改 → 想清 Release 会不会静默还原旧包（Debug 看不出问题）。路径基准注意 `Examples/Workflow/Directory.Build.props:5` 的说法：基准是导入方项目目录，必须走 `MSBuildThisFileDirectory`。

> **复核口径**：全仓 grep `VeloxDev.Core.Generator` 的引用点就是上表 11 条 `PackageReference` 加对应的 `ProjectReference`（`VeloxDev.Core.csproj:44`、`VeloxDev.Core.Extension.csproj:30`、`VeloxDev.Core.Extension.Test.csproj:33`、`VeloxDev.Core.Test.csproj:28`、`Examples/Workflow/Directory.Build.props:6`、`Examples/Theme/Directory.Build.props:6`、四个 demo 的 `:17/:35/:19/:37`、`Examples/MVVM/Common/Lib/Lib.csproj:19`、`Examples/Tickable/WPF/Demo/Demo.csproj:17`）。`Src/Adapters/*` 只在 `bin/` 的 `.pdb` 里偶然命中该字符串，源码与 csproj 一律不引用（见 [architecture.md](architecture.md) §一）。

---

## 五、联动清单：加一个特性 / 生成器时要同步动哪里

| # | 位置 | 加什么 | 漏了会怎样 |
|---|---|---|---|
| 1 | `Base/Analizer.cs:116-128` | 新特性的元数据名 | **该特性完全无感**，不生成、不报错 |
| 2 | 新 `Xxx.cs`（生成器类，照 `MVVM.cs:12-45`） | `[Generator]` + `IIncrementalGenerator` + `RegisterSourceOutput` | 没有产物 |
| 3 | 新 writer（或复用） | 实现 `Writers/WriterBase.cs` 的抽象成员 | 同上 |
| 4 | 消费项目的引用对 | 若是**新项目**：`ProjectReference`(Debug) + `PackageReference`(Release) 一对 | Release 下不生成；Debug 正常 ⇒ 本地测不出来 |
| 5 | `memory/modules/<消费模块>/extension.md` | 该特性属于哪个消费模块、生成形状 | 下一个 agent 找不到生成逻辑在 `Src/Generators/` 而不是模块目录里 |
| 6 | 裁剪验证 | 跑一次裁剪/AOT 验证（§六） | 新生成的成员可能在裁剪后消失，而本地 Debug 跑不出来 |

**第 1 条与第 5 条是本仓库最容易漏的两处**：第 1 条静默失效，第 5 条要等下一个 agent 在模块目录里白找一遍。

---

## 六、改生成器或裁剪相关代码后，必须跑裁剪验证

**先记事实：探针本体不在本仓库。** `Src/Verification/VeloxDev.TrimProbe/` 在工作区里只剩 `bin/` 与 `obj/`，没有任何 `.cs` 与 `.csproj`；`VeloxDev.slnx` 里没有 `TrimProbe`。**所以这里给不出可复核的探针路径 —— 它在仓库外，本记忆只能记「有这么一条验证线，动生成器后必须走一遍」这件事本身。**

仓库内可复核的**输入侧**锚点：

| 事实 | 依据 |
|---|---|
| `VeloxDev.Core` 把 `net8.0` 那一档声明为 AOT 兼容 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:15`（`:12-14` 注释：net8.0 那一档是裁剪/AOT 契约的载体） |
| `VeloxDev.Core.Extension` 同上 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:9` |
| 裁剪 demo **不再**显式 root `VeloxDev.Core.Extension` —— 反射面已改走编译期的 Agent 上下文树与归档序列化器，没有 trim 警告了 | `Examples/Workflow/Avalonia Trimmed/Directory.Build.props:6-11` |

**为什么改生成器必须跑它**：生成器改的是「哪些类型/成员存在」，而裁剪删的是「没人引用的类型/成员」—— 两条轴正好作用在同一个集合上（`architecture.md` §六·8 已确认生成器**不产出任何裁剪提示**，所以没有任何自动保护）。**本地 Debug 构建看不出任何问题。**

> **复核（2026-10-04）**：`Src/Verification/VeloxDev.TrimProbe/` 底下**只有 `bin/` 与 `obj/`**，没有源码 —— `git ls-files` 对它零命中。但注意 `Src/Verification/` 这一层**并非全空**：`git ls-files Src/Verification/` 现在命中 4 个被跟踪的 `.ps1`（`agent-ui-harness.ps1`、`agent-web-harness.ps1`、`verify-jalium-item-templates.ps1`、`verify-workflow-item-templates.ps1`），磁盘上还有一个尚未 `git add` 的新脚本 `verify-workflow-item-templates-all.ps1`（另两个同名脚本是转发到它的薄包装）。准确的说法是「**TrimProbe 本体无被跟踪文件**」—— 不是「`Src/Verification/` 为空」。`VeloxDev.slnx` 里依旧没有 `TrimProbe`。**结论不变：这里没有可写的代码事实，裁剪探针的本体在仓库外。**

---

## 七、死扩展点 / 已失效的钩子

| 东西 | 位置 | 现状 |
|---|---|---|
| `AnalizeHelper.IsAopClass(ClassDeclarationSyntax)` | `Base/AnalizeHelper.cs:12-17` | **没有调用者**。实际用的是同名的符号重载 `:43-46`（调用点 `AopSurface.cs:65`、`Writers/AopWriter.cs:23`）。语法版只扫**单份声明**的成员，是符号版之前的写法；留着但无效 |
| `Generators.AgentCatalog` | 无（源码 29 个 `.cs` 无此类；`Src/Core/VeloxDev.Core/obj/Debug/net10.0/generated/VeloxDev.Core.Generator/VeloxDev.Generators.AgentCatalog/VeloxAgentCatalog.g.cs` 也不在树里） | **不存在**。别去找它 |
| `GenerateBaseTypes()` | `Writers/AopWriter.cs:40`、`Writers/CommandWriter.cs:596`、`Writers/TickWriter.cs:130`、`Writers/MVVMWriter.cs:1010` 返回 `[]` | **不是死点** —— 返回空是合法答案，只有 `Writers/WorkflowWriter.cs:73` 真正用到了它 |
| MVVM 的 View 生成路径 | 原 `Base/Analizer.cs` 的 `IsView` / `GenerateProxy()`（属性、分派、实现三段） | **已整体删除（2026-09-26）**：全源 grep 已无 `IsView` / `isView`；`MVVMPropertyFactory` 现在只有两个构造 —— 从字段（`Base/Analizer.cs:432`）与从 partial 属性（`:453`），`Generate()`（原 `GenerateViewModel` 改名，`:654`）是唯一出口。**要恢复 View 支持，必须同时改构造、`Generate()` 与调用点** —— 别再只加参数不加分支 |
| `VeloxDev.Core.Generator.targets` 的版本门槛 | `VeloxDev.Core.Generator.targets:8-19` | **活着，但条件刻意放宽**：`RoslynVersion` 为空时**跳过检查**（`:13-15` 的注释：现代宿主上的 netframework TFM 拿不到该属性，跳过以免误报）。所以这条诊断**不会**在每个项目上都出现 |

---

## 八、消费方要满足什么，生成器才用得上（2026-10-04 实测）

**「能不能用源生成」看的是谁在编译，不是编译给哪个框架。** 生成器跑在编译器进程里，程序集是
netstandard2.0 —— 与消费方的 TFM 无关。实测：一个 **net40** 工程照样加载生成器、产出、编译、运行
（`Environment.Version` 报 4.x 的 CLR、`mscorlib` 是 4.0.0.0）。

| # | 判据 | 取决于 | 不满足时 |
| --- | --- | --- | --- |
| ① | 生成器 API 的世代 | 消费方编译器的 Roslyn：`IIncrementalGenerator` ≥ **4.0** | 🔇 **静默**：什么都不生成 |
| ② | 分析器程序集版本 ≤ 编译器 | 生成器编译时引用的 `Microsoft.CodeAnalysis.CSharp`（本仓库已抬到 **4.8**） | `CS1705`「引用的程序集版本高于…」（撞过一次：测试工程还停在 4.3 时） |
| ③ | **产物**编不编得过 | 消费方的 `LangVersion` 与 BCL 面 | 报错落在**生成文件**里，看起来像生成器的锅 |
| ④ | **产物**调不调得到库 | 被生成的那个库有没有该 TFM 的资产 | 类型找不到，且 **NuGet 不报兼容性错误** |
| ⑤ | 该生成器启不启用 | 它的触发条件（特性能否解析、MSBuild 开关） | 🔇 **静默**不生成 |

**目标框架只影响 ③④**，不影响 ①② —— 所以「netX 能不能用生成器」这个问法本身是错的。

**③ 的账单**：产物用到文件范围内的命名空间（C# **10**）、可空引用类型（8）、`is { }` / `is not null`（8/9）、
集合表达式 `[...]`（**12**）⇒ 消费方 `LangVersion` 下限 **12**，而 **.NET Framework 目标默认 7.3**（`CS8370`）。
另外产物用 `[ModuleInitializer]`，**包里不自带那个 polyfill** —— 仓库内看不出来，因为
`Src/Core/VeloxDev.Core/ModuleInitializerAttribute.cs` 自己带着（同理还有 `IsExternalInit.cs`）。

**④ 的账单**：`VeloxDev.Core` 已发布的最新（8.0.0）只有 `net461` / `netstandard2.0` / `netcoreapp3.0` /
`net5.0`。挂到 net40 上时 NuGet **还原成功、不报兼容性错误**，只是不给编译资产 —— `CS0246` 最后出现在
消费方**自己那行 `using`** 上：错误指向受害者。**序列化生成器的产物直接调用引擎**
（`VeloxJsonSerializer` / `VeloxJsonRegistry`），所以它不可能脱离 Core 单独支持某个 TFM。

> ①②⑤ 全是**静默**的（不报错、只是没产物），只有 ②③ 响亮 —— 所以外部报「生成器没生效」时，
> 先问 ①（编译器版本）与 ⑤（触发条件），再看产物报的错。
