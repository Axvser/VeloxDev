# AI — 架构

> 代码：`Src/Core/VeloxDev.Core/AI/`。**目录名 `AI`，命名空间是 `VeloxDev.AI`** —— 两者不同名，`grep VeloxDev.AI` 会连带命中消费方。
> **这个模块一行反射都没有**。所有助手都读编译期目录 `AIContextTree`（见 §七），动作经每类型生成的 `IAIContextAccessor` 落地：`AgentContextReader` / `AgentTypeResolver` / `AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor`。标注特性**在 Core 里没有读取点**，只有生成器读它们。§一~§六 讲可观察行为，涉及「怎么做的」以 §七 为准。
> 消费方：`Src/Core/VeloxDev.Core.Extension/Agent/`（命名空间同样以 `VeloxDev.AI` 起头：`VeloxDev.AI.Pipelines`、`VeloxDev.AI.Workflow`、`VeloxDev.AI.MCP`、`VeloxDev.AI.Skills`、`VeloxDev.AI.SubAgents` 等）。`Agent/` 是项目里的大部分（其余是根目录的 `AgentEx.cs`、`CheckpointEx.cs`、`CompiledGraphEx.cs` 与 `Compat/IsExternalInit.cs`、`Compat/NotNullWhenAttribute.cs`，与本模块无关）。**依赖方向单向：Extension → Core**。Core/AI 不引用 Extension 的任何东西，也不引用 `VeloxDev.WorkflowSystem` 的类型 —— 唯一一处提及是 `SlotSelectorsAttribute.cs` 的 XML 注释 `cref`（同程序集，所以能解析，但不是编译期依赖）。
> 平台差异：**本模块没有平台轴**。整个 `AI/` 只用两个 BCL using —— `System.Reflection`（`AgentContextReader.cs` 的 `using System.Reflection;`，取 `MemberInfo` 的声明类型与名字，不是对元数据做遍历）与 `System.Globalization`（`AIContextConvert.cs` 的 `using System.Globalization;`）；没有任何 GUI 依赖。所以本模块没有 `adapters/`。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一组**无状态静态助手 + 标注特性 + 一组事件 DTO/接口**，作用是把一个任意 .NET 对象上「Agent 能调的东西」（`ICommand` 属性、公开方法、公开属性、挂在类型/成员上的自然语言说明）**枚举出来、读出来、写进去、调起来**。助手读的是编译期目录，动作经生成的访问器落地（§七）。

一句话：**`object` → 描述符列表；描述符 + 字符串 → 调用**。

**不解决什么**：

| 你以为在这里 | 实际在哪 |
|---|---|
| 工具怎么变成 `AITool`、工具名/描述/JSON 参数长什么样 | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs`（`CreateTools`）、`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs` |
| 调用上限、拒绝、埋点、事件流水线 | `Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/`（`AgentPipeline`/`ToolPipeline`）。Core 的 `AgentToolCallEventArgs` 是**事后**通知，且不含结果成败字段 |
| 弹窗、用户点「同意/拒绝」的 UI | 各 demo，例如 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml.cs`（`ShowConfirmationDialogAsync`）。Core 只给 `AgentConfirmationEventArgs`/`AgentSelectionEventArgs` 两个 DTO |
| 「等用户点完」的异步握手 | **不在 Core**。`EventHandler` 是同步的；`IAgentConfirmationNotifier` / `IAgentSelectionNotifier` 未实现，并已标 `[Obsolete]`；实际做这件事的是消费方的 `Func<..., Task>` 形状（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs` 的 `WithSelectionHandler` / `WithConfirmationHandler`） |
| 线程编组 | **不在 Core**。目录读写与访问器调用都在**调用方线程**上跑；`AgentObjectToolkit` 明说它刻意不注册编组（`Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs` 的 `Tools` 注释「It registers no thread marshalling on purpose」） |
| 语言包 / 翻译 | 只有 `AgentLanguages` 枚举与码表，**没有一条文案**。文案由作者写在 `[AgentContext]` 或 `*.agentcontext.xml` 上（§七·十四） |
| 类型 schema（属性列表、枚举值、默认实例） | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/TypeIntrospector.cs`（`GetTypeSchema`）；Core 的 `AgentTypeResolver` 只做「字符串 → `Type`」 |

### 本模块**不是**唯一的实现路径

消费方**复用本模块的东西分几层**：`AgentContextAttribute`/`AgentCommandParameterAttribute`/`SlotSelectorsAttribute` 一组**特性类型**、`AgentContextReader`/`AgentTypeResolver` 几个纯读助手、事件 DTO 与接口，以及 §七 的目录面（`AIContextDirectory`/`AIContextTreeRegistry`/`IAIContextAccessor` —— 消费方直接读目录、直接调访问器）。**命令的发现/执行与属性的写入这两条在 Workflow 侧是分叉实现**：

