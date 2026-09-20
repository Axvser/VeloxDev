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
| 给类型/属性/方法/命令补说明文字 | `[AgentContext]` | 声明 `AgentContextAttribute.cs:4`；读取入口 `AgentContextReader.GetContexts`（类型 / 成员两个重载），规则在私有 `Select`；四个助手转调它（`architecture.md` §二） |
| 声明一个命令要吃什么参数 | `[AgentCommandParameter]` | 声明 `AgentCommandParameterAttribute.cs:10`；**Core 里唯一读取点** `AgentCommandDiscoverer.cs:223` |
| 限定 `SlotEnumerator<TSlot>` 允许哪些 selector 类型 | `[SlotSelectors]` | 声明 `SlotSelectorsAttribute.cs:38`；**Core 零读取**，全在消费方 |
| 控制 Agent 能写哪些属性 | `rejected` 集合 | `AgentPropertyAccessor.SetProperties`（`AgentPropertyAccessor.cs:138`）；由消费方构造时传（`Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:29`） |
| 加一个语言 | `AgentLanguages` 枚举 + 码表 + 两个 switch | `AgentLanguages.cs:3-39`、`:43-86`、`:88`、`:163`；另有消费方第二张码表（见 §四·1） |
| 把一个新的对象整体暴露成工具面 | `AsAgentToolkit()` / `AsAgentTools()` | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:340`、`:349` |
| 给确认/选择接自己的交互 | `Func<..., Task>` handler | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:494`、`:521` |
| 加一种新的「特性 + 反射读取」能力 | 特性类 + **一个读取助手** | 见 §二·6 —— 说明文字的规则已经收敛到 `AgentContextReader`，新特性照它写，别再复制过滤条件 |

---

## 二、官方做法 vs 看着能编译、但错的捷径

### 1. `[AgentContext]` 的位置

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 把 `[AgentContext]` 写在具体类上，指望接口/基类的能带下来 | 每次读取都传 `inherit: false`，**没有继承**；而且属性/方法路径**不扫接口** | `AgentContextReader.cs:20`、`:27`、`:34`；`AgentMethodInvoker.cs:78`；`AgentPropertyAccessor.cs:63` |
| 只写英文，就以为中文/日文界面下这个成员没有说明 | **有语言回退，但是整目标、全有或全无**：该目标一条目标语言都没有时整体退回英文；只要命中 ≥ 1 条目标语言，就不再夹带英文 | `AgentContextReader.cs:63-67`；测试 `.../AgentContextReaderTests.cs` 的 `..._FallsBackToEnglish` / `..._FallbackIsAllOrNothing` |
| 只写了中文，指望英文界面也能看到 | **英文无处可退**：`language == English` 时直接返回命中集（可能为空） | `AgentContextReader.cs:64`；测试 `..._EnglishRequestNeverFallsBack` |
| 写 `[AgentContext("说明")]` | **编译不过**：位置参数第一位是 `AgentLanguages` | `AgentContextAttribute.cs:4` |
| 给命令补说明时写在具体类的属性上 | 对 `ICommand` 而言**接口上的才算数**（先扫接口 + 按名字去重，具体类同名的那个根本不会被扫到） | `AgentCommandDiscoverer.cs:64-88`、`:90-94` |

**官方**：命令的说明与参数类型标在**接口**上（`Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:33` 那一族是范本）；其它成员标在**声明它的那个类**上。想支持几种语言就写几条 —— 但**不必为回退而写**：整目标缺该语言时会自动退回英文。

### 2. 暴露一个命令

**官方**：一个 `ICommand` 类型的公开属性 +（可选）`[AgentCommandParameter(typeof(T))]`。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 以为 `canExecute: false` 会挡住执行 | **`CanExecute` 只被报告，从不被强制** —— `Execute` 不查它，消费方的 `ExecuteCommand` 也不查 | `AgentCommandDiscoverer.cs:123-154`、`:159-168`；`.../Agent/AgentObjectToolkit.cs:225`、`:240` |
| 用 `FindBackingCommand` 当通用的「属性→命令」映射 | 它只认两种命名：`Set{X}Command` 与 `{X}Command`；别的命名返回 `null`（调用方通常据此当成「没有命令」） | `AgentCommandDiscoverer.cs:176-195` |
| 用 `Execute(target, "saveCommand")` 之类的大小写变体 | 规范化是 `EndsWith("Command")`，大小写敏感 → 拼成 `saveCommandCommand` 然后找不到 | `AgentCommandDiscoverer.cs:199-200` |
| 让命令属性抛异常 | 异常被吞成 `ExecuteResult.Error` 字符串，**栈不保留**（只留 `ex.Message`） | `AgentCommandDiscoverer.cs:148-151` |

