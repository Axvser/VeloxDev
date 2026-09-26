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

**判据：** 先问「这个新能力是『哪些类会被处理』变了，还是『处理出来的东西』变了」。前者必须动 `Base/Analizer.cs:82-94`；后者才动 writer。**只动 writer 不动清单 = 新特性永远不触发，且不报错。**

---

## 二、扩展点逐个说

### A. 加一个新的触发特性

三个位置，缺一不可：

1. **`Base/Analizer.cs:82-94` 的 `TriggerAttributes`**（10 条 `readonly string[]`）—— 加一条**元数据名**（`Namespace.Type+嵌套名`，泛型特性带 `` `1 `` 这类元数后缀，照 `:83-86` 的 `WorkflowBuilder+TreeAttribute\`1` 写法）。这是**所有生成器共用的同一张表**。
2. **写或复用 writer**：实现 `Base/ICodeWriter.cs:8-11` 的四个成员（`Initialize` / `CanWrite` / `Write` / `GetFileName`），或继承 `Writers/WriterBase.cs` 只实现它的五个抽象成员（`Writers/WriterBase.cs:219-223`）。
3. **一个生成器类**：`[Generator(LanguageNames.CSharp)]` + `IIncrementalGenerator`，`Initialize` 写 `context.RegisterSourceOutput(Analizer.Filters.Targets(context).Combine(context.CompilationProvider), GenerateSource)`，`GenerateSource` 走 `Filters.Resolve` → `new writer` → `CanWrite()` → `AddSource(writer.GetFileName(), writer.Write())`。照 `MVVM.cs:12-36` 抄最短。

**候选闸门也是硬性的**：`Base/Analizer.cs:160-165` 的 `IsCandidateClass` 要求类是 **`partial`**。不 `partial` ⇒ 该类型不进入流程，**没有诊断**。

### B. 给已有生成器加一种产物形状

- **多生成一个成员**：改对应 writer 的 `GenerateBody()`，或改 `Base/Analizer.cs` 里那组 `MVVM*` 类（它是 MVVM 侧绝大多数模板的所在地：`SetterMode` 分支 `:477-560`、集合订阅 `:646-710`、槽位生命周期 `GenerateWorkflowSlotMembers()` `:756`、`Enumerate{名}Items` 合成 `:775-838`）。
- **多写一份文件**：照 `Writers/AopWriter.cs` 的两产物写法 —— 额外的 `GetExtensionFileName()`/`WriteExtension()` 对**不在 `ICodeWriter` 接口里**，是在生成器类里直接调（`AopProxy.cs:31` 与 `:36` 两句 `AddSource`）。**加第二份产物必须自己保证 hint name 不与第一份撞。**
- **加基础类型/接口**：`GenerateBaseTypes()` 或 `GenerateBaseInterfaces()` 返回数组即可，`Writers/WriterBase.cs:167-168` 会把它们并入用户原有基类型后 `.Distinct()`。
- **改修饰符**：只能改 `Writers/WriterBase.cs:190-217` 的 `FormatModifiers` —— `partial` 必须排在最后，否则生成的声明与原声明被视为不同而报重复定义。

### C. 加一个全新生成器

在一个类里做四件事：`[Generator(LanguageNames.CSharp)]`（`MVVM.cs:12` 等 7 处同形）→ `IIncrementalGenerator` → `Initialize` 注册 → `GenerateSource` 里循环。**不要写 `ISourceGenerator`**（`architecture.md` §二）。

**引用点不用改**：`VeloxDev.Core.Generator.csproj:32` 是按 `bin\$(Configuration)\$(TargetFramework)\$(AssemblyName).dll` 整包进 `analyzers/dotnet/cs`，新增的生成器类自然进包。

### D. 加一个特性元数（以 Theme 为例）