| 问题 | Core 通用路径 | Workflow 路径（各写各的） |
|---|---|---|
| 命令怎么被列出/执行 | `AgentCommandDiscoverer`，调用者 `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs` 的 `ListCommands` / `ExecuteCommand` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/CommandInvoker.cs` 的 `DiscoverCommands` / `Invoke`，调用者 `.../Workflow/Functions/WorkflowAgentToolkit.cs` 的 `ListComponentCommands` / `ExecuteCommandOnNode` / `ExecuteCommandById` |
| 属性怎么被批量写 | `AgentPropertyAccessor.SetProperties` | `.../Functions/ComponentPatcher.cs`（`ApplyPatch`，调用者 `.../WorkflowAgentToolkit.cs` 的 `PatchNodeProperties` / `PatchComponentById`），连标量对拷都另写了一版（`ComponentPatcher.CopyScalarProperties`） |

两条路**可观察行为**不同：改 Core 的规则 ≠ 改工具的行为。**`CommandInvoker` 也读目录**（`CommandInvoker.DiscoverCommands` 的 `MembersAcross`），但它仍是一份独立实现：自带一个**同名的 `CommandDescriptor`**（`CommandInvoker.cs` 的 `CommandDescriptor`，与 `AgentCommandDiscoverer.CommandDescriptor` 同名不同命名空间，字段是 `Type? ParameterType` 与 `Descriptions` 的 `KeyValuePair<AgentLanguages,string>`）；**不做语言过滤** —— 把节点上每种语言的说明全部倒出来（`DiscoverCommands` 里的 `Descriptions`）；参数用 `AgentJsonValue.Convert` 反序列化（`CommandInvoker.Invoke`）而不是 `AgentMethodInvoker` 的 `AIContextConvert` 转换表。**枚举参数两条路现在都能通**（见 §五·2）。

---

## 二、标注特性 vs 读它们的代码（本模块的主轴）

| 特性 | 声明位置 | Core 里谁读它 | 消费方谁读它 |
|---|---|---|---|
| `AgentContextAttribute` | `AgentContextAttribute.cs` 的 `AgentContextAttribute` | **只有生成器**（`Src/Generators/VeloxDev.Core.Generator/Base/AIContextModel.cs` 的 `ReadTexts`）。运行期没有读取点：说明在编译期就被渲染成 `AgentText` 存进目录，Core 的 `AgentTextSelection.Select`（`AgentText.cs`）是**语言与回退规则的唯一所在**，`AgentContextReader` 只是按 `DeclaringType`+`Name` 查节点后转调它 | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/AgentContextCollector.cs` 的 `GetAgentContext`（转调 Core）、`.../Workflow/Functions/TypeIntrospector.cs` 的 `GetTypeSchema` |
| `AgentCommandParameterAttribute` | `AgentCommandParameterAttribute.cs` 的 `AgentCommandParameterAttribute` | **只有生成器**（`AIContextModelBuilder.ReadCommandParameterType`）。存进目录的方式是节点上一条 `AIContextRefKind.CommandParameterType` 引用 | 运行期只有 `.../Agent/Workflow/Functions/CommandInvoker.cs` 的 `DiscoverCommands` / `Invoke` 经访问器的 `ParameterType(commandName)` 取它（`typeof` 字面量），用于反序列化参数 |
| `SlotSelectorsAttribute` | `SlotSelectorsAttribute.cs` 的 `SlotSelectorsAttribute` | **没有。Core 一处都不读它**（生成器读，存成 `SlotSelectorType` 引用 + `HasSlotSelectors` 标志） | `.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的 `ListSlotProperties`（展示白名单）、`SetEnumSlotCollection` 的 `IsEnumTypeAllowed` 校验；`.../Functions/ComponentPatcher.cs` 的 `ApplyPatch`（拒绝直接 patch） |

**结论**：`SlotSelectorsAttribute` 住在 Core/AI，但它是**给消费方的 Workflow 工具箱用的**（语义属于 `SlotEnumerator<TSlot>` 的校验白名单）。Core 只是提供了一个「编译器保证的常量容器」。动它的语义 = 动 Extension，Core 无感。

说明文字有两条来源：`[AgentContext]` 特性与 `*.agentcontext.xml` 资源文件。两者在 `AIContextModelBuilder.ReadTexts` 合并，资源文件的格式与匹配规则见 §七·十四。

### `[AgentContext]` 的四条硬规则（读代码得出，不是读注释得出）

1. **说明不沿继承链或接口下沉。** 类型/成员上的 `[AgentContext]` 在编译期就落到它自己那个目录节点上，运行期没有「继承」这一步：`AgentContextReader.EntryFor(member)` 按 `member.DeclaringType` 查节点（`AgentContextReader.cs` 的 `EntryFor(MemberInfo)`），所以继承来的成员读的是**基类**那条说明，派生类型不会多出一份；属性/方法也不扫接口。没有 `inherit:` 参数 —— 说明不从元数据读取。
2. **有语言回退，且是「整目标、全有或全无」。** 规则集中在 `AgentTextSelection.Select`（`AgentText.cs`）：先取 `Language == language` 的；若**一条都没有**且请求的不是 `English`，则整体改取 `English` 的那些。所以「同语言命中 ≥ 1 条」时**不会**混入英文；`English` 自身无处可退。测试 `Src/Test/Unit/Core/VeloxDev.Core.Test/AI/AgentContextReaderTests.cs`：`..._FallsBackToEnglish`、`..._FallbackIsAllOrNothing`、`..._EnglishRequestNeverFallsBack`、`..._NoEnglishEither_ReturnsEmpty`。**各助手走 `AIContextMembers.DescriptionsFor(node, language)`（`AIContextMembers.cs`），按 `MemberInfo`/`Type` 进来的走 `AgentContextReader`** —— 两条都只是转调 `Select`，规则没有第二份拷贝，改它只需改 `Select` 一处。
3. **`HasAgentContext` 不看语言**（`AgentContextReader.HasAgentContext`）：只要有任何语言的标注就为 true，与 `GetContexts` 的选取无关。
4. **一个语言可写多条，全部返回且保序**（`AllowMultiple = true`，`AgentContextAttribute.cs` 的 `AttributeUsage`；测试 `.../AgentContextReaderTests.cs` 断言 English 命中多条）。`Context` 默认空串，所以 `[AgentContext(AgentLanguages.English)]` 合法但什么都不说明。

**`ICommand` 是唯一「接口上的特性算数」的地方。** 运行期不扫描接口：`AgentCommandDiscoverer.DiscoverCommands` 直接遍历目录里的 `Commands` 节点，去重交给 `AIContextDirectory.MembersAcross` 的 `seen` 集合。「接口上的标注才算数」这条规则前移到了生成期 —— `AIContextModelBuilder.InterfaceCommandProperty`（`AIContextModel.cs`，在 `BuildProperty` 与 `BuildPromotedCommand` 里只对命令调用）按同名属性回查接口，把 `[AgentContext]`/`[AgentCommandParameter]` 复制到实现类的命令节点上。对**方法**与**普通属性**没有对应的接口回查，接口上的特性一律读不到。命令标注的范本仍是 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Components/ITreeComponent.cs` 的 `CreateNodeCommand` 那一族。

---

## 三、一次发现 / 调用经过谁

```
调用方（Extension）              Core/AI                                    返回
──────────────────────────────────────────────────────────────────────────────────────
DiscoverCommands(obj, lang) ─── AIContextDirectory.MembersAcross(类型, "Commands") ─→ IReadOnlyList<CommandDescriptor>
                                参数类型 ← 节点上的 CommandParameterType 引用
                                CanExecute ← 访问器，实参恒为 null（见 §四·2）
DiscoverMethods(obj, lang)  ─── MembersAcross(类型, "Methods") + 参数子节点 ─→ IReadOnlyList<MethodDescriptor>
DiscoverProperties(...)     ─── MembersAcross(类型, "Properties") ──────────→ IReadOnlyList<PropertyDescriptor>

ExecuteCommand(obj, "Delete")   → NormalizeCommandName → "DeleteCommand" → 节点 → 声明类型的访问器
                                → TryExecuteCommand → command.Execute(param) → ExecuteResult
InvokeMethod(obj, "Add", [3,7]) → 节点 → 访问器 TryInvoke：按**实参个数**分派到某个重载
                                （缺的尾部可选参数由生成代码省略实参，编译器补默认值）→ InvokeResult
SetProperty(obj, "Name", v)     → 节点（CanWrite 标志）→ 访问器 Set → AIContextConvert → SetResult
```

**三处共同的形状**：先用 `AIContextMembers.TypeNameOf(target)` 拿类型全名（拿不到 = 类型不在目录里，闭世界，直接返回空/拒绝），再用 `MembersAcross` 枚举**该类型 + 基类链**上的成员节点，最后用 `AIContextMembers.AccessorFor(node)` 取**声明该成员的那个类型**的访问器 —— 继承来的成员由基类的访问器执行，派生类型自己的访问器里没有它的 `case`。

**支线**：`FindBackingCommand`（属性名 → `Set{X}Command` / `{X}Command`，`AgentCommandDiscoverer.cs`）给消费方的 patcher 判断「这个属性该走 setter 还是走命令」—— 现在形参仍是 `Type`，但只读它的 `FullName` 去查目录；`AgentTypeResolver.ResolveType` 给「字符串类型名 → `Type`」（查访问器注册表，闭世界）；`AgentContextReader` 只服务类型/成员的说明文字。

**这里没有「谁拥有状态」的问题，只有「谁有副作用」的问题。** 全是静态类和每次调用新建的 DTO，**静态可变状态为零**（`AIContextTreeRegistry` 的 `_fragments` / `_accessors` 只在模块初始化时写一次；`AgentLanguagesExtensions.LanguageCodeMap`，`AgentLanguages.cs`，初始化后再无写者）；唯一被长期持有的状态是消费方那边的被代理对象。有副作用的入口只有以下几个：`Execute`、`Invoke`、`SetPropertyValue`/`SetProperties`、`CopyScalarProperties`。

---

## 四、不变量

