# AI — 扩展

> 代码：`Src/Core/VeloxDev.Core/AI/`（命名空间 `VeloxDev.AI`）。
> 消费方：`Src/Core/VeloxDev.Core.Extension/Agent/`（同一个命名空间树下）。**单向依赖：Extension → Core。**
> 平台差异：**本模块没有平台轴**（无 GUI 依赖），所以没有 `adapters/`。
>
> 本文只写「怎么走正确的路」。每条推论的机制依据在 `architecture.md`，此处只给锚点不重复推导。

---

## 一、扩展点地图

| 我要扩展… | 扩展点 | 具体位置 |
|---|---|---|
| 给类型/属性/方法/命令补说明文字 | `[AgentContext]` | 声明 `AgentContextAttribute.cs:5`；**运行期没有读取点** —— 生成器在编译期把它渲染成 `AgentText` 存进目录；语言与回退规则唯一在 `AgentTextSelection.Select`（`AgentText.cs`），节点侧入口 `AIContextMembers.DescriptionsFor`，`Type`/`MemberInfo` 侧入口 `AgentContextReader`（`architecture.md` §二） |
| 声明一个命令要吃什么参数 | `[AgentCommandParameter]` | 声明 `AgentCommandParameterAttribute.cs:10`；**只有生成器读**（`AIContextModelBuilder.ReadCommandParameterType`），存成节点上一条 `CommandParameterType` 引用 |
| 限定 `SlotEnumerator<TSlot>` 允许哪些 selector 类型 | `[SlotSelectors]` | 声明 `SlotSelectorsAttribute.cs:38`；**Core 零读取**（生成器读，存成 `SlotSelectorType` 引用），判断全在消费方 |
| 控制 Agent 能写哪些属性 | `rejected` 集合 | `AgentPropertyAccessor.SetProperties`；由消费方构造时传（`Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:34`） |
| 加一个语言 | `AgentLanguages` 枚举 + 码表 + 两处 switch | `AgentLanguages.cs:4-40`、`:45-88`、`:103`（`ToLanguageCode`）、`:186`（`GetDisplayName`）；另有消费方第二张码表（见 §四·1） |
| 把一个新的对象整体暴露成工具面 | `AsAgentToolkit()` / `AsAgentTools()` | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:362`、`:371` |
| 给确认/选择接自己的交互 | `Func<..., Task>` handler | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:726`、`:753` |
| 加一种新的「特性 + 反射读取」能力 | 特性类 + **一个读取助手** | 见 §二·6 —— 说明文字的语言选取规则收敛在 `AgentTextSelection.Select`，新助手照 `AgentContextReader`/`AIContextMembers.DescriptionsFor` 转调它，别再复制过滤条件 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. `[AgentContext]` 的位置

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 把 `[AgentContext]` 写在基类上，指望派生类的属性/方法带下来 | 属性与方法**不继承**：目录按类型分别收录成员，派生类型读自己那份（基类的成员只能通过基类链被"发现"，不会把说明挪到派生类型的同名成员上） | `AIContextModelBuilder.ReadMembers`（只读 `symbol.GetMembers()`） |
| 给**命令**补说明时写在具体类的属性上 | 对 `ICommand` 而言**接口上的才算数** —— 生成器按同名属性回查接口，接口上写了就用接口的（与旧反射路径先扫接口一致） | 生成器 `AIContextModelBuilder.InterfaceCommandProperty` |
| 给**普通属性/方法**补说明时写在接口上 | **不生效**：只有命令那条路会回查接口，属性/方法不扫接口（旧反射路径也一样） | 同上，`InterfaceCommandProperty` 只在 `isCommand` 时调用 |
| 只写英文，就以为中文/日文界面下这个成员没有说明 | **有语言回退，但是整目标、全有或全无**：该目标一条目标语言都没有时整体退回英文；只要命中 ≥ 1 条目标语言，就不再夹带英文 | `AgentTextSelection.Select`（`AgentText.cs`）；测试 `.../AgentContextReaderTests.cs` 的 `..._FallsBackToEnglish` / `..._FallbackIsAllOrNothing` |
| 只写了中文，指望英文界面也能看到 | **英文无处可退**：`language == English` 时直接返回命中集（可能为空） | 同上；测试 `..._EnglishRequestNeverFallsBack` |
| 写 `[AgentContext("说明")]` | **编译不过**：位置参数第一位是 `AgentLanguages` | `AgentContextAttribute.cs:5` |