### 3. 让 Agent 改属性

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用 `SetProperties` 的 `rejected` 做通配/前缀屏蔽 | 它是**精确名字**比对的 `ISet<string>`，没有模式匹配 | `AgentPropertyAccessor.cs:149` |
| 用 `CopyScalarProperties` 做「同名字段对拷」并期待类型自适应 | 白名单只有 11 种标量 + 枚举，**且不做转换**：类型不同的同名属性会在 `SetValue` 抛然后被吞 | `AgentPropertyAccessor.cs:183-185`、`:187-188` |
| 靠 `SetPropertyValue` 报错来兜住非法值 | 它先 `ConvertValue` 再写：枚举**能从字符串（`ignoreCase`）或数字进来**，其它不受支持的转换会抛、并被吞成 `SetResult.Error`（不会有异常逃出到调用方） | `AgentPropertyAccessor.cs:196-218`、`:124-127` |

### 4. 方法调用

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 以为 `Invoke` 会按**类型**挑重载 | 它按**实参个数**挑，同个数重载取反射枚举到的第一个（顺序未定义） | `AgentMethodInvoker.cs:119-121` |
| 经 `InvokeMethod` 传枚举参数 | `Convert.ChangeType` 认不了枚举 → 被 `catch {}` 吞掉 → 最终以参数不匹配失败。JSON 整数是 `Int64`，必然踩 | `AgentMethodInvoker.cs:144-146`、`:149` |
| 以为 `InvokeStatic` 和 `Invoke` 一样会补默认值/转类型 | 它两样都不做；匹配失败还会**随便挑一个同名重载**再抛 | `AgentMethodInvoker.cs:165-198`、`:180` |
| 用 `DiscoverMethods` 的输出当「唯一的方法集合」 | 它按 `MethodInfo` 逐条输出，**同名的每个重载各占一条** | `AgentMethodInvoker.cs:67-91` |

**官方**：需要枚举/复杂参数时，给目标对象加一个**收 `string`/`int` 的包装方法**，让转换发生在你自己的代码里 —— 不要指望反射层代劳。

### 5. `[SlotSelectors]`

**官方**：用 **`typeof` 构造**（`[SlotSelectors(typeof(MyEnum))]`）。

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 用字符串构造指望跨程序集能解析 | 收集端解析字符串用的是**裸 `Type.GetType(name, throwOnError: false)`** —— 不做程序集扫描；而 Core 的 `AgentTypeResolver.ResolveType`（会扫全部已加载程序集）**不被这条路径调用** | `.../Agent/Workflow/WorkflowAgentScope.cs:863-878`；对照 `AgentTypeResolver.cs:21`、`.../Functions/TypeIntrospector.cs:12` |
| 给 `[SlotSelectors]` 属性直接 patch | 消费方**按代码硬拒** | `.../Agent/Workflow/Functions/ComponentPatcher.cs:128-135` |
| 以为空数组 = 什么都不允许 | 两个集合都空时语义是**「任意类型都接受」** | `SlotSelectorsAttribute.cs:42`、`:49` |

### 6. 新增一种「特性 + 反射读取」

**官方**：特性类 + **一个读取助手**，读取语义（语言、回退、inherit）写在那一个助手里。说明文字的这条规则已经从「内联复制在每一处」改成了集中实现 —— `AgentCommandDiscoverer`（`:73`、`:94`）、`AgentMethodInvoker`（`:78`）、`AgentPropertyAccessor`（`:63`）现在都只是转调 `AgentContextReader.GetContexts(member, language)`（`PropertyInfo`/`MethodInfo` 都是 `MemberInfo`），过滤条件只在 `AgentContextReader.Select`（`:59`）里有一份。新增一种读取语义（「回退英文」正是这次的例子）改 `Select` 一处即可；**新写一个助手时要照这个形状走**，再把过滤条件抄一遍就又回到了「漏一处没有编译错误、只有行为不一致」的老问题。