1. **`CanExecute` 只被报告，从不拦截。** `AgentCommandDiscoverer.Execute` 不查 `CanExecute`，消费方也不查 —— `AgentObjectToolkit` 把 `CanExecute` 原样放进 `ListCommands` 的输出，而 `ExecuteCommand` 直接调 `Execute`（`.../Agent/AgentObjectToolkit.cs` 的 `ListCommands` / `ExecuteCommand`）。要拦只能由工具层自己拦。**生成器那条闸不存在**：`WriteTryExecuteCommand` **不**发 `if (!c.CanExecute(parameter)) return false;`（那与 `IAIContextAccessor.CanExecuteCommand` 的注释直接矛盾）。按不变量不查，`Src/Test/Unit/Core/VeloxDev.Core.Test/AI/AgentCommandDiscovererTests.cs` 的 `CanExecute_IsReportedAndNeverEnforced` 锁住。
2. **`CanExecute` 的实参恒为 `null`**（`AgentCommandDiscoverer.cs` 的 `DiscoverCommands` 与 `CanExecuteCommand`）—— 命令的 `ParameterType` 完全不参与。后果：需要非空参数才返回 `true` 的命令被报成 `canExecute: false`（假阴）。
3. **命令名规范化单向且大小写敏感**：`name.EndsWith("Command") ? name : name + "Command"`（`AgentCommandDiscoverer.NormalizeCommandName`）。发现时给的是**原始属性名**（`"SaveCommand"`），执行时 `"Save"` 与 `"SaveCommand"` 都行，但 `"savecommand"` 会被拼成 `"savecommandCommand"` 然后找不到。
4. **`[AgentContext]` 的语言参数占了位置参数第一位**（`AgentContextAttribute.cs` 的 `AgentContextAttribute` 构造），所以写不出 `[AgentContext("说明")]`（字符串撞 `AgentLanguages`，编译不过）。官方写法是 `[AgentContext(AgentLanguages.Chinese, "说明")]`，全仓一致，例 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/Common/IWorkflowComponent.cs` 的接口级 `[AgentContext]`。
5. **确认题的默认答案是「拒绝」**：`AgentConfirmationEventArgs.Result` 初值 `Deny`（`AgentConfirmationEventArgs.cs` 的 `Result` 属性）。处理器忘了赋值 = 拒绝，fail-closed。消费方在这之上做 `AllowAlways` 的会话级持久化（`.../Agent/Workflow/WorkflowAgentScope.cs` 的 `ResolveConfirmationAsync`）。
6. **`AgentLanguages.Chinese` 与 `ChineseSimplified` 是同一个值**（`AgentLanguages.cs` 的 `Chinese = ChineseSimplified`，值 1）：枚举里有同值的别名。所以 `ToLanguageCode`/`GetDisplayName` 里 `Chinese or ChineseSimplified` 只覆盖一个值。**未定义值会抛** `ArgumentOutOfRangeException`（两处 switch 的 `_ => throw`）—— 例如反序列化来一个 `(AgentLanguages)99`。
7. **码表方向不对称**（`AgentLanguages.cs` 的 `LanguageCodeMap`）：`no`/`nb`/`nn` 都 → `Norwegian`，但 `Norwegian.ToLanguageCode()` 只回 `no`；`zh`/`zh-hans`/`zh-cn`/`zh-sg` → `ChineseSimplified`，回程只有 `zh-Hans`。`TryParseLanguageCode` 额外做两件事：先把 `_` 换成 `-`（`normalizedCode`），失败后再按 `-` 切首段重试（`separatorIndex` 分支）—— 重试取的是**第一个 `-` 之前的那一段**，所以命中不了 `zh-hk` 这类两段码：`zh-HK-x-foo` 会退到 `zh`（→ `ChineseSimplified`），要命中 `zh-hk` 只有写成 `zh-HK` 本身。

---

## 五、反直觉（带依据）

1. **重载解析仍按实参个数，不按类型。** 生成期把同一名字的方法排成 `switch (args.Length)`：精确元数优先，其余元数由「必填数 ≤ 个数 < 总数」的那个重载接手（尾部缺的参数由生成代码**省略实参**，编译器补它自己的默认值 —— 所以目录里不需要存默认值，只需要 `Optional` 标志）。**同元数的重载谁赢取决于声明顺序**，`Add(int)` 与 `Add(string)` 仍然无法区分。
2. **枚举参数能进去。** 生成的转换走 `AIContextConvert.ToEnum<TEnum>`（字符串按名字、数字按底层值，`AIContextConvert.cs` 的 `ToEnum`）。数字形态也能落到枚举上（`ToEnum<TEnum>` 对非字符串走 `Enum.ToObject`）。
3. **`GetPropertyValue` 对「不可读」与「不在目录里」都回 `null`，不抛。** `CanRead` 是「有**公开** getter」，所以 `{ private get; set; }` 走「不可读」这条路。
4. **`CopyScalarProperties` 转调访问器的 `CopyScalarFrom`**，搬的是**两个类型共有的、可写的全部成员**（生成代码里 `(T)value!`）。**它零生产调用者** —— 实际跑的是 `ComponentPatcher.CopyScalarProperties` 那份自带白名单的分叉（`extension.md` §二·7），改 Core 这份不影响实际路径。
5. **`SetProperties` 的 `rejected` 是按名字精确比对的 `ISet<string>`** —— 没有通配、没有前缀规则。这条没变。
6. **`AgentMethodInvoker` 没有静态面**：没有 `InvokeStatic` 与 `DiscoverMethods(includeStatic:)`（目录不录静态方法，访问器只对实例做事），也没有 `MethodDescriptor.IsStatic` 与 `ParameterDescriptor.DefaultValue`（全仓零读取者）。
7. **参数类型只来自 `[AgentCommandParameter]`**，接口上写了就以接口为准（见 §七）；目录里没有按方法名猜参数类型的启发式。
8. **`DiscoverCommands` 的顺序是声明顺序**（目录保序）。名字仍按名字去重；**同名不同元数的方法在目录里只能留一个节点**（`AIContextTreeRegistry.List` 按名字去重），所以 `DiscoverMethods` 一个方法名只报一条 —— 但 `Invoke` 仍按实参个数支持全部重载（访问器里 `switch (args.Length)` 每个合法个数一个分支）。
9. **`AgentContextReader.HasAgentContext` 不看语言**：只要有任何语言的 `[AgentContext]` 就为 true，与 `GetContexts` 的选取无关。
10. **Core/AI 零反射，裁剪/AOT 契约开在 `net8.0` 那一档。** `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` 的 `TargetFrameworks` 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`，其中 `IsAotCompatible` 只对 `net8.0` 为 true；Extension 的 csproj 同样如此。**Core 与 Extension 两侧都没有反射**（`…ByReflection` 回退体现在住在测试工程 `ReflectionContextOracle`）；少数无法消除的点改成「按需声明」而不是消除 —— 见 §七。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 命令怎么被执行（名字规范化、错误文本） | `AgentCommandDiscoverer.cs`（`Execute` / `CanExecuteCommand` / `NormalizeCommandName`） |
| **哪些成员算命令、叫什么名字、说明与参数类型从哪来** | **不在本模块** —— 生成期：`Src/Generators/VeloxDev.Core.Generator/Base/AIContextModel.cs`（`BuildProperty` / `BuildPromotedCommand` / `InterfaceCommandProperty`） |
| 方法重载怎么挑、实参怎么补/转 | 生成期：`Src/Generators/VeloxDev.Core.Generator/Writers/AIContextTreeWriter.cs` 的 `ArityPlan` / `WriteInvokeBody`；转换表在同文件的 `RenderConversion` + `AIContextConvert.cs` |
| 属性读写与类型转换规则 | `AgentPropertyAccessor.cs`（发现与拒绝文案）；转换在生成代码里，同上 |
| 类型/成员的继承链怎么走 | `AIContextDirectory.MembersAcross`（`AIContextDirectory.cs`）—— **只此一份**，渲染器与各助手都转调它 |
| 说明文字怎么取（语言、回退、多值） | `AgentTextSelection.Select`（`AgentText.cs`）一处；节点侧的入口是 `AIContextMembers.DescriptionsFor`，`MemberInfo`/`Type` 侧的入口是 `AgentContextReader`，都只是转调，见 §二表 |
| 语言枚举与码表 | `AgentLanguages.cs` 的 `AgentLanguages`（枚举）、`LanguageCodeMap`（码表）、`AllLanguages`（派生列表）、`ToLanguageCode`/`GetDisplayName`（两处 switch） |
| 确认 / 选择 / 工具调用的事件负载 | `AgentConfirmationEventArgs.cs`、`AgentSelectionEventArgs.cs`、`AgentToolCallEventArgs.cs` |
| `SlotSelectors` 的行为 | **不在本模块** —— `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 的 `ListSlotProperties` / `SetEnumSlotCollection`、`.../Functions/ComponentPatcher.cs` 的 `ApplyPatch` |
| 工具面的整体装配（谁把描述符变成工具） | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs`（通用对象）、`.../Agent/Workflow/WorkflowAgentScope.cs`（工作流） |

---

## 七、AIContextTree：编译期静态目录