`Theme.cs:32-55` 是 **5 条并列的 `ForAttributeWithMetadataName`**（`ThemeConfigAttribute\`3` … `` `7 ``），`.Collect()` 后逐级 `.Combine(...)`（`:36-40`）。加第 6 个元数要改**两处**：新增一条流 + 把新流 `.Combine` 进 `allClasses`。漏掉 Combine 不会报错，只会让该元数的类**永不生成**。

### E. 改「谁算 AOP 类」这类判定

`Base/AnalizeHelper.cs` 里有两个同名重载：

- `:43-46` `IsAopClass(INamedTypeSymbol)` —— **实际被用的**（`AopInterface.cs:28`、`Writers/AopWriter.cs:24`），走 `Members(symbol)` 扫**所有 partial 声明**。
- `:12-17` `IsAopClass(ClassDeclarationSyntax)` —— **没有任何调用者**（全源 grep 只命中定义本身）。见 §六。

判定用的名字是**裸字符串**比较：`HasAspectOriented`（`:48-53`）判 `attribute.Name.ToString() == NAME_ASPECTORIENTED`（`NAME_ASPECTORIENTED = "AspectOriented"`，`:10`）—— **不是全限定名**。所以任何**恰好叫 `AspectOriented`** 的成员特性（哪怕来自别的命名空间、别的库）都会把类判成 AOP 类。**改用全限定名比较会收紧行为，属于会改变现有输出的改动。**

---

## 三、官方做法 vs 看着能编译、但错的捷径

| 场景 | 官方做法 | 看着能编译、但错的捷径 | 错了会怎样 |
|---|---|---|---|
| 让某个特性触发生成 | 把元数据名加进 `Base/Analizer.cs:82-94` | 只在 writer 里支持该特性 | 类从不进入流程，**不报错、不生成** |
| 让某一家 GUI 生成不同的东西 | **不做** —— 平台差异去改适配器（`Src/Adapters/VeloxDev.<GUI>/`） | 在 writer 里判断平台 / `#if WPF` | 生成器是 `netstandard2.0` 且不引用任何 GUI 程序集，写不出来；就算写出来也违背「契约只写一次」 |
| 把语义信息带进 writer | writer 里用 `Initialize` 收到的**新鲜** `INamedTypeSymbol` 现读 | 给 `Base/Analizer.cs:35-73` 的 `GeneratorTarget` 加 symbol 字段 | 增量下 symbol 指向**陈旧 `Compilation`**，偶发生成错码；只改别的文件才复现 |
| 从 writer 里读 `partial` 的其它声明 | `Base/AnalizeHelper.cs:23-36` 的 `Declarations`/`Members` | 只看 `Initialize` 拿到的那一份 `ClassDeclarationSyntax` | 拆成多个 partial 文件时**静默少生成**；`Writers/AopWriter.cs:19-21` 的注释就是为这条写的 |
| 命名产物 | `GetFileName()` 返回 `{类}_{命名空间}_X.g.cs` | 用 `ToDisplayString()` 拼完事 | 全局命名空间会生成出 `{类}_<global namespace>_Aop`；`Writers/WriterBase.cs:63/72` 更会写出非法的 `namespace <global namespace>;`。**唯一正确的样板是 `Writers/MVVMWriter.cs:850-853`** |
| 加基础接口 | 返回 `GenerateBaseInterfaces()` 数组 | 在 `GenerateBody()` 或模板串里手写 `: IFoo` | 类型声明由 `Writers/WriterBase.cs:92` 一处拼出，模板串里写的会出现在类体里 ⇒ 语法错误 |
| 让生成器报错 | **目前的不变量是「一个诊断都不发」** | 随手 `context.ReportDiagnostic(...)` | 需要自己定义 `DiagnosticDescriptor` 并声明 `SupportedDiagnostics`；本项目已开 `EnforceExtendedAnalyzerRules`（`VeloxDev.Core.Generator.csproj:5`），会额外报 RS1036/RS2008 类规范告警 |
| 发版 | 改 `VeloxDev.Core.Generator.csproj:11` 的 `<Version>` **并且**改 §四那 9 处引用 | 只改 csproj | Debug 走源码仍是对的，Release **静默**还原旧包 —— 本地怎么调都复现不出来 |

---

## 四、改这里的代价（版本锁：9 处必须一起改）

**当前状态：包版本 `9.0.228`，而 9 处引用仍是 `9.0.0` —— 这是刻意的，不要「顺手修平」。** 依据：2026-09-26 的决定 —— 生成器包拉到 `9.0.228`，Core 与其余库统一停在 `9.0.0`，**只保证 Debug 能跑**。Debug 走 `ProjectReference`，包版本号根本不参与；Release 会解析到 NuGet 上已发布的 `9.0.0` 生成器，即**不含这条线上任何后续本地改动的那一版**。所以看到 `9.0.228` 与 `9.0.0` 并存时，第一反应不该是补齐，而是先确认「这一轮是不是仍然只要 Debug」。

`VeloxDev.Core.Generator.csproj:11` 是 `<Version>9.0.228</Version>`；下面 9 处 `PackageReference` 目前全部是 `Version="9.0.0"`：

