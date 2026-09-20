# AI — 架构

> 代码：`Src/Core/VeloxDev.Core/AI/`（13 个 .cs）。**目录名 `AI`，命名空间是 `VeloxDev.AI`** —— 两者不同名，`grep VeloxDev.AI` 会连带命中消费方。
> 消费方：`Src/Core/VeloxDev.Core.Extension/Agent/`（**44** 个 .cs，命名空间同样以 `VeloxDev.AI` 起头：`VeloxDev.AI.Pipelines`、`VeloxDev.AI.Workflow`、`VeloxDev.AI.MCP`、`VeloxDev.AI.Skills`）。**别把 `Agent/` 与整个 `VeloxDev.Core.Extension` 项目搞混**：整个项目是 106 个 .cs，`Agent/` 只是其中 44 个（其余是框架侧的 `WorkflowSystem` 辅助、`TransitionSystem`、`MVVM` 等，与本模块无关）。**依赖方向单向：Extension → Core**。Core/AI 不引用 Extension 的任何东西，也不引用 `VeloxDev.WorkflowSystem` 的类型 —— 唯一一处提及是 `SlotSelectorsAttribute.cs:4` 的 XML 注释 `cref`（同程序集，所以能解析，但不是编译期依赖）。
> 平台差异：**本模块没有平台轴**。整个 `AI/` 只 `using System.Reflection` 与 `System.Windows.Input`（后者在 .NET Core 由 `System.ObjectModel` 提供，不是 WPF 依赖）。所以本模块没有 `adapters/`。

本文只写「读完 13 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一组**无状态静态反射助手 + 三个标注特性 + 一组事件 DTO/接口**，作用是把一个任意 .NET 对象上「Agent 能调的东西」（`ICommand` 属性、公开方法、公开属性、挂在类型/成员上的自然语言说明）**枚举出来、读出来、写进去、调起来**。

一句话：**`object` → 描述符列表；描述符 + 字符串 → 调用**。

**不解决什么**（这几条决定了你找不到代码时该往哪看）：

| 你以为在这里 | 实际在哪 |
|---|---|
| 工具怎么变成 `AITool`、工具名/描述/JSON 参数长什么样 | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:49`（`CreateTools`）、`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs` |
| 调用上限、拒绝、埋点、事件流水线 | `Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/`（`AgentPipeline`/`ToolPipeline`）。Core 的 `AgentToolCallEventArgs` 是**事后**通知，且不含结果成败字段 |
| 弹窗、用户点「同意/拒绝」的 UI | 七家 demo，例如 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml.cs:431`。Core 只给 `AgentConfirmationEventArgs`/`AgentSelectionEventArgs` 两个 DTO |
| 「等用户点完」的异步握手 | **不在 Core**。`EventHandler` 是同步的，所以接口注释只能写「signal any awaitable completion mechanism」（`AgentConfirmationEventArgs.cs:40-43`）；真正做这件事的是消费方的 `Func<..., Task>` 形状（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:494`、`:521`） |
| 线程编组 | **不在 Core**。反射读写在**调用方线程**上跑；`AgentObjectToolkit` 明说它刻意不注册编组（`Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:84-88`） |
| 语言包 / 翻译 | 只有 `AgentLanguages` 枚举与码表，**没有一条文案**。文案由作者写在 `[AgentContext]` 上 |
| 类型 schema（属性列表、枚举值、默认实例） | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/TypeIntrospector.cs:20`（`GetTypeSchema`）；Core 的 `AgentTypeResolver` 只做「字符串 → `Type`」 |

### 本模块**不是**唯一的实现路径（读到这里之前一定会踩）

消费方**只复用本模块的三样东西**：`AgentContextAttribute`/`AgentCommandParameterAttribute`/`SlotSelectorsAttribute` 三个**特性类型**、`AgentContextReader`/`AgentTypeResolver` 两个纯读助手、以及事件 DTO 与接口。**命令的发现/执行与属性的写入这两条在 Workflow 侧是分叉实现**：