`AIContextTree` 把「Agent 面是什么」与「怎么对一个对象动手」都前移到编译期，让这套东西在 NativeAOT 下工作。**它是唯一的路径**：Core 的助手与 Extension 的 `AgentContextCollector`/`CommandInvoker`/`ComponentPatcher`/`TypeIntrospector`/`WorkflowAgentScope` 全部读目录；`…ByReflection` 实现只在测试工程里当 parity 的 oracle。

### 两半

| | 装什么 | 为什么 |
|---|---|---|
| `AIContextTree`（目录） | 字符串、枚举、整数、数组。**没有** `Type` / `MemberInfo` / 委托 | 模型被**告知**什么。它里面任何东西都不 root 元数据，所以可裁剪 |
| `IAIContextAccessor`（访问器） | 每个类型一个生成的类，含 `Type TargetType => typeof(T)` 与成员操作 | 对对象**做**什么。`typeof` 字面量裁剪安全，且恰好 root 访问器实际碰到的成员 |

目录之所以能只装数据，是因为**类型身份全由访问器承担**。一个只以字符串出现在目录里的类型（如 `[SlotSelectors("Some.Type")]`），不由目录 root，而由它自己那个访问器 root。

### 一个「引用」是 `(kind, path, declaredName)` 三元组

`AIContextRef`（`AIContextTree.cs`）不装结点、不装委托：委托是指向生成代码的托管指针，会让裁剪行为依赖委托构造。`Path` 是稳定可读的路径（`Framework/Components/Slots/<类型全名>`），解析是注册表里一次字典查找；为空时回退到 `DeclaredName`，那是 `[SlotSelectors("…")]` 允许写编译期不存在的名字时唯一还能报告的东西。

### 目录的形状

```
Framework/                          ← Core 自己产的分片（VeloxAgentContextTreeRoot=Framework）
  Components/{Nodes,Slots,Links,Trees}/<类型全名>/{Properties,Fields,Commands,Methods}
  Interfaces/<类型全名>/Properties     Enums/<类型全名>/Members     Data/<类型全名>/…
Customer/                           ← 每个消费者程序集一个分片（默认根）
  …同上
```

`Components` 下四分不是发明的：`WorkflowAgentScope` 的自动发现本来就按这几个接口分类。默认视图模型、框架枚举/接口/数据也都在 `Framework/` 里；`WithAutoDiscovery` 只按目录列举（`WorkflowAgentScope.WithAutoDiscovery`），没有手维护的 `Type[]`。

### 惰性：一个目录一个 `Lazy`

生成器**逐目录**产出 `Lazy<AIContextNode[]>`，`ChildrenOf(path)` 是一个按路径字符串分派的 `switch`。列一个目录只构建那个目录；解析一个 `Ref` 是独立调用，没有任何遍历会为了它走进子目录。类型名索引同样是首次访问时才建字典。

### 生成器：`Src/Generators/VeloxDev.Core.Generator/AIContextTree.cs`

**它不套用 `Analizer.Filters.Targets`**：那套管线只收 `ClassDeclarationSyntax` 且要求 `partial`，触发集不含 Agent 特性，也表达不了「实现了哪个接口」这种组件身份。它走一次编译级遍历，一次 `AddSource`。两条守卫：编译单元里没有 `VeloxDev.AI.AgentContextAttribute` 就整体不产出；`VeloxAgentContextTree=false` 可整体关闭。

**框架分片必须在 Core 内部生成。** 目录要收录带 `[VeloxProperty]` 的**私有实例字段**（`AIContextModelBuilder.ReadMembers` 的私有字段分支 `BuildDescribedOnlyField`），而 Roslyn **引用程序集里没有私有成员** —— 消费者侧去读引用程序集符号会静默丢掉每一个字段行。

### 三条坑

1. **生成器之间看不见彼此的产物。** 目录里的 `Channel` 是 MVVM 生成器写出来的属性，`SaveCommand` 是 CommandWriter 写出来的 —— `AIContextTree` 生成器**看不到它们**，只能复现命名规则。所以规则抽在 `Base/AIContextNaming.cs`，`MVVMFieldAnalizer`（`Analizer.cs`，转调点 `GetPropertyNameFromFieldName`）与 `CommandWriter.ReadCommandConfig` 都转调它。改一处漏一处会让目录命名不存在的成员，报错落在消费方编译里。 ⚠ **多一个运行时依赖**：`AgentContextReader.EntryFor(MemberInfo)` 要靠这条规则把 `FieldInfo` 认回它那个提升条目（见 §七·十三）。它按**匹配**而不是按**构造**用规则（只认目录里已经存在的名字），所以规则改了它只会静默认不到 —— 症状是「字段的标注读不出来」。
2. **`[VeloxCommand]` 方法在实现类里普遍是 `private`。** 生成出来的命令属性却是公开的，所以命令的收录**不能按方法可见性过滤** —— 按 `Public` 过滤会把整个命令面漏掉（`SlotDefaultViewModel` 的命令全是 private）。
3. **访问器够不着别的类型的 `private` 成员。** 提升属性靠的是**生成出来的那个属性**（同一个类里，编得过），不是字段本身。

### 明确不在目录里的东西

泛型类型与泛型方法（`T` 在生成的 cast 里没有绑定）、静态类（`(Static)target` 编不过）、`private`/`protected`/`file` 类型、编译器生成的名字、`object` 的四个成员（`ToString`/`GetHashCode`/`Equals`/`GetType` —— 生成器的 `IsObjectMember` 把它们滤掉，理由一样：没有作者想把它们放进 Agent 面）。同元数的重载只留一个，并报 `VELOX_AI_TREE001`。

### 生成器开销（2026-10-03 实测，单次墙钟，非基准）

**代价集中在 Core**（分片最大）。消费方的增量代价落在噪声里。关掉的方式是 `-p:VeloxAgentContextTree=false`。

| 工程 | 生成器开 | 生成器关 | 差 |
|---|---|---|---|
| Core（分片 453KB）clean | 3121 ms | 2613 ms | +508 ms |
| Core **touch 增量** | 2534–2796 ms | 2281–2335 ms | **+280 ms（~12%）** |
| `Examples/Workflow/Common/Lib`（分片 145KB）clean | 3535 ms | 3123 ms | +412 ms |
| 同上 **touch 增量** | 1965–2054 ms | 1944–2015 ms | **+8 ms（噪声级）** |

### 验证在哪

- `Src/Test/Unit/Core/VeloxDev.Core.Test/AI/AIContextTreeTests.cs` —— 注册、目录列举、条目查找、访问器读写与执行。含**反向对照**：一个没被标注的类型必须查不到路径也没有访问器 —— 目录一旦放宽收录规则，那条会先红。
- `Src/Test/Unit/Core/VeloxDev.Core.Test/AI/AIContextTreeGeneratorTests.cs` —— 走 `GeneratorProbe` 驱动生成器，断言提升名、命令名、重载诊断、以及没有 Agent 面时**不产出**。
- `Src/Test/Unit/Core/VeloxDev.Core.Extension.Test/Agent/Workflow/AgentContextTreeParityTests.cs` —— 运行时遍历 `Framework/` 下每个类型条目 × 各语言，目录渲染与反射渲染逐字对比。它是「换数据源不换输出」的唯一保证。**它只验 Framework 根，oracle（`ReflectionContextOracle`）自身也曾有错 —— parity 绿 ≠ 这一档被验过。**

### 验证裁剪

`IsTrimmable=true` 单独用不够：SDK 10 上它不引 `Microsoft.NET.ILLink.Tasks`，「0 警告」是假象。必须显式 `-p:EnableTrimAnalyzer=true`，且 Core 要一起多目标到 net8.0（否则 Extension 的 net8.0 构建引用不到它）。

### Extension 侧的对象面

**`WithAutoDiscovery` 是目录列举**：读 `Customer/{Enums,Interfaces,Data}` 与 `Customer/Components/{Nodes,Slots,Links,Trees}`，每个名字经 `AgentTypeResolver.ResolveType` 换成 `typeof` 字面量再进既有的 `Customer*` 集合。**框架类型由只读 `Customer/` 排除**，不需要手写排除表；没有 `GetTypes()` + 成员深扫推断。