### 7. 「我改 Core 的助手，为什么跑起来没变化」

**官方**：先确认你要改的行为归**哪条管线**。命令的发现/执行与属性的写入各有两个分叉实现，Core 是最通用那条、不是唯一那条：

| 行为 | Core 那条 | Workflow 那条 |
|---|---|---|
| 列出/执行命令 | `AgentCommandDiscoverer.cs:54`、`:123` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/CommandInvoker.cs:21`、`:81` |
| 批量写属性 | `AgentPropertyAccessor.cs:138` | `.../Workflow/Functions/ComponentPatcher.cs`（`ApplyPatch`） |
| 标量对拷 | `AgentPropertyAccessor.cs:169`（无调用者） | `.../Workflow/Functions/ComponentPatcher.cs:236` |

| 捷径 | 为什么错 | 依据 |
|---|---|---|
| 在 Core 里改命令的命名规范化/语言过滤，期待 Workflow 工具跟着变 | Workflow 工具走 `CommandInvoker`，不与 Core 共享任何一行实现 —— 改 Core 只影响 `AgentObjectToolkit`（通用对象）那条路 | `AgentObjectToolkit.cs:240` vs `WorkflowAgentToolkit.cs:921` |
| 以为 Core 的 `Convert.ChangeType` 限制也管着 Workflow | Workflow 那条用 `JsonConvert.DeserializeObject(json, paramType)`，**枚举参数能进** | `CommandInvoker.cs:107` vs `AgentMethodInvoker.cs:144` |
| 以为「命令描述符」是同一个类型 | 有两个同名类：Core 的嵌套 `AgentCommandDiscoverer.CommandDescriptor` 与 `VeloxDev.AI.Workflow.Functions.CommandDescriptor`（`CommandInvoker.cs:171`）；字段也不同（后者带 `Descriptions` 的 `KeyValuePair<AgentLanguages,string>`） | 同上 |

**要一起改的两处**（改命令语义时）：`AgentCommandDiscoverer.cs`（通用路径）与 `CommandInvoker.cs`（Workflow 路径）。反过来说，**只**想要 Workflow 行为变、通用路径不变，也是可行的 —— 那就只改后者。

---

## 三、步骤清单

### A. 给一个成员补 Agent 说明

1. 定位它**被读取的方式**：命令 → 写在**接口**上；属性/方法/类型 → 写在**声明类**上。
2. 每种要支持的语言各写一条 `[AgentContext(AgentLanguages.X, "…")]`（同一语言可多条，全部会返回）。
3. 命令若带参数，另外补 `[AgentCommandParameter(typeof(T))]` —— 它和 `[AgentContext]` 的扫描规则**不一样**：`FindParameterAttribute` 会依次找接口 → 具体属性 → 去掉 `"Command"` 的**后备方法**（`AgentCommandDiscoverer.cs:223-242`）。
4. 自检：用目标语言调一次 `AgentContextReader.GetContexts(...)`。返回空数组 = 该目标**既没有目标语言、也没有英文**标注；只写英文时返回的是英文那几条（回退），不是空，也不是「两种语言都有」。

### B. 暴露一个新命令

1. 目标类型（通常是接口）上加一个 `ICommand` 公开属性，命名以 `Command` 结尾。
2. 需要参数就在同一位置加 `[AgentCommandParameter(typeof(T))]`；参数类型里出现的自定义类型会被消费方自动注册（`.../Agent/Workflow/WorkflowAgentScope.cs:810`）。
3. 不要在 `CanExecute` 上寄托拦截 —— 要拦就在工具层拦。
4. 命令体抛出的异常会变成一句错误字符串，**要保栈要自己 catch 后写日志**（`AgentCommandDiscoverer.cs:148-151`）。

### C. 给 `SlotEnumerator` 属性加白名单

1. 在 `SlotEnumerator<TSlot>` 属性（或它的后备字段）上标 `[SlotSelectors(typeof(A), typeof(B))]` —— **用 typeof**。
2. 消费方三处会读它：`ListSlotProperties` 的输出（`.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1133`）、`SetEnumSlotCollection` 的校验（`:1293`、`:1344`）、说明文字（`.../Agent/Workflow/AgentContextCollector.cs:221`）。
3. `ComponentPatcher` 会自动拒绝直接 patch 这个属性，**不需要额外注册**（`.../Agent/Workflow/Functions/ComponentPatcher.cs:128`）。

### D. 把一个新的对象类型整体暴露成工具面

1. 消费方调用 `obj.AsAgentToolkit(language, rejectedProperties)`（`.../Agent/AgentObjectToolkit.cs:340`），得到 10 个基础工具（`:54-66`）。
2. `rejectedProperties` 只影响 `PatchProperties` 一路，**不影响** `SetProperty`（单属性写，`:181`）—— 要屏蔽必须两条都走 `rejected`，或不在工具面暴露写工具。核对：`SetProperty` 调 `SetPropertyValue`（无 rejected 参数）。
3. 目标若绑在某条 UI 线程上，**必须自己设 `ToolPipeline.MarshalTo`**：Core 不注册任何编组（`architecture.md` §一表）。

### E. 加一个语言

见 §四·1 的联动表 —— 这条没有「只改一处」的走法。

---

## 四、联动清单

**读法**：下表每行都是一处必须同步改的地方。漏掉通常**不报错**，而是静默少一个能力。

### 4.1 加一个 `AgentLanguages` 值

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `AgentLanguages.cs:3-39` 加枚举值（注意值必须唯一，`Chinese` 是别名） | —— |
| 2 | `AgentLanguagesExtensions.ToLanguageCode` 的 switch（`:88-127`） | 该语言调 `ToLanguageCode()` **抛** `ArgumentOutOfRangeException` |
| 3 | `AgentLanguagesExtensions.GetDisplayName` 的 switch（`:163-202`） | 同上，提示词渲染时抛 |
| 4 | `LanguageCodeMap`（`:43-86`） | `TryParseLanguageCode` 永远认不出该语言，**静默返回 false** |
| 5 | 消费方的第二张码表 `Src/Core/VeloxDev.Core.Extension/Agent/AgentEmbeddedResources.cs:38-46` | 它的判断是 `language == AgentLanguages.Chinese ? "zh" : "en"`，而 `Chinese` 是 `ChineseSimplified` 的别名（`AgentLanguages.cs:7`）⇒ **只有简中进 `zh` 目录，繁体中文也走 `"en"`**；新语言同样静默走英文目录 |
| 6 | 每个作者的 `[AgentContext]` | 该语言下这个成员的说明**退回英文**（只有在连英文都没写时才是空数组） |
| 7 | 嵌入式资源目录（今天只有 `Src/Core/VeloxDev.Core.Extension/Resources/Workflow/en|zh/` 两个，32 个 .md） | 该语言的技能/参考文件不存在 → 静默回退英文。目录约定见 `.../Agent/AgentEmbeddedResources.cs:9-15`，读法见 `.../Agent/Skills/EmbeddedSkillSource.cs:103` |

**两张同名不同义的 `ToLanguageCode` 都在 `VeloxDev.AI` 命名空间里**：`AgentLanguagesExtensions.ToLanguageCode`（扩展方法，BCP-47 风格，33 个出口）与 `AgentEmbeddedResources.ToLanguageCode`（普通静态，只有 `zh`/`en`）。因为前者是扩展方法，`lang.ToLanguageCode()` 永远解析到前者 —— 所以**没有编译期歧义，也没有任何提示**告诉你这里有两套码。

### 4.2 加一个 `[AgentContext]` 的读取点（新助手 / 新发现器）

**转调 `AgentContextReader.GetContexts(...)` 就完了** —— 语言与回退规则只在 `AgentContextReader.Select`（`:59`）里一份。今天的调用点是 `AgentCommandDiscoverer.cs:73`、`:94`、`AgentMethodInvoker.cs:78`、`AgentPropertyAccessor.cs:63`，全都是转调。**不要**在这些助手内部再写一次 `Where(a => a.Language == language)`：那正是这次修掉的历史形态，它会静默地不回退。

### 4.3 给发现器加一个字段（例如 `CommandDescriptor` 上再挂一个属性）

| # | 位置 | 漏了会怎样 |
|---|---|---|
| 1 | `AgentCommandDiscoverer.cs` 的 `CommandDescriptor` + 两个扫描分支（`:80`、`:104`） | **两条分支都要加**：接口分支与具体类型分支是两段独立代码 |
| 2 | 消费方的工具输出 `.../Agent/AgentObjectToolkit.cs:221-229` / `.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的对应 `JObject` | Core 加了字段，工具输出里**不会自动出现** |