| # | 文件:行 |
|---|---|
| 1 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:37` |
| 2 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:33` |
| 3 | `Src/Core/VeloxDev.Core.Extension.Test/VeloxDev.Core.Extension.Test.csproj:30` |
| 4 | `Examples/Workflow/Directory.Build.props:10` |
| 5 | `Examples/AOP/WPF/Demo/Demo.csproj:21` |
| 6 | `Examples/AOP/Avalonia/Demo/Demo.csproj:39` |
| 7 | `Examples/MVVM/WPF/Demo/Demo.csproj:21` |
| 8 | `Examples/MVVM/Avalonia/Demo/Demo.csproj:39` |
| 9 | `Examples/MonoBehaviour/WPF/Demo/Demo.csproj:21` |

**为什么每处都要重复写**：Debug 走 `ProjectReference`、Release 走包，而**分析器不随 `ProjectReference` 传递**（`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:30-31` 的注释原文：「Debug 用本地生成器源码，Release 用包。analyzer 不随 ProjectReference 传递，所以用到生成器特性的项目都要各自重复这一对。」）。`Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:30` 又重复了一遍同样的理由。

**两条引用必须互斥**（`Examples/Workflow/Directory.Build.props:4`）：「两者必须互斥，否则生成器执行两次、报重复成员」。改这一对时不要图省事让它们同时生效。

**升版本的完整动作**：`VeloxDev.Core.Generator.csproj:11` → 上表 9 处 `Version=` 全改 → 想清 Release 会不会静默还原旧包（Debug 看不出问题）。路径基准注意 `Examples/Workflow/Directory.Build.props:5` 的说法：基准是导入方项目目录，必须走 `MSBuildThisFileDirectory`。

> **复核口径**：全仓 grep `VeloxDev.Core.Generator` 的引用点就是上表 9 条 `PackageReference` + 同样 9 个文件里的 `ProjectReference`（`VeloxDev.Core.csproj:33`、`VeloxDev.Core.Extension.csproj:32`、`VeloxDev.Core.Extension.Test.csproj:26`、`Examples/Workflow/Directory.Build.props:6`、四个 Demo 的 `:35/:17/:35/:17`、`Examples/MonoBehaviour/WPF/Demo/Demo.csproj:17`）。**「11 处」在树里复核不到；`9.0.153` 也不在任何构建文件里**，只在 `Src/Generators/VeloxDev.Core.Generator/bin/Release/netstandard2.0/VeloxDev.Core.Generator.deps.json:10` 这个构建产物中出现。

---

## 五、联动清单：加一个特性 / 生成器时要同步动哪里

| # | 位置 | 加什么 | 漏了会怎样 |
|---|---|---|---|
| 1 | `Base/Analizer.cs:82-94` | 新特性的元数据名 | **该特性完全无感**，不生成、不报错 |
| 2 | 新 `Xxx.cs`（生成器类，照 `MVVM.cs:12-36`） | `[Generator]` + `IIncrementalGenerator` + `RegisterSourceOutput` | 没有产物 |
| 3 | 新 writer（或复用） | 实现 `Writers/WriterBase.cs` 的抽象成员 | 同上 |
| 4 | 消费项目的引用对 | 若是**新项目**：`ProjectReference`(Debug) + `PackageReference`(Release) 一对 | Release 下不生成；Debug 正常 ⇒ 本地测不出来 |
| 5 | `memory/modules/<消费模块>/extension.md` | 该特性属于哪个消费模块、生成形状 | 下一个 agent 找不到生成逻辑在 `Src/Generators/` 而不是模块目录里 |
| 6 | 裁剪验证 | 跑一次裁剪/AOT 验证（§六） | 新生成的成员可能在裁剪后消失，而本地 Debug 跑不出来 |

**第 1 条与第 5 条是本仓库最容易漏的两处**：第 1 条静默失效，第 5 条要等下一个 agent 在模块目录里白找一遍。

---

## 六、改生成器或裁剪相关代码后，必须跑裁剪验证

**先记事实：探针本体不在本仓库。** `Src/Verification/VeloxDev.TrimProbe/` 在工作区里只剩 `bin/` 与 `obj/`，没有任何 `.cs` 与 `.csproj`；`git ls-files Src/Verification/` 为空；`VeloxDev.slnx` 里没有 `TrimProbe`。**所以这里给不出可复核的探针路径 —— 它在仓库外，本记忆只能记「有这么一条验证线，动生成器后必须走一遍」这件事本身。**