**泛型的两条路**（目录不收泛型类型，所以访问器够不着 `SlotEnumerator<TSlot>`）：

1. `VeloxDev.WorkflowSystem` 新增非泛型视图 —— `IConditionalSlotProvider`（擦掉 `TSlot` 的 `Parent`/`SelectorTypeName`/`SelectorType`/`Slots`/`TrySelect`/`SetSelector`）与 `IConditionalSlot`（`Name`/`Value`/`Slot`），两个泛型类显式实现。Toolkit 里那些 `GetProperty("Items")`/`GetProperty("Slot")`/`GetMethod("TrySelect")` 全部变成一次转型。
2. `IAIContextAccessor` 新增 `MemberType(member)` 与 `ParameterType(commandName)` —— 每处都是 `typeof` 字面量。这是唯一让 `Type` 合法离开访问器的地方，也是生成式序列化、`IsAssignableFrom` 判定、`Create` 之外所有需要 `Type` 的地方的解。

**值类型也能 `Create`**（`IsRefLikeType` 除外），所以 `defaultJson_runtimeOnly` 对 struct 依旧。

**新标志**：`Optional`(2048) / `IsValueType`(4096) / `IsSlotCollection`(8192)。`IsSlotEnumerator` 认「实现了 `IConditionalSlotProvider<T>`」的类型，不只是 `SlotEnumerator<T>` 本身。

### Core 助手的 API 形状

`PropertyDescriptor.PropertyType` / `MethodDescriptor.ReturnType` / `ParameterDescriptor.ParameterType` 是 `string`（**字段名保留**），`DiscoverProperties` / `DiscoverMethods` 的 `filter` 是 `Func<string,bool>`。没有 `InvokeStatic`、`includeStatic:`、`MethodDescriptor.IsStatic`、`ParameterDescriptor.DefaultValue`（全仓零读取者），`CopyScalarProperties` 没有 `skip` 形参（访问器的 `CopyScalarFrom` 接不了它）。

**行为事实（都不在 parity 覆盖范围内）：**

- 工具 JSON 里的类型名是目录全名形状（`Int32` → `System.Int32`，泛型带实参）；`AgentObjectToolkitTests.TheListTools_ReportTheTypeNamesTheTreeCarries` 锁住。
- 泛型方法不出现在目录里。
- `DiscoverMethods` 一个方法名只报一条（目录按名字去重），`Invoke` 仍支持全部元数。
- 闭世界：`AgentObjectToolkit` 只对生成器收录过的类型有效（被标注、或成员里有 `ICommand`、或是四个组件接口的实现）。任意 POCO 现在是「列不出、写不进、调不动」，不是「能用」—— 这是公开 API 的契约位移，写在 `AgentObjectToolkit` 的 `<remarks>` 里。

### AOT / 裁剪状态

`VeloxDev.Core` 与 `VeloxDev.Core.Extension` 的 IL2xxx/IL3050 都是 0。工具面与存档路径用 `VeloxDev.Serialization`（生成式 JSON 读写 + 不反射的 JSON 树），不用 Newtonsoft；形状、闭世界、逐字节兼容与读入侧**实测确认消不掉的两处**记在 [`VeloxDev.Core.Extension/architecture.md`](../VeloxDev.Core.Extension/architecture.md) §八。`SlotEnumerator.ResolveTypeByName` 是目录查询；`CompileKeyNormalizer` 的路由键、`TransitionProperty.FindIndexer`、`Interpolator` 三处**换法实测过会红**，按需声明，理由在各自注释里。

两个 csproj 的 net8.0 档开 `IsAotCompatible=true`（`VeloxDev.Core.csproj` / `VeloxDev.Core.Extension.csproj` 里 `Condition="'$(TargetFramework)' == 'net8.0'"` 的 `IsAotCompatible`），分析器在这一档常开。`Examples/Workflow/Avalonia Trimmed/Directory.Build.props` 里没有 `TrimmerRootAssembly`；端到端裁剪发布没验过 —— 桌面那条路被 `NETSDK1124` 挡着（`-p:PublishTrimmed=true` 是全局属性，会泄漏给多目标的 Core/Extension），配了裁剪的是 Browser/Android 两条（此判断未再复核）。

### 踩过的坑（测试锁定）

- **`Anchor` / `Offset` / `Size` / `Scale` 是 class 不是 struct**，尽管名字像值对象。`GetTypeSchema` 的 `kind` 报的是 `class`，`IsValueType` 标志只对真的 struct（`CellKey`、`ThreadRef`）为真。
- **`TreeDefaultViewModel` 上没有 `Anchor`** —— 它是节点/插槽的属性。测「命令后备」那条要先把节点挂到树上（`tree.GetHelper().CreateNode(node)`），否则 `ResolveTree` 直接以「未挂载」拒绝。
- **`FriendlyTypeName` 只把「特殊类型」写短**：`ObservableCollection<VeloxDev.WorkflowSystem.INodeComponent>` 里的类型实参仍是全名。
- **`TreeProperty.Get` 要吞异常**：生成的 `TryGet` 是裸读（`value = t.X`），一个会抛的 getter 会逃出去。
- **`GetType().Name` 不是反射警告源**：`Object.GetType` 与 `Type.Name`/`FullName` 都没有裁剪注解，那些「类型名标签」一行都不用改 —— 裁剪警告来自 `GetProperties`/`GetProperty`/`GetMethod`/`GetInterfaces`/`GetCustomAttribute`/`Activator.CreateInstance` 这些**索取成员**的调用。

### 两条契约代价

1. **夹具必须 `internal` 以上。** 生成器跳过私有 / `protected` / `file` 类型，所以任何要被 Agent 描述的类型都得可见 —— 测试里那批 `private sealed class` 夹具读不到说明，要改成 `internal`。用户组件类同理，不能是嵌套私有的。
2. **`AgentTypeResolver.ResolveType` 是封闭世界。** `ResolveType("System.String")` 返回 `null` —— 没有分片的类型解析不到。

### 生成期的约束

- **MVVM 生成器产出的属性也在同一个编译里**，所以 `symbol.GetMembers()` 里既有私有字段又有它提升出的属性。不按名字去掉生成的那份，每个 `[VeloxProperty]` 会在目录里出现两次，顺序也被带偏。
- **同一个目录里不能有两个同名节点**：类型条目，与代表它成员目录的 Directory 占位；`List` 的去重方向一变就翻车。
- **`AIContextTreeRegistry.List` 不能排序** —— 排序会抹掉声明顺序，而表格逐字复现反射输出。
- **`Type.FullName` 与 Roslyn 显示名不同形**：嵌套用 `+` 不用 `.`、泛型带反引号元数、不带可空标注、不特殊化关键字（`System.Int32` 而非 `int`）。那个字符串既是索引键也是渲染出来那一行，所以 `Base/AIContextModel.cs` 里 `ReflectionFullName` / `TableType` 各管一头。**每个要去查目录的名字都必须走 `ReflectionFullName`** —— 写成 `BaseTypeName` 的 `ToDisplayString(FullyQualifiedFormat)` 时，对嵌套基类型永远查不到，继承来的成员整条丢掉。
- **接口上的命令标注要在生成期带回实现类。** 目录只录类型自己声明的成员，而命令的 `[AgentContext]` / `[AgentCommandParameter]` 官方写在接口上；`AIContextModelBuilder.InterfaceCommandProperty` 按同名属性回查 `AllInterfaces` 补齐。**只对命令做** —— 对普通成员做会让 `AgentClass` 表多出行、打翻 parity。
- **诊断要先滤掉 `object` 的四个成员**，否则 `VELOX_AI_TREE001` 会报在每个带强类型 `Equals` 的值类型上，下场是被整仓 `NoWarn` 掉。
- **缺省参数不用往目录里存默认值**：生成代码只需**少写一个实参**，C# 会在调用点补上声明的默认值。目录里只要一个 `AIContextFlags.Optional` 标志供描述符报 `IsOptional`。

## 七·五、两个闭世界**不是同一个**，工具面正好卡在中间