| 问题 | Core 通用路径 | Workflow 路径（各写各的） |
|---|---|---|
| 命令怎么被列出/执行 | `AgentCommandDiscoverer`，调用者 `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:217`、`:240` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/CommandInvoker.cs:21`、`:81`，调用者 `.../Workflow/Functions/WorkflowAgentToolkit.cs:869`、`:921`、`:937` |
| 属性怎么被批量写 | `AgentPropertyAccessor.SetProperties`（`AgentPropertyAccessor.cs:138`） | `.../Functions/ComponentPatcher.cs`（`ApplyPatch`，调用者 `.../WorkflowAgentToolkit.cs:810`、`:822`），连标量对拷都另写了一版（`ComponentPatcher.cs:236`） |

两条路在**可观察行为**上不同，所以「改 Core 的规则」和「改工具的行为」不是同一件事：`CommandInvoker` 的接口扫描**不做语言过滤**（返回全部语言的键值对，`CommandInvoker.cs:36-38`）而具体类扫描**硬编码 English**（`:64`、`:70`）；去重是「与已累积结果比名字」（`:53`）而不是名字集合；参数是按 `[AgentCommandParameter].ParameterType` **反序列化 JSON**（`:107`）—— 也正因为如此，**枚举参数在 Workflow 路径上是通的，在 `AgentMethodInvoker` 那条路上是断的**（见 §五·2）。它还自带一个**同名的 `CommandDescriptor`**（`CommandInvoker.cs:171`，与 `AgentCommandDiscoverer.CommandDescriptor` 同名不同命名空间）。

---

## 二、三个特性 vs 读它们的代码（本模块的主轴）

| 特性 | 声明位置 | Core 里谁读它 | 消费方谁读它 |
|---|---|---|---|
| `AgentContextAttribute` | `AgentContextAttribute.cs:4` | `AgentContextReader.cs:16`/`:26`；四个助手**各自内联**过滤：`AgentCommandDiscoverer.cs:73`、`:97`；`AgentMethodInvoker.cs:78`；`AgentPropertyAccessor.cs:63` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/AgentContextCollector.cs:18`（转调 Core）、`.../Workflow/Functions/TypeIntrospector.cs:66` |
| `AgentCommandParameterAttribute` | `AgentCommandParameterAttribute.cs:10` | **只有** `AgentCommandDiscoverer.FindParameterAttribute`（`AgentCommandDiscoverer.cs:223`） | `.../Agent/Workflow/WorkflowAgentScope.cs:810`、`:838`、`:850`（只取 `ParameterType` 用于注册类型，不参与调用） |
| `SlotSelectorsAttribute` | `SlotSelectorsAttribute.cs:38` | **没有。Core 一处都不读它** | `.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1133`、`:1293`、`:1344`；`.../Functions/ComponentPatcher.cs:128`；`.../Agent/Workflow/AgentContextCollector.cs:221`；`.../Agent/Workflow/WorkflowAgentScope.cs:809`、`:818`、`:837`、`:863` |

**结论**：`SlotSelectorsAttribute` 住在 Core/AI，但它是**给消费方的 Workflow 工具箱用的**（语义属于 `SlotEnumerator<TSlot>` 的校验白名单）。Core 只是提供了一个「编译器保证的常量容器」。动它的语义 = 动 Extension，Core 无感。

### `[AgentContext]` 的三条硬规则（读代码得出，不是读注释得出）

1. **`inherit: false` 到处都是。** 类型/属性/方法上的 `[AgentContext]` **不会**沿继承链或接口下沉 —— 每一处读取都传 `inherit: false`（`AgentContextReader.cs:16`、`:26`、`:36`；`AgentMethodInvoker.cs:78`；`AgentPropertyAccessor.cs:63`）。
2. **没有语言回退。** 读取是 `Where(a => a.Language == language)`，只返回**完全匹配**的那些。只写英文的成员在 `Chinese` 下返回**空数组**，不会退回英文（`AgentContextReader.cs:16-18`；测试 `Src/Core/VeloxDev.Core.Test/AI/AgentContextReaderTests.cs:37-40` 断言 Japanese 命中 0 条）。
3. **一个语言可写多条，全部返回且保序**（`AllowMultiple = true`，`AgentContextAttribute.cs:3`；测试 `.../AgentContextReaderTests.cs:20-23` 断言 English 命中 2 条）。`Context` 默认空串，所以 `[AgentContext(AgentLanguages.English)]` 合法但什么都不说明。

**`ICommand` 是唯一「接口上的特性算数」的地方。** `AgentCommandDiscoverer.DiscoverCommands` **先扫接口**再扫具体类型（`AgentCommandDiscoverer.cs:64-88` / `:90-111`），用一个 `Ordinal` 名字集合去重（`:61`、`:70`、`:94`）。对命令而言「特性写在接口上」是官方做法（代码注释自己写着 `these carry the authoritative attributes`，`:64`），这也正是 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:33` 那一族标注的位置。对**方法**与**普通属性**没有对应的接口扫描，接口上的特性一律读不到。

---

## 三、一次发现 / 调用经过谁

```
调用方（Extension）              Core/AI                                    返回
──────────────────────────────────────────────────────────────────────────────────────
DiscoverCommands(obj, lang) ─┬─ type.GetInterfaces() 的 ICommand 属性 ─┐
                             └─ type 自己的 ICommand 属性 ─────────────┴→ IReadOnlyList<CommandDescriptor>
                                参数类型 ← AgentCommandParameterAttribute
                                CanExecute 的实参恒为 null（见 §五·1）