### 4.4 加一个 `SlotSelectors` 的消费者

只有消费方要动（`.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1293`、`:1344` 校验，`:1133` 描述，`.../Agent/Workflow/AgentContextCollector.cs:221` 说明文字，`.../Agent/Workflow/Functions/ComponentPatcher.cs:128` 拒绝）。**Core 一行都不用改**。

### 4.5 加一个事件负载字段

`AgentConfirmationEventArgs` / `AgentSelectionEventArgs` / `AgentToolCallEventArgs` 是纯 DTO，Core 里**没有任何生产者** —— 全是消费方 `new` 出来的（`.../Agent/Workflow/WorkflowAgentScope.cs:498`、`:525`、`:555`；`.../Agent/AgentObjectToolkit.cs:113`）。加字段要同步：七家 demo 的对话框实现 + 消费方 handler 的包装层。

### 4.6 本模块**不需要**联动的东西（省掉无谓的搜索）

- **没有生成器**：`Src/Generators/VeloxDev.Core.Generator/` 不参与 AI 模块（对比 WorkflowSystem 的 Helper/命令是靠生成器写出来的）。
- **没有平台适配器**：加一家 GUI 不需要在本模块改任何一行；交互 UI 归各 demo。
- **没有序列化契约**：`AgentLanguages` 是 `byte` 枚举，但没有任何地方对它做自定义序列化；跨进程传的是它自己的值。