`AIContextTree` 与归档序列化器各有一个闭世界，**收录条件不同**：

| | 谁进去 | 怎么进去 |
|---|---|---|
| **AI 闭世界** | 类型能被 `AgentTypeResolver.ResolveType` 解开 —— 即它有一条**类型条目**，因而有一个 `IAIContextAccessor` | 带 `[AgentContext]`（自己或成员）、是某成员的声明类型、被 `[SlotSelectors]` 之类**引用**到…… |
| **归档闭世界** | 类型能被 `VeloxJsonSerializer` 读写 —— 即生成器为它编了 reader/writer | 组件、`[VeloxProperty]`、`[Archivable]`、`[WorkflowBuilder.*]`，或从这些出发沿**成员的声明类型**走到 |

**能解析 ≠ 能重建。** 工具面有好几处是「模型给一段 JSON，宿主按类型名读回来」，那些地方要的是**两个闭世界同时成立** —— 而两个条件之间没有蕴含关系。

### 缝在哪（都实测过）

- **`SetEnumSlotCollection` 的非枚举路径**（`WorkflowAgentToolkit.cs`）：`TypeIntrospector.ResolveType(nonEnumTypeName)` 走的是 AI 闭世界，紧接着 `VeloxJsonSerializer.Deserialize(json, targetType)` 走的是归档闭世界。provider 是普通类（既不是组件也不是 `[VeloxProperty]` 持有者），**必须自己带 `[Archivable]`**。缺了它引擎会报「has no registered JSON reader」，现在这句直接点名 `[Archivable]`。
- **`ComponentPatcher` / `CommandInvoker` 的枚举参数**：工具面**写**枚举用名字（`AppendScalarProperties`、`WorkflowStateTracker` 都写 `ToString()`），而归档引擎的运行期读法**刻意没有枚举分支**（`TryReadScalar` 对 enum 返回 null，生成的 reader 才用强制转换）。于是 `PatchNodeProperties {"CompileMode":"Static"}` —— 写在 `EnumSelectorNodeViewModel` 自己的 `[AgentContext]` 里的例子 —— 直接抛。**修在工具层**：`AgentJsonValue.Convert` 就地认名字或底层整数，引擎那条「没有独立枚举读法」的设计不动。
- **`VeloxJsonValue.From(context.Data)`**：运行载荷是宿主的业务对象，可以是任何一个归档不认识的类型，然后整个状态调用变成错误信封。`WorkflowAgentToolkit.DataJson` 现在兜底成文本。

### 两条收录规则

- **`[SlotSelectors]` 只往目录里放一条*引用*，不放类型条目。** 所以一个只在 `[SlotSelectors]` 里被点名的枚举**解析不开**，`SetEnumSlotCollection` 会答「Type not found」，尽管属性自己把它列为允许。**选择器枚举要带 `[AgentContext]`**（demo 的 `DatasetSource` / `VoltageRange` 都带）。演示见 `SetEnumSlotCollectionTests.AnEnumTheTreeHoldsOnlyAsAReference_DoesNotResolve`。
- **非枚举 provider 要带 `[Archivable]`**，见上。

### 守卫

`ClosedWorldBoundaryTests` 把这两条**按声明整体扫**，而不是举一个例子：

- 扫各程序集里**每个** `[SlotSelectors]`（**注意挂在成员上，不是类型上** —— 拿 `Type.GetCustomAttributes` 扫会一个都找不到，用例空过），非枚举的那类断言 `VeloxJsonRegistry.ReaderFor` 非空。
- 扫每个组件的命令参数类型，断言可读 / 是枚举 / 是标量 / 在一张**具名的接口允许表**里（`INodeComponent`、`ISlotComponent`、`ITaskContext`、`IActionPair` —— 它们各自有专用工具，不需要从 JSON 造）。

两条都带**自证守卫**（扫不到任何条目就红），否则重命名一次属性就能让用例永远变绿。

### 工具返回的形状

`SetEnumSlotCollection` 两条路径共用 `EnumeratorResult`，都带 `status: "ok"` / `count` / `slots`（id + label + value）。只回 `{ok, selectorType, property}` 时模型看不见自己那段 JSON 的结果，实测会打转。**改这个工具时别把 slots 拿掉。**

### 七·六、槽属性的三条判据链

一个槽属性要被工具面认出来，要连过三关：**生成器给出标志 → 目录带着标志 → 工具按标志解析**。

- **`AIContextFlags.IsSingleSlot` 由目录生成器算**，判据是「属性的类型实现了 `ISlotComponent`」。但 `[WorkflowBuilder.Slot<T>]` 声明的类型，那个接口是 **Workflow 生成器**在同一编译趟注入的 —— 生成器之间看不见彼此的产物，于是这些类型一律判 false。`EnumSelectorNodeViewModel.InputSlot` 就是这样。
- **断口的三个下游**（都是静默的）：`ListSlotProperties` 不列它；`BuildSlotPropertyMap` 不认它，`GetNodeDetail` 里那个槽因此没有 `prop` 字段；`ResolveSlotId` / `ConnectByProperty` 按属性名解析不到它 → 只能回落到 `ConnectSlotsById`。**还有一条**：`ComponentPatcher` 靠同一个标志拒绝对槽属性赋值，标志缺了它就放行 —— 那是唯一绕过 `CreateSlotCommand` 的写入口。
- **生成器算标志，工具侧同时有运行期兜底**（`TreeProperty.HoldsASingleSlot(target)` = 标志 || 值当前是 `ISlotComponent`）。**兜底实测不是死代码**：单独撤掉生成器那一行，用例仍绿；两处一起撤才红。
- `IsSlotCollection` 对消费方声明的 `SlotEnumerator<T>` 为 true。`ResolveSlotId` 先查集合那条分支，而那条要求值是 `IList` —— 枚举器装的是 `ConditionalSlot`，不是 `IList`，所以它单独走 `IsSlotEnumerator` 分支。`AddSlotToCollection` / `RemoveSlotFromCollection` 也把枚举器单独分出来。

守卫：`SlotPropertyResolutionTests`。生成器那张「谁认 `[WorkflowBuilder.*]`」的表见 [`VeloxDev.Core.Generator/architecture.md`](../VeloxDev.Core.Generator/architecture.md) §六·9。

### 七·七、工具面上的**两个坐标系**

`SetAnchorCommand` / `SetSizeCommand` 存的是**世界坐标**（字段直存，存档里也是世界值）；而 `node.Anchor` / `node.Size` 的 **getter 为了渲染按画布缩放折叠过**（`Anchor.Collapse`，即除以缩放）。把渲染 getter 当对外读数时：

- 写进去 (3120, 940)，读回来 (2836.36, 854.55)，**比例随用户缩放变**；
- 位置无法往返核对，两次不同缩放下的读数也无法互比。

`ListNodes` / `GetNodeDetail` 报世界坐标（乘回缩放，`WorldAnchor` / `WorldSize`），说明里也写明用的是哪个空间。**只在读侧乘回** —— 渲染确实需要折叠值，错的是把渲染用的 getter 当成对外的位置读数。守卫在 `NodeGeometryToolTests`（覆盖非单位缩放的读数与往返）。

### 七·八、目录的「谁算组件」判据

`AIContextModelBuilder.ComponentKindOf` 认四个 `I*Component` 接口，也认 `[WorkflowBuilder.*]`（接口是另一个生成器注入的，生成器之间看不见）。`BuildType` 的收录规则是「是组件 / 或被标注过 / 或成员带标注」，所以只写 `[WorkflowBuilder.Node<T>]` 的节点类型也会进目录；归档侧同理，`VeloxJsonModel.RootReason` 第二条就是 `[WorkflowBuilder.*]`。

⚠ **这是对消费方目录的加宽**：他们声明的每个组件都进目录（不只带标注的），prompt 体积会涨。回退点：`ComponentKindOf` 里那一行。

守卫：`DiscoveryCoverageTests`。该用例还钉住了**「枚举器不带 `[SlotSelectors]` = 不设限」**：白名单才是
限制，没有它就是放开；枚举/布尔一路、自定义 provider 一路，各验一次（`IsEnumTypeAllowed` 对两路的代码不同）。