**官方**：命令的说明与参数类型标在**接口**上（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:37-41` 那一族是范本）；其它成员标在**声明它的那个类**上。想支持几种语言就写几条 —— 但**不必为回退而写**：整目标缺该语言时会自动退回英文。

### 2. 暴露一个命令

**官方**：一个 `ICommand` 类型的公开属性 +（可选）`[AgentCommandParameter(typeof(T))]`。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 以为 `canExecute: false` 会挡住执行 | **`CanExecute` 只被报告，从不被强制** —— `Execute` 不查它，消费方的 `ExecuteCommand` 也不查。生成器那条 `if (!c.CanExecute(parameter)) return false;` 已经删掉（它曾经与这条不变量矛盾） | `AgentCommandDiscoverer.cs` 的 `Execute` / `CanExecuteCommand`；`.../Agent/AgentObjectToolkit.cs:232`、`:247` |
| 用 `FindBackingCommand` 当通用的「属性→命令」映射 | 它只认两种命名：`Set{X}Command` 与 `{X}Command`；别的命名返回 `null`（调用方通常据此当成「没有命令」）。形参仍是 `Type`，但只读它的 `FullName` 查目录 | `AgentCommandDiscoverer.FindBackingCommand` |
| 用 `Execute(target, "saveCommand")` 之类的大小写变体 | 规范化是 `EndsWith("Command")`，大小写敏感 → 拼成 `saveCommandCommand` 然后找不到 | `AgentCommandDiscoverer.NormalizeCommandName` |
| 让命令属性抛异常 | 异常被吞成 `ExecuteResult.Error` 字符串，**栈不保留**（只留 `ex.Message`） | `AgentCommandDiscoverer.cs` 的 `Execute` 的 try/catch |
| 把命令体写成**显式接口实现** | 目录只收录公开成员，显式实现的那个属性不算 —— 命令整个消失（旧反射路径也读不到它） | `AIContextModelBuilder.ReadMembers` 的 `Accessibility.Public` 过滤 |

### 3. 让 Agent 改属性

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用 `SetProperties` 的 `rejected` 做通配/前缀屏蔽 | 它是**精确名字**比对的 `ISet<string>`，没有模式匹配 | `AgentPropertyAccessor.SetProperties` |
| 用 `CopyScalarProperties` 做「同名字段对拷」并期待类型自适应 | 名不副实：它转调访问器的 `CopyScalarFrom`，搬的是**两个类型共有的、可写的全部成员**（不再限标量），类型不同就在生成代码里硬转失败、被吞掉。**零生产调用者** —— 真正跑的是 `ComponentPatcher` 那份自带白名单的分叉 | `AgentPropertyAccessor.CopyScalarProperties`；`.../Functions/ComponentPatcher.cs` |
| 靠 `SetPropertyValue` 报错来兜住非法值 | 它查目录拿 `CanWrite` 再写：枚举**能从字符串（`ignoreCase`）或数字进来**，其它不受支持的转换会抛、并被吞成 `SetResult.Error`（不会有异常逃出到调用方）。「不在目录里」与「只读」现在是两条不同的错误文案 | `AgentPropertyAccessor.SetPropertyValue`；`AIContextConvert` |
| 以为写一个**不在目录里**的属性会悄悄失败 | 会失败，但原因是闭世界：**类型本身**不在目录里时，连属性列表都是空的 | `AIContextMembers.TypeNameOf`；`architecture.md` §七 |

### 4. 方法调用

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 以为 `Invoke` 会按**类型**挑重载 | 它按**实参个数**挑：生成期排好的元数表里，精确元数优先，其余元数由「必填数 ≤ 个数 < 总数」的那个重载接手。同元数的重载分不开 | 生成器 `Writers/AIContextTreeWriter.cs` 的 `ArityPlan` |
| 以为传少了参数会失败 | 缺的尾部可选参数由生成代码**省略实参**、编译器补默认值 —— 这条是保住的 | 同上，`WriteInvokeBody` |
| 传一个目录里认不出的参数类型 | 转换表就是生成期那张 `RenderConversion`（`AIContextConvert`）：字符串 / 数字 / bool / 日期 / `Guid` / `TimeSpan` / 枚举，其余一律 `(T)value!` 硬转，转不过就返回一句错误 | `AgentMethodInvoker.cs` 的 `Invoke` |
| 用 `DiscoverMethods` 的输出当「唯一的方法集合」 | **一个方法名只报一条**（目录按名字去重），虽然 `Invoke` 支持该名字的全部元数 | `AgentMethodInvoker.DiscoverMethods` |
| 在目录里找**静态**方法 | 没有：目录不录静态方法，访问器只对实例做事。`InvokeStatic` / `includeStatic` 已删除，没有回退 | `architecture.md` §五·6 |

**官方**：枚举参数现在能直接传（`AIContextConvert.ToEnum<T>`，按名字或底层值）。需要更复杂的参数时，仍然推荐给目标对象加一个**收 `string`/`int` 的包装方法** —— 那让转换发生在你自己的代码里，比依赖生成期那张表可控。

### 5. `[SlotSelectors]`

**官方**：用 **`typeof` 构造**（`[SlotSelectors(typeof(MyEnum))]`）。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用字符串构造指望跨程序集能解析 | 字符串形式**能编译**，但白名单校验是**对字符串的 Ordinal 精确比对**：`IsEnumTypeAllowed` 拿 `selectorType.FullName` 去比生成期记下的 `SlotSelectorType` 引用名（`.../Functions/WorkflowAgentToolkit.cs:2913-2928`），生成的类型名要与所写的字符串**逐字符相同**才行。没有任何 `Type.GetType` 或程序集扫描（全仓已零反射），`AgentTypeResolver.ResolveType` 也是闭世界（只查目录，`AgentTypeResolver.cs:25`） | `.../Functions/WorkflowAgentToolkit.cs:2913-2928`；对照 `AgentTypeResolver.cs:25`、`.../Functions/TypeIntrospector.cs:30` |
| 给 `[SlotSelectors]` 属性直接 patch | 消费方**按代码硬拒** | `.../Agent/Workflow/Functions/ComponentPatcher.cs:127-135` |
| 以为空数组 = 什么都不允许 | 两个集合都空时语义是**「任意类型都接受」** | `SlotSelectorsAttribute.cs:42`、`:49` |

### 6. 新增一种「特性 + 反射读取」

**官方**：特性类 + **生成期的一个读取点**。特性本身在运行期已经没有任何读者 —— 说明文字在编译期被渲染成 `AgentText` 存进目录，运行期只是取值，规则唯一在 `AgentTextSelection.Select`（`AgentText.cs`）。两条取值入口都是转调：节点侧 `AIContextMembers.DescriptionsFor(node, language)`（三个助手用），`Type`/`MemberInfo` 侧 `AgentContextReader`（消费方与表格用）。要新增一种读取语义（「回退英文」正是上一次的例子）改 `Select` 一处即可；**新写一个助手时要照这个形状走**，再把过滤条件抄一遍就又回到了「漏一处没有编译错误、只有行为不一致」的老问题。

**新增一个标注特性**（比上面多一步）：特性类 + `AIContextModelBuilder` 里的一处读取 + 目录节点上的承载方式（要么进 `AIContextFlags`，要么进 `AIContextRefKind`）—— 三个特性各自是这三样的一种组合，照抄最近的那个。

### 7. 「我改 Core 的助手，为什么跑起来没变化」

**官方**：先确认你要改的行为归**哪条管线**。命令的发现/执行与属性的写入各有两个分叉实现，Core 是最通用那条、不是唯一那条：

| 行为 | Core 那条 | Workflow 那条 |
|---|---|---|
| 列出/执行命令 | `AgentCommandDiscoverer.DiscoverCommands` / `Execute`（读目录） | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/CommandInvoker.cs`（**也读目录**了，但仍是一份独立实现：自带 `CommandDescriptor`、参数走生成的 `VeloxJsonSerializer` 反序列化、不按语言过滤） |
| 批量写属性 | `AgentPropertyAccessor.cs:164` | `.../Workflow/Functions/ComponentPatcher.cs`（`ApplyPatch` `:41`） |
| 标量对拷 | `AgentPropertyAccessor.CopyScalarProperties`（**零生产调用者**；转调访问器，搬全部可写成员） | `.../Workflow/Functions/ComponentPatcher.cs:256`（自带标量白名单，实际跑的是这份） |

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 在 Core 里改命令的命名规范化/语言过滤，期待 Workflow 工具跟着变 | Workflow 工具走 `CommandInvoker`，不与 Core 共享任何一行实现 —— 改 Core 只影响 `AgentObjectToolkit`（通用对象）那条路 | `AgentObjectToolkit.cs:247` vs `WorkflowAgentToolkit.cs:1045` |
| 以为 Core 的转换表也管着 Workflow | 两条各有各的：Core 走**生成期那张表**（`AIContextConvert`，需要重新生成才会变），Workflow 那条走 `AgentJsonValue.Convert(VeloxJsonValue.Parse(json), paramType)`（运行期，改了立刻生效） | `CommandInvoker.cs:91` vs `AIContextConvert.cs` |
| 以为「命令描述符」是同一个类型 | 有两个同名类：Core 的嵌套 `AgentCommandDiscoverer.CommandDescriptor` 与 `VeloxDev.AI.Workflow.Functions.CommandDescriptor`（`CommandInvoker.cs:122`）；字段也不同（后者带 `Descriptions` 的 `KeyValuePair<AgentLanguages,string>`） | 同上 |