DiscoverMethods(obj, lang)  ─── type.GetMethods(Public|Instance[|Static]) ─→ IReadOnlyList<MethodDescriptor>
DiscoverProperties(...)     ─── type.GetProperties(Public|Instance) ──────→ IReadOnlyList<PropertyDescriptor>

ExecuteCommand(obj, "Delete")   → NormalizeCommandName → "DeleteCommand"
                                → GetCommandInstance → command.Execute(param) → ExecuteResult
InvokeMethod(obj, "Add", [3,7]) → 按**实参个数**挑重载 → 补默认值 → Convert.ChangeType → Invoke → InvokeResult
SetProperty(obj, "Name", v)     → ConvertValue → prop.SetValue → SetResult
```

**支线**：`FindBackingCommand`（属性名 → `Set{X}Command` / `{X}Command`，`AgentCommandDiscoverer.cs:176-195`）给消费方的 patcher 判断「这个属性该走 setter 还是走命令」；`AgentTypeResolver.ResolveType` 给「字符串类型名 → `Type`」；`AgentContextReader` 只服务类型/成员的说明文字。

**这里没有「谁拥有状态」的问题，只有「谁有副作用」的问题。** 13 个文件里全是静态类和每次调用新建的 DTO，**静态可变状态为零**（唯一的 `static` 字段是 `AgentLanguagesExtensions.LanguageCodeMap`，`AgentLanguages.cs:43`，初始化后再无写者）；唯一被长期持有的状态是消费方那边的被代理对象。有副作用的入口只有五个：`Execute`、`Invoke`、`InvokeStatic`、`SetPropertyValue`/`SetProperties`、`CopyScalarProperties`。

---

## 四、不变量

1. **`CanExecute` 只被报告，从不拦截。** `AgentCommandDiscoverer.Execute`（`:123-154`）不查 `CanExecute`；消费方也不查 —— `AgentObjectToolkit` 把 `CanExecute` 原样放进 `ListCommands` 的输出（`.../Agent/AgentObjectToolkit.cs:225`），而 `ExecuteCommand` 直接调 `Execute`（`.../Agent/AgentObjectToolkit.cs:240`）。要拦只能由工具层自己拦。
2. **`TryCanExecute` 的实参恒为 `null`**：`command.CanExecute(paramType == null ? null : (object?)null)`（`AgentCommandDiscoverer.cs:246`）—— 三元两个分支都是 `null`，`ParameterType` 完全不参与。属性注释写「checked with null parameter if no ParameterType」（`:32-33`），**与代码不符**。后果：需要非空参数才返回 true 的命令被报成 `canExecute: false`（假阴）。
3. **命令名规范化单向且大小写敏感**：`name.EndsWith("Command") ? name : name + "Command"`（`:199-200`）。发现时给的是**原始属性名**（`"SaveCommand"`），执行时 `"Save"` 与 `"SaveCommand"` 都行，但 `"savecommand"` 会被拼成 `"savecommandCommand"` 然后找不到。
4. **`[AgentContext]` 的语言参数占了位置参数第一位**（`AgentContextAttribute.cs:4`），所以写不出 `[AgentContext("说明")]`（字符串撞 `AgentLanguages`，编译不过）。官方写法是 `[AgentContext(AgentLanguages.Chinese, "说明")]`，全仓一致，例 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowViewModel.cs:11`。
5. **确认题的默认答案是「拒绝」**：`AgentConfirmationEventArgs.Result` 初值 `Deny`（`AgentConfirmationEventArgs.cs:30`）。处理器忘了赋值 = 拒绝，fail-closed。消费方在这之上做 `AllowAlways` 的会话级持久化（`.../Agent/Workflow/WorkflowAgentScope.cs:537-550`）。
6. **`AgentLanguages.Chinese` 与 `ChineseSimplified` 是同一个值**（`AgentLanguages.cs:7`，值 1）：枚举共 34 个名字 / **33 个不同值**。所以 `ToLanguageCode`/`GetDisplayName` 里 `Chinese or ChineseSimplified`（`:93`、`:168`）只覆盖一个值。**未定义值会抛** `ArgumentOutOfRangeException`（`:125`、`:200`）—— 例如反序列化来一个 `(AgentLanguages)99`。
7. **码表 41 条 → 33 个值，方向不对称**（`AgentLanguages.cs:43-86`）：`no`/`nb`/`nn` 都 → `Norwegian`，但 `Norwegian.ToLanguageCode()` 只回 `no`；`zh`/`zh-hans`/`zh-cn`/`zh-sg` → `ChineseSimplified`，回程只有 `zh-Hans`。`TryParseLanguageCode` 额外做两件事：先把 `_` 换成 `-`（`:138`），失败后再按 `-` 切首段重试（`:144-148`），所以 `zh-HK-x-foo` 会命中 `zh-hk`。