### 七·九、归档的三条「加宽」，目录不需要

归档闭世界沿三条线扩：**派生类向下展开**（`BuildFamilyIndex`）、**字典的键**、**开放泛型根上的类型参数约束**；AI 目录都不需要：

- **向下展开**解决的是「成员声明成基类、装着派生类时 `$type` 要解得开」—— 那是**写读文档**的问题。目录是
  **给人/模型看的清单**，按类型各有一条，本来就没有「按声明类型去找运行期类型」这一步。派生类型要么自己带标注，
  要么经 §七·八 那条新判据进来。
- **字典的键**在目录里没有对应物：目录不描述容器的键类型。
- **开放泛型约束**同样没有对应物：`BuildType` 对开放泛型直接返回 null（生成的 cast 里 `T` 没有绑定）。

### 七·十、可达成员类型与诊断收窄

第二趟（「可达的成员类型」）收录 `Enum | Struct | Class`。只走一遍、只认直接声明或泛型实参，所以是一跳、有界；`ListCreatableTypes` 只看 `Components/Nodes|Slots`，`Data` 条目不会被当成可创建类型报给模型。

`VeloxDev.Core.Extension` 自己会因此多出 `VELOX_AI_TREE001`（`AgentPipeline.Use`、`WorkflowAgentScope.WithSkills`）—— 被成员类型捎带进来的宿主 API 也在目录里。于是 `DetectNotices` 收一个 `declared` 集合（第一趟填的），只对**作者自己声明进目录**的类型报；枚举/结构体那一趟本就产生不了诊断。

> 过滤里有个真空引用：`if (type.Symbol is not null && !declared.Contains(type.Symbol)) continue;` —— `Symbol` 为 null 时不 continue，下一句就用它取 `Locations`，CS8602 报出。

守则：`DiscoveryCoverageTests.AClassExposedByAnAnnotatedMember_IsResolvable`。

### 七·十一、**详细归说明，返回值保持简单**

> 返回值要简单，工具描述可以详细。

**返回值每次调用都进模型的上下文**（而且是累积的），说明是**静态的**、每条请求发一次。「模型需要知道的东西」搬进说明，返回值只放这一次调用产出的东西。

`WorkflowAgentToolkit` 的类注释写着 "All JSON output uses `VeloxJsonFormat.Compact` to minimize token consumption"。拒绝写「选择器驱动的节点建出来没有端口」，不是在 `CreateNode` 的返回值里加提醒，而是在它的 `[Description]` 里写；`ConnectEnumSlot` 的两种接收口形态、`receiverCondition` 收什么值，也都在说明与参数说明里。

⚠ **说明要有测试**：`PortConnectionRouteTests` 验证连接、拒绝语点名了那个参数、`count:0` 确实是 0。

### 七·十二、注册表的锁**不能**盖住「读别的程序集的静态成员」

`AIContextTreeRegistry.PathFor` 不能持 `_lock` 调 `BuildTypeIndex()`：后者读 `fragment.TypeNames`，那是**另一个程序集**的静态类，首次读取会把那个程序集的**模块初始化器**拉起来，而生成出来的初始化器（`{程序集名}_AIContextRegistration.Register()`）里调的 `RegisterFragment` / `RegisterAccessor` **要的就是同一把 `_lock`**。两个线程各碰一半就是 ABBA 死锁；单线程下 Monitor 可重入，不会发生。

**规则**：注册表可以持 `_lock` 做**快照**，但凡是会碰到分片自身静态状态的动作，都要在**锁外**做。同一文件里的 `List` 就是这个写法（`Fragments` 锁内取快照，`fragment.ChildrenOf` 在锁外）；`PathFor` 是「锁内快照 + 锁外建索引 + 按 `_fragmentVersion` 校验后发布」。

**症状**：宿主**挂起**，只在并发下出现。实测复现、挂起转储与定位手法记在 [`VeloxDev.Core.Extension.Test/architecture.md`](../VeloxDev.Core.Extension.Test/architecture.md) §五。

**守卫**：`Src/Test/Unit/Core/VeloxDev.Core.Test/AI/AIContextTreeLockingTests.cs`（`[DoNotParallelize]`）。
它**不**去赛两个 `PathFor` —— 那个窗口在索引建好之后就永久关闭，赛出来的用例无论锁持没持都会绿，是空验证。
它把环直接摆出来：探针分片的 `TypeNames` 被读到时放行另一个线程去做一次普通 `PathFor`（**那正是要拿这把锁的操作**），
再看那次查找能否在本次读取返回之前完成。持锁的写法下它 **5 秒超时后断言失败**（不是挂起整轮），撤掉修复即红。

- **只有发起用例的那个线程才拦探针**（比 `Environment.CurrentManagedThreadId`）。另一个线程的重建若也被拦，它就等一个只有它自己能置位的信号 —— 自锁。
- **它把注册表原样还回去**：`internal static AIContextTreeRegistry.UnregisterFragment(string)` 只对 `Core.Test` 可见（`AssemblyInfo.cs` 的 `InternalsVisibleTo`），让用例结束时断言「分片集合与顺序与开始时逐条相同」。注册在生产里是单向的（模块初始化器只跑一次、没有程序集会被卸载）。

### 七·十三、提升条目属于**字段**，不属于生成出来的属性

`AgentContextReader.EntryFor(MemberInfo)` 不能只按成员自己的名字查一次：`[VeloxProperty]` 字段在目录里存的是**提升后的**名字（`payload` → `Payload`），**拿着 `FieldInfo` 按 `payload` 查是 null** —— `GetAgentContext(field, lang)` 对每个 `[VeloxProperty]` 字段都返回空。Framework 分片里这些字段的说明现在来自 `Assets/VeloxDev.Core.agentcontext.xml`；parity 只比较两种渲染，不验证这条字段查找路径。

**修法是两半，缺一不可**：

| 问谁 | 答什么 |
|---|---|
| `FieldInfo` | 先按自己的名字查；查不到就按**字段承载的条目**反查（名字与声明类型都对上才算），`Properties` 与 `Fields` 两个目录都找 |
| `PropertyInfo` | 命中字段承载的条目时**答空** —— `[VeloxProperty]` 从来没写在属性上 |

**只修前半会让 parity 当场红**：同一个条目会被字段和属性各认领一次，两条路都算「有描述」，于是每一行变成两行 —— `Data`（`Anchor` / `Offset` / `Size` 的提升字段）与 `Class` 两档都会翻倍。

**判据是 `Kind` + flag，不是「在哪个目录」**：`AIContextNodeKind.Field`（普通公开字段、以及只带
`[AgentContext]` 或资源点名的私有字段）与 `Kind == Property && IsPromotedField`（提升出来的属性）都算「字段承载」。
只带 `[AgentContext]` 或资源点名的私有字段**也按提升名收录**（`BuildDescribedOnlyField(field, PromotedPropertyName(...))`），所以它和提升字段一样按名字查不到。

**歧义时答空**：`volume` 与 `_volume` 会提升成同一个名字，两个都命中就返回 null，不猜。

### 七·十四、说明可以写在 `*.agentcontext.xml` 里

`AIContextTree.cs` 订阅 `AdditionalTextsProvider`，只收后缀 `.agentcontext.xml` 的文件，交给 `AIContextXml.Parse`；`AIContextModelBuilder.Build` 用 `AIContextSupplement` 把条目按符号解析，`ReadTexts` 把资源文字接在特性文字后面。**只在编译期读**：产物里没有 XML，目录仍只装字面量。

格式：

```xml
<AgentContext>
  <Type name="A.B">
    <Text>Type description</Text>
    <Member name="Save">
      <Text>Member description</Text>
    </Member>
  </Type>
</AgentContext>
```