**要一起改的两处**（改命令语义时）：`AgentCommandDiscoverer.cs`（通用路径）与 `CommandInvoker.cs`（Workflow 路径）。反过来说，**只**想要 Workflow 行为变、通用路径不变，也是可行的 —— 那就只改后者。

---

## 三、步骤清单

### A. 给一个成员补 Agent 说明

1. 定位它**被读取的方式**：命令 → 写在**接口**上；属性/方法/类型 → 写在**声明类**上。
2. 每种要支持的语言各写一条 `[AgentContext(AgentLanguages.X, "…")]`（同一语言可多条，全部会返回）。
3. 命令若带参数，另外补 `[AgentCommandParameter(typeof(T))]` —— 参数类型**只从这里来**，没有按名字猜后备方法的启发式：写在实现类属性上就用它，写在接口上由生成器按同名属性回查、复制到实现类的命令节点（`AIContextModelBuilder.InterfaceCommandProperty`，`AIContextModel.cs:845`）。
4. 自检：用目标语言调一次 `AgentContextReader.GetContexts(...)`。返回空数组 = 该目标**既没有目标语言、也没有英文**标注；只写英文时返回的是英文那几条（回退），不是空，也不是「两种语言都有」。

### B. 暴露一个新命令

1. 目标类型（通常是接口）上加一个 `ICommand` 公开属性，命名以 `Command` 结尾。
2. 需要参数就在同一位置加 `[AgentCommandParameter(typeof(T))]`；参数类型由访问器的 `ParameterType(commandName)`（`typeof` 字面量）取回给生成式序列化器，所以它得是目录/序列化契约认得的类型。
3. 不要在 `CanExecute` 上寄托拦截 —— 要拦就在工具层拦。
4. 命令体抛出的异常会变成一句错误字符串，**要保栈要自己 catch 后写日志**（`AgentCommandDiscoverer.Execute` 的 try/catch）。
5. 别写成**显式接口实现**：目录只收公开成员，显式实现的命令整个不出现。