---

## 五、反直觉（带依据）

1. **重载解析按实参个数，不按类型。** `Invoke` 先找个数精确相等的，再找「必填数 ≤ 个数 ≤ 总数」的第一个（`AgentMethodInvoker.cs:119-121`）；同级重载谁先被反射枚举到就用谁（顺序未定义）—— `Add(int)` 与 `Add(string)` 无法区分。
2. **枚举参数经 `InvokeMethod` 传不进去。** `Convert.ChangeType(3L, typeof(MyEnum))` 抛 `InvalidCastException`，被 `AgentMethodInvoker.cs:145` 的 `catch { }` 吞掉，然后在 `:149` 的 `Invoke` 上以参数不匹配失败。JSON 整数经 `JToken.ToObject<object>()` 是 `Int64`，必然走这条路。对照：`AgentPropertyAccessor.ConvertValue` 有专门的枚举分支（`AgentPropertyAccessor.cs:210-215`，字符串走 `Enum.Parse(..., ignoreCase: true)`）—— **同一个模块里两条路径不对称**。
3. **`GetPropertyValue` 是唯一不吞异常的读入口**（`AgentPropertyAccessor.cs:84-90`，`prop.GetValue` 裸调且无 try）；`DiscoverProperties(includeValues: true)` 那条是吞异常的（`:68-72`）。`CanRead` 反映「有 getter」而不是「getter 可见」，所以一条 `{ private get; set; }` 的属性会让 `GetPropertyValue` 抛 `ArgumentException`。当前树里没有这种属性（`private get;` 零命中），属潜在坑。
4. **`CopyScalarProperties` 只搬 11 种标量 + 枚举**（`AgentPropertyAccessor.cs:183-185`），**且不做类型转换**（直接 `prop.SetValue(target, prop.GetValue(source))`，`:187`）：同名的但类型不同的属性在 `SetValue` 上抛，然后被 `:187-188` 吞掉。
5. **`SetProperties` 的 `rejected` 是按名字精确比对的 `ISet<string>`**（`:149`）—— 没有通配、没有前缀规则。
6. **`InvokeStatic` 有两处不对称**（`AgentMethodInvoker.cs:165-198`）：不补默认值、不做类型转换；且匹配失败时的兜底是 `?? candidates.FirstOrDefault()`（`:180`）**挑一个名字对但参数个数不对的重载**，然后在 `:187` 抛出来 —— 调用方只会看到一条错误字符串。
7. **`FindParameterAttribute` 会猜「后备方法」**：`commandName.Replace("Command", "")`（`:239`）—— `Replace` 替换**所有**出现，`"CommandHistoryCommand"` 会变成 `"History"`。
8. **`DiscoverCommands` 的输出顺序未定义**（`type.GetInterfaces()` 的顺序不是规范）；两次调用之间、两台机器之间都可能不同。名字本身会被去重，顺序不会。
9. **`AgentContextReader.HasAgentContext` 不看语言**（`:34-37`）：只要有任何语言的 `[AgentContext]` 就为 true。
10. **反射未加裁剪注解。** `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0` 且 `IsTrimmable=false`；`AI/` 里 `RequiresUnreferencedCode`/`DynamicallyAccessedMembers` 零处。也就是说这套反射**没有 AOT/裁剪契约**；消费方要裁剪就必须自己保住元数据。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 命令怎么被发现（哪些属性算命令、去重、接口优先） | `AgentCommandDiscoverer.cs:54-114` |
| 命令怎么被执行（名字规范化、参数、错误文本） | `AgentCommandDiscoverer.cs:123-168`、`:199-242` |
| 方法重载怎么挑、实参怎么补/转 | `AgentMethodInvoker.cs:103-160` |
| 属性读写与类型转换规则 | `AgentPropertyAccessor.cs:84-218` |
| 说明文字怎么取（语言、inherit、多值） | `AgentContextReader.cs` + 四个内联过滤点（见 §二表） |
| 语言枚举与码表 | `AgentLanguages.cs:3-39`（枚举）、`:43-86`（码表）、`:88`/`:129`/`:163`（三处 switch） |
| 确认 / 选择 / 工具调用的事件负载 | `AgentConfirmationEventArgs.cs`、`AgentSelectionEventArgs.cs`、`AgentToolCallEventArgs.cs` |
| `SlotSelectors` 的行为 | **不在本模块** —— `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1293`、`.../Functions/ComponentPatcher.cs:128` |
| 工具面的整体装配（谁把描述符变成工具） | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs`（通用对象）、`.../Agent/Workflow/WorkflowAgentScope.cs`（工作流） |