仓库内可复核的**输入侧**锚点：

| 事实 | 依据 |
|---|---|
| `VeloxDev.Core` 显式声明不可裁剪 | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:12` |
| `VeloxDev.Core.Extension` 同上 | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj:8` |
| 裁剪 demo 必须显式 root 住 `VeloxDev.Core.Extension`，因为 ProjectReference 不导入包的 buildTransitive props | `Examples/Workflow/Avalonia Trimmed/Directory.Build.props:7-16` |
| 该 demo 的 root 只对本树生效（别的 Workflow demo 不引用 Core.Extension） | 同上，`:12-13` 的注释 |

**为什么改生成器必须跑它**：生成器改的是「哪些类型/成员存在」，而裁剪删的是「没人引用的类型/成员」—— 两条轴正好作用在同一个集合上（`architecture.md` §六·8 已确认生成器**不产出任何裁剪提示**，所以没有任何自动保护）。**本地 Debug 构建看不出任何问题。**

> **已结案（2026-09-20 复核）**：`Src/Verification/` 里只有一个 `VeloxDev.TrimProbe/`，它底下**只有 `bin/` 与 `obj/`**（两者被通用的 `bin/`、`obj/` 规则忽略）；**目录本身不在任何 ignore 文件里** —— `.gitignore` 共 467 行、第 465 行是空行，四份 `.gitignore` 加 `.git/info/exclude` 都搜不到 `Verification`；往里丢一个 `__probe.txt`，`git status` 报 `??`（未跟踪）而不是忽略。所以 `git ls-files Src/Verification/` 为空的原因是**那里根本没有源文件**，不是被忽略。早先那个「`.gitignore:465`」是把 `check-ignore -v` 对**目录**打出的 `.gitignore:465:<TAB>路径`（pattern 字段整个为空）当成了证据。**结论不变：这里没有可写的代码事实，裁剪探针的本体在仓库外。**

---

## 七、死扩展点 / 已失效的钩子

| 东西 | 位置 | 现状 |
|---|---|---|
| `AnalizeHelper.IsAopClass(ClassDeclarationSyntax)` | `Base/AnalizeHelper.cs:12-17` | **没有调用者**。实际用的是同名的符号重载 `:43-46`（调用点 `AopInterface.cs:28`、`Writers/AopWriter.cs:24`）。语法版只扫**单份声明**的成员，是符号版之前的写法；留着但无效 |
| `Generators.AgentCatalog` | `Src/Core/VeloxDev.Core/obj/Debug/net10.0/generated/VeloxDev.Core.Generator/VeloxDev.Generators.AgentCatalog/VeloxAgentCatalog.g.cs` | **源码里已不存在**。当前 16 个 `.cs` 无此类，当前 Debug 产物 DLL 中 `AgentCatalog` 命中 0 次。`obj/` 里那份 149 KB 是陈旧产物，别拿它当现状 |
| `GenerateBaseTypes()` | `Writers/AopWriter.cs:41`、`Writers/CommandWriter.cs:119`、`Writers/MonoWriter.cs:124`、`Writers/MVVMWriter.cs:866` 返回 `[]` | **不是死点** —— 返回空是合法答案，只有 `Writers/WorkflowWriter.cs:65` 真正用到了它 |
| MVVM 的 View 生成路径 | 原 `Base/Analizer.cs` 的 `IsView` / `GenerateProxy()`（属性、分派、实现三段） | **已整体删除（2026-09-26）**：两处构造一直传 `isView: false`（`Writers/MVVMWriter.cs:105`、`:131`），这条分支从未被走到，于是连同同样没人读的 `modifies` 参数与 `Modifies` 属性一并移除。现在 `MVVMPropertyFactory` 只有 `MVVMPropertyFactory(analyzer)` 一个参数，`Generate()`（原 `GenerateViewModel` 改名）是唯一出口。**要恢复 View 支持，必须同时改构造签名、`Generate()` 与调用点** —— 别再只加参数不加分支 |
| `VeloxDev.Core.Generator.targets` 的版本门槛 | `VeloxDev.Core.Generator.targets:8-19` | **活着，但条件刻意放宽**：`RoslynVersion` 为空时**跳过检查**（`:13-15` 的注释：现代宿主上的 netframework TFM 拿不到该属性，跳过以免误报）。所以这条诊断**不会**在每个项目上都出现 |