### C. 给 `SlotEnumerator` 属性加白名单

1. 在 `SlotEnumerator<TSlot>` 属性（或它的后备字段）上标 `[SlotSelectors(typeof(A), typeof(B))]` —— **用 typeof**。
2. 消费方两处会读它：`ListSlotProperties` 输出白名单（`.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1258`）、`SetEnumSlotCollection` 的校验（`:1413`、`:1466`）。
3. `ComponentPatcher` 会自动拒绝直接 patch 这个属性，**不需要额外注册**（`.../Agent/Workflow/Functions/ComponentPatcher.cs:127`）。

### D. 把一个新的对象类型整体暴露成工具面

0. **目标类型必须先被生成器收录**（闭世界）：它自己被 `[AgentContext]` 标注过，或它的某个成员带标注，或它有一个 `ICommand` 属性，或它实现了四个工作流组件接口之一。否则 10 个工具照样建出来，但每一个都只回「不在目录里」。
1. 消费方调用 `obj.AsAgentToolkit(language, rejectedProperties)`（`.../Agent/AgentObjectToolkit.cs:362`），得到 10 个基础工具（`CreateTools` `:54`，工具表 `:61-70`）。
2. `rejectedProperties` 只影响 `PatchProperties` 一路，**不影响** `SetProperty`（单属性写，`:185`）—— 要屏蔽必须两条都走 `rejected`，或不在工具面暴露写工具。核对：`SetProperty` 调 `SetPropertyValue`（无 rejected 参数）。
3. 目标若绑在某条 UI 线程上，**必须自己设 `ToolPipeline.MarshalTo`**：Core 不注册任何编组（`architecture.md` §一表）。