---

## 五、死扩展点与零调用者

以下成员**在整个 `Src/` 与 `Examples/` 里只有测试调用，或连测试都没有**（各行已标明）——不要把它们当成「有消费方在依赖的契约」：

| 成员 | 位置 | 说明 |
|---|---|---|
| `AgentCommandDiscoverer.CanExecuteCommand` | `AgentCommandDiscoverer.cs:159` | 零非测试调用者；发现路径用的是私有的 `TryCanExecute`（`:244`） |
| `AgentMethodInvoker.InvokeStatic` | `AgentMethodInvoker.cs:165` | 零非测试调用者 |
| `AgentPropertyAccessor.CopyScalarProperties` | `AgentPropertyAccessor.cs:169` | 零非测试调用者；**消费方另写了一份同名实现在 `.../Agent/Workflow/Functions/ComponentPatcher.cs:236`**（没有转调 Core）—— 改 Core 那份不会影响实际跑的路径 |
| `AgentContextReader.HasAgentContext` | `AgentContextReader.cs:32` | 零非测试调用者 |
| `AgentLanguagesExtensions.ParseLanguageCode` | `AgentLanguages.cs:153` | **零调用者**（连测试都没有；测试用的是 `TryParseLanguageCode`）。`ToLanguageCode`/`GetDisplayName` 相反，消费方在用（`.../Agent/Workflow/WorkflowAgentScope.cs:1203`、`:1204`） |
| `IAgentConfirmationNotifier` / `IAgentSelectionNotifier` | `AgentConfirmationEventArgs.cs:37`、`AgentSelectionEventArgs.cs:60` | **全仓零实现者**。三个 `IAgent*Notifier` 里只有 `IAgentToolCallNotifier` 被实现了（`.../Agent/AgentObjectToolkit.cs:29`、`.../Agent/Workflow/WorkflowAgentScope.cs:20`）。确认/选择的实际通路是消费方的 `Func<..., Task>` handler —— 因为 `EventHandler` 没法表达「等用户点完」 |

唯一有非测试调用者的替身入口是 `AgentCommandDiscoverer.FindBackingCommand`（`AgentCommandDiscoverer.cs:176`），调用者是 `.../Agent/Workflow/Functions/ComponentPatcher.cs:230` 的一层薄委托。