- `language` 是 `AgentLanguages` 的成员名（大小写不敏感）；**省略即 `English`，英文条目默认不写**，只有非英文条目才写 `language`。不是枚举名报 `VELOX_AI_TREE005`。
- 类型名两种写法都认：元数据形状 `A.B+C` 与源码形状 `A.B.C`；都查不到报 `VELOX_AI_TREE003`。
- 成员名同时比对目录里的名字与源码符号名：`SaveCommand` / `Save`、`Payload` / `_payload` 都行；都对不上报 `VELOX_AI_TREE004`。成员条目归**声明它的类型** —— 继承来的成员写在基类的 `<Type>` 下，写在派生类上会报 `TREE004`。
- 文字是**追加**的：特性在前、资源在后；同一语言可写多条，顺序保留。
- 类型/成员只靠资源描述也能进目录；私有字段只有资源点名时按描述-only 收录（同 `[AgentContext]`，读写都 false）。
- 语法错误 `VELOX_AI_TREE002`（Error），未知元素/属性 `VELOX_AI_TREE006`（Warning）。

资源是**每个程序集各自**的：生成器只遍历当前编译，别家的类型解析不到。`VeloxDev.Core.Generator.targets`、`Src/Directory.Build.targets`、`Examples/Directory.Build.targets` 按 `**/*.agentcontext.xml` 收成 `AdditionalFiles`；同一路径被加两次时 `AIContextXml.Parse` 去重。框架自己的说明在 `Assets/VeloxDev.Core.agentcontext.xml`，由 `VeloxDev.Core.csproj` 显式收 —— `Assets` 在项目目录外，约定 glob 覆盖不到。

该文件的英文条目都不写 `language`（默认 English）：框架说明只有英文，中文请求由 `AgentTextSelection.Select` 整体回退到英文。资源不重写继承关系已经表达过的说明 —— 类的继承成员由 `AIContextDirectory.MembersAcross` 沿基类链合并，接口的基契约由每个渲染块里的 `Base Interfaces` 列出；四个 `*DefaultViewModel` 在资源里没有条目，`TaskContext` 只留类型说明、不重写 `Data`/`Sender`/`Receiver`。

## 八、Agent 节点选中：动态提示词，不是新工具

`VeloxDev.Core.Extension` 的 `Agent/Workflow/WorkflowAgentSelectionExtensions.cs` 把宿主选中的节点作为**每轮动态提示词**的一部分交给模型：

- `node.SelectForAgent()` / `IReadOnlyCollection<INodeComponent>.SelectForAgent()` **并入**该树的选中集（并集，重复不计）；`tree.ClearAgentSelection()` / `tree.GetAgentSelection()` 清空与读取。
- `node.DeselectForAgent()` / `IReadOnlyCollection<INodeComponent>.DeselectForAgent()` 只释放指定的节点，保留其余节点与顺序；未选中/未挂载的节点忽略，未命中任何快照时不推进 revision。
- **返回值是 HashSet 风格且全程静默**：单节点注册/释放返回 `bool`（是否真的新增/移除）；集合注册/释放返回 `int`（实际新增/移除的个数）；内部异常返回 `-1`。null、未挂载、不在 `tree.Nodes`、没有稳定 `RuntimeId` 的节点直接跳过，不抛异常。`ClearAgentSelection` 返回释放数（null 树 / 空集为 0，内部异常 -1）。要“替换”选中集，先 `ClearAgentSelection` 再注册。
- 存储是**每棵树一份**的 `ConditionalWeakTable<ITreeComponent, State>`（`WorkflowAgentSelection.cs`）。选中集在调用时取快照（稳定 `RuntimeId` + 类型名），渲染不读活模型，所以 agent 线程与 UI 线程不共享可变状态。
- **移除靠反向表**：`ConditionalWeakTable<INodeComponent, NodeOwner>`（值是树的 `WeakReference`）。`ConditionalWeakTable` 在 netstandard2.0 上**不可枚举**，所以不能遍历所有树；反向表让节点删除后 `Parent` 已为 null 时仍能找到那棵树。`Set` 负责同步旧/新节点的归属，`Remove` 只清归属未变的那一项。
- **选中即订阅，删除/重父自动移除**：`Observe` 订阅节点的 `PropertyChanged`（`Parent`）、父树的 `Nodes.CollectionChanged`（Remove/Replace/Reset）与 helper 的 `INodeEvents.Deleted` 三条；`Unobserve` 在节点移出选中集时对称摘除。三条在标准删除路径上都会触发，`Remove` 幂等；`Nodes.Clear()` 的 Reset 与整条 `Nodes` 属性被替换都直接清空该树选中集。**订阅只观察工作流，不写模型**；`TreeObserver` 只持树的弱引用，避免「树 → 集合 → 观察者 → 树」。
- `WorkflowAgentScope.ContextKey` 把选中集 revision 与 `Version`、`UsageBand` 分开打包（24/24/3 位）；selection 改 → 上下文重渲染，但 `Version` 不变 → `BuildDynamicTools` 不重建工具。**这是「不加工具」的机制保证**。
- `RenderEnvelope` 在闸门之前调 `AppendAgentSelection`；标题 `## Current Node Selection` / `## 当前选中的节点`，最多列 64 个身份，计数仍是全量。
- 渲染仍然只读快照，不回头读 `tree.Nodes`（避免在 agent 线程遍历 UI 集合）；删除、重父、从 `Nodes` 移除、集合清空/替换由上面的订阅在 UI 线程上驱动。
- 守卫：`Src/Test/Unit/Core/VeloxDev.Core.Extension.Test/Agent/Workflow/WorkflowAgentSelectionTests.cs`。

### 八点五、Demo 接线：Ctrl+click 与卡片光环

**全量** demo 接入（Trimmed 不接，它们没有 Agent）：

- 共享桥：`Examples/Workflow/Common/Lib/ViewModels/Workflow/AgentSelection.cs`。`Toggle` 调扩展的 `SelectForAgent`/`DeselectForAgent`，再用**静态 `Changed` 事件**广播；不往节点模型写伪属性，也不触发 `PropertyChanged`。
- 输入点：各家的 `VetoFrameworkGestures`（WPF/Avalonia/WinUI/MAUI/WinForms/Blazor）在 `InputModifiers.Control` 分支里调 `AgentSelection.Toggle(e.Target as INodeComponent)` 并照旧置 `PreventDefault`；Jalium 在 `NodeEditorSurface.OnLinkPointerPressed` 里处理 Ctrl+左键。Ctrl+click 因此同时阻止框架的拖拽手势。
- 视觉（紫罗兰 `#A78BFA`）：
  - WPF：`AgentSelectionVisual` 在外层 `UserControl` 上写 attached `IsSelected`；`Themes/CardTheme.xaml` 的 `CardBorderStyle` 用 DataTrigger 加 2px 边框 + `DropShadowEffect`。节点视图构造函数各调一次 `Attach(this)`。
  - Avalonia：`AgentSelectionVisual` 给 `UserControl` 加 `agent-selected` 类；`CardTheme.axaml` 的 `.agent-selected Border.card` 设边框 + `BoxShadow`。⚠ **类型选择器 `UserControl.agent-selected` / `Control.agent-selected` 不命中派生视图**，必须用类选择器 `.agent-selected`。
  - WinUI/MAUI：`AgentSelectionVisual` 在 Loaded 后找到卡片 `Border`，直接改 `BorderBrush`/`BorderThickness`（WinUI）或 `Stroke`/`StrokeThickness`/`Shadow`（MAUI），离开时恢复原值。
  - Jalium：`NodeViewBase.Bind` 在 Viewbox 外放一个 ring `Border`，`AgentSelection.Changed` 到达时切 `Visibility`。
  - WinForms：`WorkflowNodeCard.OnPaintBackground` 在卡片圆角路径上画两层紫罗兰描边；`AgentSelection.Changed` 触发 `Invalidate()`。
  - Blazor：`TemplateNodeView.razor` 给 `.wf-card` 加 `agent-selected`；代码后置订阅 `AgentSelection.Changed` 并 `StateHasChanged`；`app.css` 用 `outline` + `box-shadow`（不改布局）。
- 所有卡片视觉都按**引用相等**匹配自己的节点；订阅在视图卸载/DataContext 置空时解除，避免静态事件把池化视图拽住。