### E. 加一个语言

见 §四·1 的联动表 —— 这条没有「只改一处」的走法。

---

## 四、联动清单

**读法**：下表每行都是一处必须同步改的地方。漏掉通常**不报错**，而是静默少一个能力。

### 4.1 加一个 `AgentLanguages` 值

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `AgentLanguages.cs:4-40` 加枚举值（注意值必须唯一，`Chinese` 是别名） | —— |
| 2 | `AgentLanguagesExtensions.ToLanguageCode` 的 switch（`:103-140`） | 该语言调 `ToLanguageCode()` **抛** `ArgumentOutOfRangeException` |
| 3 | `AgentLanguagesExtensions.GetDisplayName` 的 switch（`:186-223`） | 同上，提示词渲染时抛 |
| 4 | `LanguageCodeMap`（`:45-88`） | `TryParseLanguageCode` 永远认不出该语言，**静默返回 false** |
| 5 | 消费方的第二张码表 `Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs:38-39`（`ToLangCode`，公开包装 `:46`） | 它的判断是 `language == AgentLanguages.Chinese ? "zh" : "en"`，而 `Chinese` 是 `ChineseSimplified` 的别名（`AgentLanguages.cs:8`）⇒ **只有简中进 `zh` 目录，繁体中文也走 `"en"`**；新语言同样静默走英文目录 |
| 6 | 每个作者的 `[AgentContext]` | 该语言下这个成员的说明**退回英文**（只有在连英文都没写时才是空数组） |
| 7 | 嵌入式资源目录（今天只有 `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/en|zh/` 两个，32 个 .md） | 该语言的技能/参考文件不存在 → 静默回退英文。目录约定见 `.../Agent/AgentEmbeddedResources.cs:9-16`，读法见 `.../Agent/Skills/EmbeddedSkillSource.cs:103` |

**两张同名不同义的 `ToLanguageCode` 都在 `VeloxDev.AI` 命名空间里**：`AgentLanguagesExtensions.ToLanguageCode`（扩展方法，BCP-47 风格，33 个出口）与 `AgentEmbeddedResources.ToLanguageCode`（普通静态，只有 `zh`/`en`）。因为前者是扩展方法，`lang.ToLanguageCode()` 永远解析到前者 —— 所以**没有编译期歧义，也没有任何提示**告诉你这里有两套码。

### 4.2 加一个 `[AgentContext]` 的读取点（新助手 / 新发现器）

**转调 `AgentTextSelection.Select(...)` 就完了** —— 语言与回退规则只在那一个方法里。今天的两条取值入口是 `AIContextMembers.DescriptionsFor(node, language)`（三个助手用）与 `AgentContextReader`（`Type`/`MemberInfo` 用），都是转调。**不要**在助手内部再写一次 `Where(a => a.Language == language)`：那正是这次修掉的历史形态，它会静默地不回退。

### 4.3 给发现器加一个字段（例如 `CommandDescriptor` 上再挂一个属性）

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `AgentCommandDiscoverer.CommandDescriptor` + `DiscoverCommands` 里的填充 | 描述符上多出来的字段**必须有一个数据来源**：目录里没有的事实，得先加生成器（`AIContextFlags` 或 `AIContextRefKind`），Core 这边才有东西可读 |
| 2 | 消费方的工具输出 `.../Agent/AgentObjectToolkit.cs:222-239`（`ListCommands`）/ `.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的对应 JSON 对象 | Core 加了字段，工具输出里**不会自动出现** |

### 4.4 加一个 `SlotSelectors` 的消费者

只有消费方要动（`.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1413`、`:1466` 校验，`:1258` 描述，`.../Agent/Workflow/Functions/ComponentPatcher.cs:127` 拒绝）。**Core 一行都不用改**。

### 4.5 加一个事件负载字段

`AgentConfirmationEventArgs` / `AgentSelectionEventArgs` / `AgentToolCallEventArgs` 是纯 DTO，Core 里**没有任何生产者** —— 全是消费方 `new` 出来的（`.../Agent/Workflow/WorkflowAgentScope.cs:757`、`:730`、`:787`；`.../Agent/AgentObjectToolkit.cs:118`）。加字段要同步：七家 demo 的对话框实现 + 消费方 handler 的包装层。

### 4.6 本模块**不需要**联动的东西（省掉无谓的搜索）

- ~~**没有生成器**~~ —— **2026-10-03 起不但有，而且它就是这套东西的行为来源。** `Src/Generators/VeloxDev.Core.Generator/AIContextTree.cs` 往每个程序集里加一个只读的上下文分片与一组访问器（见 `architecture.md` §七），五个助手全部读它。**所以「哪些成员进 Agent 面」「命令叫什么」「说明与参数类型从哪来」这些问题要改生成器，改 Core 的助手改不动它们**；反过来，改助手的拒绝文案/错误形状只动 Core。两条交叉线：命名规则抽在生成器的 `Base/AIContextNaming.cs`（`MVVMFieldAnalizer` 与 `CommandWriter` 都转调它），成员遍历抽在 Core 的 `AIContextDirectory.MembersAcross`（渲染器与三个助手都转调它）。
- **Release 构建用的是 NuGet 上的 `VeloxDev.Core.Generator` 包，不是本地源码**（`Condition="'$(Configuration)' != 'Debug'"`）。生成器改了而包没重发，Release 下的目录仍是旧形状 —— Debug（含 `dotnet test` 默认配置）才跑本地生成器。
- **没有平台适配器**：加一家 GUI 不需要在本模块改任何一行；交互 UI 归各 demo。
- **没有序列化契约**：`AgentLanguages` 是 `byte` 枚举，但没有任何地方对它做自定义序列化；跨进程传的是它自己的值。

---

## 五、死扩展点与零调用者

以下成员**在整个 `Src/` 与 `Examples/` 里只有测试调用，或连测试都没有**（各行已标明）——不要把它们当成「有消费方在依赖的契约」：

| 成员 | 位置 | 说明 |
|---|---|---|
| `AgentPropertyAccessor.CopyScalarProperties` | `AgentPropertyAccessor.cs:197` | 零生产调用者（转调访问器的 `CopyScalarFrom`）；**消费方另写了一份同名实现在 `.../Agent/Workflow/Functions/ComponentPatcher.cs:256`**（自带标量白名单，没有转调 Core）—— 改 Core 那份不会影响实际跑的路径 |
| `IAIContextAccessor.CopyScalarFrom` | 生成代码里，每个访问器一份 | 目前唯一的调用者就是上面那个零调用者的包装（`AgentPropertyAccessor.cs:201`）—— 等到 `ComponentPatcher` 那条分叉也搬过来才有真实用途 |
| `AgentContextReader.HasAgentContext` | `AgentContextReader.cs:52` | 零非测试调用者 |
| `AgentLanguagesExtensions.ParseLanguageCode` | `AgentLanguages.cs:175` | **零调用者**（连测试都没有；测试用的是 `TryParseLanguageCode`）。`ToLanguageCode`/`GetDisplayName` 相反，消费方在用（`.../Agent/Workflow/WorkflowAgentScope.cs:1220`、`:1221`） |
| `IAgentConfirmationNotifier` / `IAgentSelectionNotifier` | `AgentConfirmationEventArgs.cs:37`、`AgentSelectionEventArgs.cs:60` | **全仓零实现者**。三个 `IAgent*Notifier` 里只有 `IAgentToolCallNotifier` 被实现了（`.../Agent/AgentObjectToolkit.cs:34`、`.../Agent/Workflow/WorkflowAgentScope.cs:22`）。确认/选择的实际通路是消费方的 `Func<..., Task>` handler —— 因为 `EventHandler` 没法表达「等用户点完」 |

**已经不在这个表里的**（2026-10-03 删除或改变了地位）：`AgentMethodInvoker.InvokeStatic`（删除）、`DiscoverMethods(includeStatic:)`（删除）、`AgentCommandDiscoverer.CanExecuteCommand`（不再零调用者 —— `DiscoverCommands` 与它自己都走它）。

唯一有非测试调用者的替身入口仍是 `AgentCommandDiscoverer.FindBackingCommand`，调用者是 `.../Agent/Workflow/Functions/ComponentPatcher.cs:244` 的一层薄委托。
