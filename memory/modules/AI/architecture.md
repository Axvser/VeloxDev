# AI — 架构

> 代码：`Src/Core/VeloxDev.Core/AI/`（20 个 .cs）。**目录名 `AI`，命名空间是 `VeloxDev.AI`** —— 两者不同名，`grep VeloxDev.AI` 会连带命中消费方。
> **这个模块已经一行反射都没有了**（2026-10-03 起）。五个助手全部读编译期目录 `AIContextTree`（见 §七），动作经每类型生成的 `IAIContextAccessor` 落地：`AgentContextReader` / `AgentTypeResolver` / `AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor`。三个标注特性**在 Core 里已经没有读取点**，只有生成器读它们。**下面 §一~§六 仍然成立**（讲的是可观察行为，那些基本都保住了），但凡涉及「怎么做的」的句子都以 §七 为准。
> 消费方：`Src/Core/VeloxDev.Core.Extension/Agent/`（**54** 个 .cs，命名空间同样以 `VeloxDev.AI` 起头：`VeloxDev.AI.Pipelines`、`VeloxDev.AI.Workflow`、`VeloxDev.AI.MCP`、`VeloxDev.AI.Skills`、`VeloxDev.AI.SubAgents` 等）。**别把 `Agent/` 与整个 `VeloxDev.Core.Extension` 项目搞混**：整个项目是 97 个 .cs（2026-10-04 实测；含 `Compat/NotNullWhenAttribute.cs` 这类非 Agent/ 下的文件），`Agent/` 只是其中 54 个（其余是框架侧的 `WorkflowSystem` 辅助、`TransitionSystem`、`MVVM` 等，与本模块无关）。**依赖方向单向：Extension → Core**。Core/AI 不引用 Extension 的任何东西，也不引用 `VeloxDev.WorkflowSystem` 的类型 —— 唯一一处提及是 `SlotSelectorsAttribute.cs:4` 的 XML 注释 `cref`（同程序集，所以能解析，但不是编译期依赖）。
> 平台差异：**本模块没有平台轴**。整个 `AI/` 只用两个 BCL using —— `System.Reflection`（`AgentContextReader.cs:1` 取 `MemberInfo` 的声明类型与名字，不是对元数据做遍历）与 `System.Globalization`（`AIContextConvert.cs:1`）；没有任何 GUI 依赖。所以本模块没有 `adapters/`。

本文只写「读完 20 个文件才知道的东西」。类型清单、成员表、继承树请看 IDE。

---

## 一、这个模块是什么，不解决什么

**是什么。** 一组**无状态静态助手 + 三个标注特性 + 一组事件 DTO/接口**，作用是把一个任意 .NET 对象上「Agent 能调的东西」（`ICommand` 属性、公开方法、公开属性、挂在类型/成员上的自然语言说明）**枚举出来、读出来、写进去、调起来**。助手读的是编译期目录，动作经生成的访问器落地（§七）。

一句话：**`object` → 描述符列表；描述符 + 字符串 → 调用**。

**不解决什么**（这几条决定了你找不到代码时该往哪看）：

| 你以为在这里 | 实际在哪 |
|---|---|
| 工具怎么变成 `AITool`、工具名/描述/JSON 参数长什么样 | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:54`（`CreateTools`）、`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs` |
| 调用上限、拒绝、埋点、事件流水线 | `Src/Core/VeloxDev.Core.Extension/Agent/Pipelines/`（`AgentPipeline`/`ToolPipeline`）。Core 的 `AgentToolCallEventArgs` 是**事后**通知，且不含结果成败字段 |
| 弹窗、用户点「同意/拒绝」的 UI | 七家 demo，例如 `Examples/Workflow/WPF/Demo/Views/Workflow/WorkflowView.xaml.cs:452`（`ShowConfirmationDialogAsync`）。Core 只给 `AgentConfirmationEventArgs`/`AgentSelectionEventArgs` 两个 DTO |
| 「等用户点完」的异步握手 | **不在 Core**。`EventHandler` 是同步的，所以接口注释只能写「signal any awaitable completion mechanism」（`AgentConfirmationEventArgs.cs:41-42`）；真正做这件事的是消费方的 `Func<..., Task>` 形状（`Src/Core/VeloxDev.Core.Extension/Agent/Workflow/WorkflowAgentScope.cs:726`、`:753`） |
| 线程编组 | **不在 Core**。目录读写与访问器调用都在**调用方线程**上跑；`AgentObjectToolkit` 明说它刻意不注册编组（`Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:90-92`） |
| 语言包 / 翻译 | 只有 `AgentLanguages` 枚举与码表，**没有一条文案**。文案由作者写在 `[AgentContext]` 上 |
| 类型 schema（属性列表、枚举值、默认实例） | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/TypeIntrospector.cs:39`（`GetTypeSchema`）；Core 的 `AgentTypeResolver` 只做「字符串 → `Type`」 |

### 本模块**不是**唯一的实现路径（读到这里之前一定会踩）

消费方**复用本模块的东西分几层**：`AgentContextAttribute`/`AgentCommandParameterAttribute`/`SlotSelectorsAttribute` 三个**特性类型**、`AgentContextReader`/`AgentTypeResolver` 两个纯读助手、事件 DTO 与接口，以及 §七 的目录面（`AIContextDirectory`/`AIContextTreeRegistry`/`IAIContextAccessor` —— 消费方直接读目录、直接调访问器）。**命令的发现/执行与属性的写入这两条在 Workflow 侧是分叉实现**：

| 问题 | Core 通用路径 | Workflow 路径（各写各的） |
|---|---|---|
| 命令怎么被列出/执行 | `AgentCommandDiscoverer`，调用者 `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs:224`、`:247` | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/CommandInvoker.cs:32`、`:64`，调用者 `.../Workflow/Functions/WorkflowAgentToolkit.cs:989`、`:1041`、`:1057` |
| 属性怎么被批量写 | `AgentPropertyAccessor.SetProperties`（`AgentPropertyAccessor.cs:164`） | `.../Functions/ComponentPatcher.cs`（`ApplyPatch` `:41`，调用者 `.../WorkflowAgentToolkit.cs:930`、`:942`），连标量对拷都另写了一版（`ComponentPatcher.cs:251`） |

两条路在**可观察行为**上不同，所以「改 Core 的规则」和「改工具的行为」不是同一件事。**`CommandInvoker` 现在也读目录**（`CommandInvoker.cs:39` 的 `MembersAcross`），但它仍是一份独立实现：自带一个**同名的 `CommandDescriptor`**（`CommandInvoker.cs:120`，与 `AgentCommandDiscoverer.CommandDescriptor` 同名不同命名空间，字段是 `Type? ParameterType` 与 `Descriptions` 的 `KeyValuePair<AgentLanguages,string>`）；**不做语言过滤** —— 把节点上每种语言的说明全部倒出来（`:45`）；参数用生成的 `VeloxJsonSerializer.Deserialize` 反序列化（`:89`）而不是 `AgentMethodInvoker` 的 `AIContextConvert` 转换表。**枚举参数两条路现在都能通**（见 §五·2）。

---

## 二、三个特性 vs 读它们的代码（本模块的主轴）

| 特性 | 声明位置 | Core 里谁读它 | 消费方谁读它 |
|---|---|---|---|
| `AgentContextAttribute` | `AgentContextAttribute.cs:5` | **只有生成器**（`Src/Generators/VeloxDev.Core.Generator/Base/AIContextModel.cs:587` 的 `ReadTexts`）。运行期没有读取点：说明在编译期就被渲染成 `AgentText` 存进目录，Core 的 `AgentTextSelection.Select`（`AgentText.cs:63`）是**语言与回退规则的唯一所在**，`AgentContextReader` 只是按 `DeclaringType`+`Name` 查节点后转调它 | `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/AgentContextCollector.cs:33`（转调 Core）、`.../Workflow/Functions/TypeIntrospector.cs:96` |
| `AgentCommandParameterAttribute` | `AgentCommandParameterAttribute.cs:10` | **只有生成器**（`AIContextModel.ReadCommandParameterType` `:871`）。存进目录的方式是节点上一条 `AIContextRefKind.CommandParameterType` 引用 | 运行期只有 `.../Agent/Workflow/Functions/CommandInvoker.cs:44`、`:83` 经访问器的 `ParameterType(commandName)` 取它（`typeof` 字面量），用于反序列化参数 |
| `SlotSelectorsAttribute` | `SlotSelectorsAttribute.cs:38` | **没有。Core 一处都不读它**（生成器读，存成 `SlotSelectorType` 引用 + `HasSlotSelectors` 标志） | `.../Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1253`（展示白名单）、`:1401`、`:1451`（校验）；`.../Functions/ComponentPatcher.cs:124`（拒绝直接 patch） |

**结论**：`SlotSelectorsAttribute` 住在 Core/AI，但它是**给消费方的 Workflow 工具箱用的**（语义属于 `SlotEnumerator<TSlot>` 的校验白名单）。Core 只是提供了一个「编译器保证的常量容器」。动它的语义 = 动 Extension，Core 无感。

### `[AgentContext]` 的四条硬规则（读代码得出，不是读注释得出）

1. **说明不沿继承链或接口下沉。** 类型/成员上的 `[AgentContext]` 在编译期就落到它自己那个目录节点上，运行期没有「继承」这一步：`AgentContextReader.EntryFor(member)` 按 `member.DeclaringType` 查节点（`AgentContextReader.cs:77-82`），所以继承来的成员读的是**基类**那条说明，派生类型不会多出一份；属性/方法也不扫接口。命名空间里已经不存在 `inherit:` 参数 —— 说明本就不从元数据读取。
2. **有语言回退，且是「整目标、全有或全无」。** 规则集中在 `AgentTextSelection.Select`（`AgentText.cs:63`）：先取 `Language == language` 的；若**一条都没有**且请求的不是 `English`，则整体改取 `English` 的那些。所以「同语言命中 ≥ 1 条」时**不会**混入英文；`English` 自身无处可退。测试 `Src/Core/VeloxDev.Core.Test/AI/AgentContextReaderTests.cs`：`..._FallsBackToEnglish`、`..._FallbackIsAllOrNothing`、`..._EnglishRequestNeverFallsBack`、`..._NoEnglishEither_ReturnsEmpty`。**三个助手走 `AIContextMembers.DescriptionsFor(node, language)`（`AIContextMembers.cs`），按 `MemberInfo`/`Type` 进来的走 `AgentContextReader`** —— 两条都只是转调 `Select`，规则不再有第二份拷贝，改它只需改 `Select` 一处。
3. **`HasAgentContext` 不看语言**（`AgentContextReader.cs:52-53`）：只要有任何语言的标注就为 true，与 `GetContexts` 的选取无关。
4. **一个语言可写多条，全部返回且保序**（`AllowMultiple = true`，`AgentContextAttribute.cs:4`；测试 `.../AgentContextReaderTests.cs` 断言 English 命中 2 条）。`Context` 默认空串，所以 `[AgentContext(AgentLanguages.English)]` 合法但什么都不说明。

**`ICommand` 是唯一「接口上的特性算数」的地方。** 运行期不再扫描接口：`AgentCommandDiscoverer.DiscoverCommands` 直接遍历目录里的 `Commands` 节点（`AgentCommandDiscoverer.cs:79-90`），去重交给 `AIContextDirectory.MembersAcross` 的 `seen` 集合（`AIContextDirectory.cs:98-111`）。「接口上的标注才算数」这条规则前移到了生成期 —— `AIContextModel.InterfaceCommandProperty`（`AIContextModel.cs:783`，在 `BuildProperty` `:739` 与 `BuildPromotedCommand` `:969` 里只对命令调用）按同名属性回查接口，把 `[AgentContext]`/`[AgentCommandParameter]` 复制到实现类的命令节点上。对**方法**与**普通属性**没有对应的接口回查，接口上的特性一律读不到。命令标注的范本仍是 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowTreeViewModel.cs:33` 那一族。

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

**这里没有「谁拥有状态」的问题，只有「谁有副作用」的问题。** 全是静态类和每次调用新建的 DTO，**静态可变状态为零**（`AIContextTreeRegistry` 的 `_fragments` / `_accessors` 只在模块初始化时写一次；`AgentLanguagesExtensions.LanguageCodeMap`，`AgentLanguages.cs:45`，初始化后再无写者）；唯一被长期持有的状态是消费方那边的被代理对象。有副作用的入口只有四个：`Execute`、`Invoke`、`SetPropertyValue`/`SetProperties`、`CopyScalarProperties`。

---

## 四、不变量

1. **`CanExecute` 只被报告，从不拦截。** `AgentCommandDiscoverer.Execute` 不查 `CanExecute`，消费方也不查 —— `AgentObjectToolkit` 把 `CanExecute` 原样放进 `ListCommands` 的输出（`.../Agent/AgentObjectToolkit.cs:232`），而 `ExecuteCommand` 直接调 `Execute`（`.../Agent/AgentObjectToolkit.cs:247`）。要拦只能由工具层自己拦。**生成器那条闸已经删掉了**：`WriteTryExecuteCommand` 曾经发一句 `if (!c.CanExecute(parameter)) return false;`，与 `IAIContextAccessor.CanExecuteCommand` 的注释直接矛盾 —— 搬入时按不变量改成不查（`Src/Core/VeloxDev.Core.Test/AI/AgentCommandDiscovererTests.cs` 的 `CanExecute_IsReportedAndNeverEnforced` 锁住）。
2. **`CanExecute` 的实参恒为 `null`**（`AgentCommandDiscoverer.cs` 的 `DiscoverCommands` 与 `CanExecuteCommand`）—— 命令的 `ParameterType` 完全不参与。后果：需要非空参数才返回 `true` 的命令被报成 `canExecute: false`（假阴）。
3. **命令名规范化单向且大小写敏感**：`name.EndsWith("Command") ? name : name + "Command"`（`AgentCommandDiscoverer.cs:191-192`）。发现时给的是**原始属性名**（`"SaveCommand"`），执行时 `"Save"` 与 `"SaveCommand"` 都行，但 `"savecommand"` 会被拼成 `"savecommandCommand"` 然后找不到。
4. **`[AgentContext]` 的语言参数占了位置参数第一位**（`AgentContextAttribute.cs:5`），所以写不出 `[AgentContext("说明")]`（字符串撞 `AgentLanguages`，编译不过）。官方写法是 `[AgentContext(AgentLanguages.Chinese, "说明")]`，全仓一致，例 `Src/Core/VeloxDev.Core/Interfaces/WorkflowSystem/IWorkflowViewModel.cs:11`。
5. **确认题的默认答案是「拒绝」**：`AgentConfirmationEventArgs.Result` 初值 `Deny`（`AgentConfirmationEventArgs.cs:30`）。处理器忘了赋值 = 拒绝，fail-closed。消费方在这之上做 `AllowAlways` 的会话级持久化（`.../Agent/Workflow/WorkflowAgentScope.cs:770-783`）。
6. **`AgentLanguages.Chinese` 与 `ChineseSimplified` 是同一个值**（`AgentLanguages.cs:8`，值 1）：枚举共 34 个名字 / **33 个不同值**。所以 `ToLanguageCode`/`GetDisplayName` 里 `Chinese or ChineseSimplified`（`:108`、`:191`）只覆盖一个值。**未定义值会抛** `ArgumentOutOfRangeException`（`:140`、`:223`）—— 例如反序列化来一个 `(AgentLanguages)99`。
7. **码表 41 条 → 33 个值，方向不对称**（`AgentLanguages.cs:45-88`）：`no`/`nb`/`nn` 都 → `Norwegian`，但 `Norwegian.ToLanguageCode()` 只回 `no`；`zh`/`zh-hans`/`zh-cn`/`zh-sg` → `ChineseSimplified`，回程只有 `zh-Hans`。`TryParseLanguageCode` 额外做两件事：先把 `_` 换成 `-`（`:157`），失败后再按 `-` 切首段重试（`:163-166`）—— 重试取的是**第一个 `-` 之前的那一段**，所以命中不了 `zh-hk` 这类两段码：`zh-HK-x-foo` 会退到 `zh`（→ `ChineseSimplified`），要命中 `zh-hk` 只有写成 `zh-HK` 本身。

---

## 五、反直觉（带依据）

1. **重载解析仍按实参个数，不按类型。** 生成期把同一名字的方法排成 `switch (args.Length)`：精确元数优先，其余元数由「必填数 ≤ 个数 < 总数」的那个重载接手（尾部缺的参数由生成代码**省略实参**，编译器补它自己的默认值 —— 所以目录里不需要存默认值，只需要 `Optional` 标志）。**同元数的重载谁赢取决于声明顺序**，`Add(int)` 与 `Add(string)` 仍然无法区分。
2. **枚举参数现在能进去了。** 生成的转换走 `AIContextConvert.ToEnum<TEnum>`（字符串按名字、数字按底层值，`AIContextConvert.cs:126`）。旧的反射路径在这里会 `Convert.ChangeType` 抛 `InvalidCastException` 再被吞掉 —— 那是 `AgentMethodInvoker` 与 `AgentPropertyAccessor` 两条路不对称的时代。数字形态也能落到枚举上（`ToEnum<TEnum>` 对非字符串走 `Enum.ToObject`，`:135`）。
3. **`GetPropertyValue` 对「不可读」与「不在目录里」都回 `null`，不抛。** `CanRead` 现在也不是「有 getter」而是「有**公开** getter」，所以 `{ private get; set; }` 走「不可读」这条路 —— 旧的反射实现会在这里抛 `ArgumentException`（行为变化见 §七）。
4. **`CopyScalarProperties` 现在转调访问器的 `CopyScalarFrom`**，搬的是**两个类型共有的、可写的全部成员**，不再限「11 种标量 + 枚举」（旧的也不做转换，靠 `SetValue` 抛出后吞掉；新的是生成代码里 `(T)value!`）。**它零生产调用者** —— 真正跑的是 `ComponentPatcher.CopyScalarProperties` 那份自带白名单的分叉（`extension.md` §二·7），改 Core 这份不影响实际路径。
5. **`SetProperties` 的 `rejected` 是按名字精确比对的 `ISet<string>`** —— 没有通配、没有前缀规则。这条没变。
6. **`AgentCommandDiscoverer` 不再有静态面**：`InvokeStatic` 与 `DiscoverMethods(includeStatic:)` 已删除（目录不录静态方法，访问器只对实例做事），`MethodDescriptor.IsStatic` 与 `ParameterDescriptor.DefaultValue` 一并删除（全仓零读取者）。
7. **「后备方法」的猜测没了**：旧的 `FindParameterAttribute` 会拿 `commandName.Replace("Command","")` 去猜属性背后的方法，`"CommandHistoryCommand"` 会被猜成 `"History"`。目录里没有这种启发式 —— 参数类型只来自 `[AgentCommandParameter]`，接口上写了就以接口为准（见 §七）。
8. **`DiscoverCommands` 的顺序变成声明顺序**（目录保序），不再是 `GetInterfaces()` 的未定义顺序。名字仍按名字去重；**同名不同元数的方法在目录里只能留一个节点**（`AIContextTreeRegistry.List` 按名字去重），所以 `DiscoverMethods` 一个方法名只报一条 —— 但 `Invoke` 仍按实参个数支持全部重载（访问器里 `switch (args.Length)` 每个合法个数一个分支）。
9. **`AgentContextReader.HasAgentContext` 不看语言**：只要有任何语言的 `[AgentContext]` 就为 true，与 `GetContexts` 的选取无关。
10. **Core/AI 已零反射，裁剪/AOT 契约也开在 `net8.0` 那一档了。** `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj:5` 的 TargetFrameworks 是 `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`，其中 `IsAotCompatible` 只对 `net8.0` 为 true（早期几档不支持分析器，靠 net8.0 替它们守）；Extension 的 csproj 同样如此。**反射已从 Core 与 Extension 两侧全部清零**（`…ByReflection` 回退体现在住在测试工程 `ReflectionContextOracle`）；少数无法消除的点改成「按需声明」而不是消除 —— 见 §七。

---

## 六、入口：我要改 X，先打开哪个文件

| 想改的东西 | 先打开 |
|---|---|
| 命令怎么被执行（名字规范化、错误文本） | `AgentCommandDiscoverer.cs`（`Execute` / `CanExecuteCommand` / `NormalizeCommandName`） |
| **哪些成员算命令、叫什么名字、说明与参数类型从哪来** | **不在本模块** —— 生成期：`Src/Generators/VeloxDev.Core.Generator/Base/AIContextModel.cs`（`BuildProperty` / `BuildPromotedCommand` / `InterfaceCommandProperty`） |
| 方法重载怎么挑、实参怎么补/转 | 生成期：`Src/Generators/VeloxDev.Core.Generator/Writers/AIContextTreeWriter.cs` 的 `ArityPlan` / `WriteInvokeBody`；转换表在同文件的 `RenderConversion` + `AIContextConvert.cs` |
| 属性读写与类型转换规则 | `AgentPropertyAccessor.cs`（发现与拒绝文案）；转换在生成代码里，同上 |
| 类型/成员的继承链怎么走 | `AIContextDirectory.MembersAcross`（`AIContextDirectory.cs`）—— **只此一份**，渲染器与三个助手都转调它 |
| 说明文字怎么取（语言、回退、多值） | `AgentTextSelection.Select`（`AgentText.cs`）一处；节点侧的入口是 `AIContextMembers.DescriptionsFor`，`MemberInfo`/`Type` 侧的入口是 `AgentContextReader`，都只是转调，见 §二表 |
| 语言枚举与码表 | `AgentLanguages.cs:4-40`（枚举）、`:45-88`（码表）、`:97`（`AllLanguages` 派生列表）、`:103`/`:186`（两处 switch：`ToLanguageCode`/`GetDisplayName`） |
| 确认 / 选择 / 工具调用的事件负载 | `AgentConfirmationEventArgs.cs`、`AgentSelectionEventArgs.cs`、`AgentToolCallEventArgs.cs` |
| `SlotSelectors` 的行为 | **不在本模块** —— `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs:1253`、`:1401`、`:1451`、`.../Functions/ComponentPatcher.cs:124` |
| 工具面的整体装配（谁把描述符变成工具） | `Src/Core/VeloxDev.Core.Extension/Agent/AgentObjectToolkit.cs`（通用对象）、`.../Agent/Workflow/WorkflowAgentScope.cs`（工作流） |

---

## 七、AIContextTree：编译期静态目录（Core 与 Extension 都已全部接入）

**为什么有它。** §五·10 记的那条 —— 整套 Agent 面靠运行期反射，没有任何裁剪/AOT 契约。`AIContextTree` 是把「Agent 面是什么」和「怎么对一个对象动手」都前移到编译期的那条路，目标就是让这套东西在 NativeAOT 下工作。**它现在是唯一的路径**：Core 五个助手与 Extension 的 `AgentContextCollector`/`CommandInvoker`/`ComponentPatcher`/`TypeIntrospector`/`WorkflowAgentScope` 全部读目录；原先那些 `…ByReflection` 实现搬进了测试工程当 parity 的 oracle。

### 两半，必须分开理解

| | 装什么 | 为什么 |
|---|---|---|
| `AIContextTree`（目录） | 字符串、枚举、整数、数组。**没有** `Type` / `MemberInfo` / 委托 | 模型被**告知**什么。它里面任何东西都不 root 元数据，所以可裁剪 |
| `IAIContextAccessor`（访问器） | 每个类型一个生成的类，含 `Type TargetType => typeof(T)` 与成员操作 | 对对象**做**什么。`typeof` 字面量裁剪安全，且恰好 root 访问器真正碰到的成员 |

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

`Components` 下四分不是发明的：`WorkflowAgentScope` 的自动发现本来就按这四个接口分类。四个默认视图模型、框架枚举/接口/数据也都在 `Framework/` 里；那些曾经手维护的 `Type[]` 已经删除，`WithAutoDiscovery` 现在只是按目录列举（`WorkflowAgentScope.cs:969-986`）。

### 惰性：一个目录一个 `Lazy`

生成器**逐目录**产出 `Lazy<AIContextNode[]>`，`ChildrenOf(path)` 是一个按路径字符串分派的 `switch`。列一个目录只构建那个目录；解析一个 `Ref` 是独立调用，没有任何遍历会为了它走进子目录。类型名索引同样是首次访问时才建字典。

### 生成器：`Src/Generators/VeloxDev.Core.Generator/AIContextTree.cs`

**它不套用 `Analizer.Filters.Targets`**，三处独立的理由：那个管线只接受 `ClassDeclarationSyntax` 且要求 `partial`（接口与枚举进不来，而它们是目录的一等公民）；它的触发集 `TriggerAttributes` 不含三个 Agent 特性；而组件的身份是**实现了哪个接口**，特性触发表达不了。它走一次编译级遍历，一次 `AddSource`。两条守卫：编译单元里没有 `VeloxDev.AI.AgentContextAttribute` 就整体不产出；`VeloxAgentContextTree=false` 可整体关闭。

**框架分片必须在 Core 内部生成。** 决定性理由：目录要收录带 `[VeloxProperty]` 的**私有实例字段**（`AIContextModel.ReadMembers` 的私有字段分支 `:697-704`；这也正是旧 `…ByReflection` 实现里 `BindingFlags.NonPublic` 做的那件事，如今那份实现住在测试工程的 `ReflectionContextOracle`），而 Roslyn **引用程序集里没有私有成员** —— 消费者侧去读引用程序集符号会静默丢掉每一个字段行。

### 三条必须记住的坑

1. **生成器之间看不见彼此的产物。** 目录里的 `Channel` 是 MVVM 生成器写出来的属性，`SaveCommand` 是 CommandWriter 写出来的 —— `AIContextTree` 生成器**看不到它们**，只能复现命名规则。所以规则抽在 `Base/AIContextNaming.cs`，`MVVMFieldAnalizer`（`Analizer.cs:223`，转调点 `:270`）与 `CommandWriter.cs:155` 都改为转调它。**改命名规则只改那一处；改一处漏一处会让目录开始命名不存在的成员，报错落在消费方编译里。**
2. **`[VeloxCommand]` 方法在实现类里普遍是 `private`。** 生成出来的命令属性却是公开的，所以命令的收录**不能按方法可见性过滤** —— 按 `Public` 过滤会把整个命令面漏掉（`SlotDefaultViewModel` 的四条命令全是 private）。
3. **访问器够不着别的类型的 `private` 成员。** 提升属性靠的是**生成出来的那个属性**（同一个类里，编得过），不是字段本身。

### 明确不在目录里的东西（都不是漏，是判过）

泛型类型与泛型方法（`T` 在生成的 cast 里没有绑定）、静态类（`(Static)target` 编不过）、`private`/`protected`/`file` 类型、编译器生成的名字、`object` 的四个成员（`ToString`/`GetHashCode`/`Equals`/`GetType` —— 生成器的 `IsObjectMember` 把它们滤掉，理由一样：没有作者想把它们放进 Agent 面）。同元数的重载只留一个，并报 `VELOX_AI_TREE001`。

**`VELOX_AI_TREE001` 差点变成噪音，值得记一笔。** 它第一次跑就报了 8 条，全在 `Anchor`/`Offset`/`Size`/`Scale`/`Viewport`/`CellKey`/`ThreadRef` 上 —— 报的是 `Equals(object)` 与 `Equals(T)` 那一对。每个带强类型 `Equals` 的值类型都会中，而没人想重载 `Equals` 给 Agent 用。**先把 `object` 的四个成员滤掉，诊断才只剩真正需要作者知道的东西**；顺序反了的话，这条警告的下场就是被整仓 `NoWarn` 掉。

### 生成器开销（2026-10-03 实测，单次墙钟，非基准）

| 工程 | 生成器开 | 生成器关 | 差 |
|---|---|---|---|
| Core（分片 453KB）clean | 3121 ms | 2613 ms | +508 ms |
| Core **touch 增量** | 2534–2796 ms | 2281–2335 ms | **+280 ms（~12%）** |
| `Examples/Workflow/Common/Lib`（分片 145KB）clean | 3535 ms | 3123 ms | +412 ms |
| 同上 **touch 增量** | 1965–2054 ms | 1944–2015 ms | **+8 ms（噪声级）** |

**代价集中在 Core**，因为它自己的分片最大。消费方的增量代价落在噪声里。关掉的方式是 `-p:VeloxAgentContextTree=false`。这几个数是单次墙钟、NuGet 已预热，当量级看，别当基准。

### 验证在哪

- `Src/Core/VeloxDev.Core.Test/AI/AIContextTreeTests.cs`（8 条）—— 注册、目录列举、条目查找、访问器读写与执行。含**反向对照**：一个没被标注的类型必须查不到路径也没有访问器 —— 目录一旦放宽收录规则，那条会先红。
- `Src/Core/VeloxDev.Core.Test/AI/AIContextTreeGeneratorTests.cs`（5 条）—— 走 `GeneratorProbe` 驱动生成器，断言提升名、命令名、重载诊断、以及没有 Agent 面时**不产出**。
- `Src/Core/VeloxDev.Core.Extension.Test/Agent/Workflow/AgentContextTreeParityTests.cs` —— **最重要的一条**（单个 `[TestMethod]`，运行时遍历 `Framework/` 下每个类型条目 × 2 种语言）：目录渲染与反射渲染逐字对比。它是「换数据源不换输出」的唯一保证。**块数不可静态复核** —— 记忆写下时是 63 个框架类型 × 2 语言 = 126 个块，以测试实际遍历为准。

### 当前状态（2026-10-03，第三站已落：**Extension 侧也清零了**）

**实测**（当时把 `VeloxDev.Core` 与 `VeloxDev.Core.Extension` 临时多目标到 `net8.0` + `-p:EnableTrimAnalyzer=true`；如今 `net8.0` 档已进两个 csproj 并常开 `IsAotCompatible`）：

| 程序集 | IL2xxx 警告 |
|---|---|
| `VeloxDev.Core.Extension` | **0** |
| `VeloxDev.Core` | **4**，全在 `AI/` 之外 |

那 4 条：`CompileKeyNormalizer.cs`（`Type.GetType(string)`）、`SlotEnumerator.ResolveTypeByName`（`Assembly.GetType`，反序列化时按名字还原 `SelectorType`）、`TransitionSystem/Binding/TransitionProperty.cs` 与 `TransitionSystem/Sampling/Interpolator.cs`。**前两条属于反序列化**，第三条属于另一个模块 —— 都不在本轮的「Agent 面清零」范围内。

**注意**：`IsTrimmable=true` 单独用**不够**。SDK 10 上它不引 `Microsoft.NET.ILLink.Tasks`，于是「0 警告」是假象 —— 必须显式 `-p:EnableTrimAnalyzer=true`，并且 Core 也要多目标到 net8.0（否则 Extension 的 net8.0 构建引用不到它）。历史上那条 `-p:TargetFrameworks=...` 的命令在 SDK 10 上已经不成立（属性会泄漏给 Core，报 MSB3277）。

### 第三站改了什么（Extension 侧反射清零）

| 文件 | 换成了 |
|---|---|
| `AgentContextCollector` | 四个 `…ByReflection` **搬进测试工程**（`ReflectionContextOracle`）当 parity 的 oracle；库里只剩目录一条路 |
| `CommandInvoker` | 目录的 `Commands` 目录 + 访问器；`Invoke` 的参数类型来自 `IAIContextAccessor.ParameterType`（`typeof` 字面量） |
| `ComponentPatcher` | 目录标志（`CanWrite`/`IsSingleSlot`/`HasSlotSelectors`）+ `accessor.Set`；`DeserializeToType` 的目标 `Type` 来自 `MemberType` |
| `TypeIntrospector` | 目录 + `MemberType` + `accessor.Create`；`FriendlyTypeName` 只吃访问器给的 `Type?`（`typeof` 字面量），不再拿反射对象 |
| `WorkflowStateTracker` | `MembersAcross(type,"Properties")` + `accessor.TryGet` |
| `WorkflowAgentToolkit` | 一组共享助手：原先散落的 `GetProperty(...)`/`GetMethod(...)`/`GetInterfaces()` 反射调用换成目录查询、访问器转型与类型解析包装 |
| `WorkflowAgentScope` | `WithAutoDiscovery()` 无参化，四个手维护的 `Type[]` 删除 |

**`WithAutoDiscovery` 现在是目录列举**：读 `Customer/{Enums,Interfaces,Data}` 与 `Customer/Components/{Nodes,Slots,Links,Trees}`，每个名字经 `AgentTypeResolver.ResolveType` 换成 `typeof` 字面量再进既有的 `Customer*` 集合。**框架类型不再需要手写的排除表** —— 只读 `Customer/` 就把它们排除了。旧的两趟扫描（`GetTypes()` + 成员深扫推断）整个消失：目录本来就是那次推断的结果。

**为泛型补的两条路**（目录不收泛型类型，所以访问器够不着 `SlotEnumerator<TSlot>`）：

1. `VeloxDev.WorkflowSystem` 新增非泛型视图 —— `IConditionalSlotProvider`（擦掉 `TSlot` 的 `Parent`/`SelectorTypeName`/`SelectorType`/`Slots`/`TrySelect`/`SetSelector`）与 `IConditionalSlot`（`Name`/`Value`/`Slot`），两个泛型类显式实现。Toolkit 里那些 `GetProperty("Items")`/`GetProperty("Slot")`/`GetMethod("TrySelect")` 全部变成一次转型。
2. `IAIContextAccessor` 新增 `MemberType(member)` 与 `ParameterType(commandName)` —— 每处都是 `typeof` 字面量。这是唯一让 `Type` 合法离开访问器的地方，也是生成式序列化、`IsAssignableFrom` 判定、`Create` 之外所有需要 `Type` 的地方的解。

**`IAIContextAccessor` 现在的形状**：`TypeName` / `TargetType` / `HasPublicParameterlessConstructor` / `Create` / `MemberType` / `ParameterType` / `TryGet` / `Set` / `TryExecuteCommand` / `CanExecuteCommand` / `TryInvoke` / `CopyScalarFrom`。**值类型也能 `Create`**（`IsRefLikeType` 除外），所以 `defaultJson_runtimeOnly` 对 struct 依旧。

**新标志**：`Optional`(2048) / `IsValueType`(4096) / `IsSlotCollection`(8192)。`IsSlotEnumerator` 现在也认「实现了 `IConditionalSlotProvider<T>`」的类型，不再只认 `SlotEnumerator<T>` 本身。

### 第二站改了什么（Core 三个助手）

**Core/AI 五个助手全部搬完，反射实现全删。** `AgentContextReader` / `AgentTypeResolver` 是第一站；`AgentCommandDiscoverer` / `AgentMethodInvoker` / `AgentPropertyAccessor` 是第二站。三个标注特性在 Core 里已经没有读取点 —— **只有生成器读它们**。

**动作路径改的是 API 形状**：`PropertyDescriptor.PropertyType` / `MethodDescriptor.ReturnType` / `ParameterDescriptor.ParameterType` 从 `Type` 变成 `string`（**字段名保留**，只是不再发放 `Type`），`DiscoverProperties` / `DiscoverMethods` 的 `filter` 从 `Func<PropertyInfo,bool>` / `Func<MethodInfo,bool>` 变成 `Func<string,bool>`。删掉的是 `InvokeStatic`、`includeStatic:`、`MethodDescriptor.IsStatic`、`ParameterDescriptor.DefaultValue`（全仓零读取者）、`CopyScalarProperties` 的 `skip` 形参（零调用者，访问器的 `CopyScalarFrom` 没地方接它）。

**搬入时顺手修掉的三处**（都有测试锁住）：

| 修的是什么 | 原来 | 现在 |
|---|---|---|
| 生成的 `TryExecuteCommand` 会拒绝 `!CanExecute(parameter)` 的命令 | 与 `IAIContextAccessor.CanExecuteCommand` 的注释、与 §四·1 的不变量直接矛盾 | 不查 —— 不变量成真 |
| 实现类的命令节点没有说明与参数类型 | 反射那条先扫接口、接口上的标注才算数；目录只录实现类自己那个属性 | 生成器在 `AIContextModel.InterfaceCommandProperty` 里按同名属性回查接口，把 `[AgentContext]` / `[AgentCommandParameter]` 复制过来（**只对命令做** —— 对普通成员做会让 `Class` 表多出今天没有的行，打翻那 126 块 parity） |
| `BaseTypeName` 用 Roslyn 的 `global::A.B.C` 形状 | 目录索引键是 `Type.FullName` 形状（嵌套用 `+`），**嵌套基类型永远查不到**，继承来的成员整条丢掉 | 改用 `ReflectionFullName`。这个 bug 一直存在，是继承成员的新测试先红的 |

**会变的行为（都不在 126 块 parity 覆盖范围内）：**

- 工具 JSON 里的类型名从 `Type.Name` 变成目录里的全名形状（`Int32` → `System.Int32`，泛型带实参）。对模型更有信息量，但确实变了 —— `AgentObjectToolkitTests.TheListTools_ReportTheTypeNamesTheTreeCarries` 锁住。
- `DiscoverMethods` 一个方法名只报一条（目录按名字去重），`Invoke` 仍支持全部元数。
- 泛型方法不再出现（目录不录）。
- `GetPropertyValue` 对 `{ private get; set; }` 从抛异常变成回 `null`。
- 枚举参数经 `InvokeMethod` 从「传不进去」变成「能进去」。
- 闭世界：`AgentObjectToolkit` 只对生成器收录过的类型有效（被标注、或成员里有 `ICommand`、或是四个组件接口的实现）。任意 POCO 现在是「列不出、写不进、调不动」，不是「能用」—— 这是公开 API 的契约位移，写在 `AgentObjectToolkit` 的 `<remarks>` 里。

**第二站结束时没搬的都还在 Extension：** `WorkflowAgentToolkit`、`CommandInvoker`、`AgentContextCollector` 的四个 `…ByReflection` 回退体、`WorkflowAgentScope` 含 `assembly.GetTypes()` 的扫描、`TypeIntrospector`、`ComponentPatcher.ApplyPatch` —— **第三站已把这些全部搬完**（见上一节「第三站改了什么」）。另有 5 条**不属于 Agent 面**（WorkflowSystem 3、TransitionSystem 2）。

**AOT 警告数本轮没有重测**（要动 `IsAotCompatible` 影子工程）。第一站的基线是 `VeloxDev.Core` 28 → 26、`VeloxDev.Core.Extension` 225（162 条 Newtonsoft + 63 条自身反射）—— 那两个数现在都过期了。

### 现在警告清零了（2026-10-03，第三个里程碑）

**`VeloxDev.Core` 与 `VeloxDev.Core.Extension` 的 IL2xxx/IL3050 都是 0。** 收尾的两步都不在本模块：

1. **Newtonsoft 走了。** 工具面（`JObject`/`JArray`/`JsonConvert`，~280 处 / 13 文件）与存档路径一起换到
   `VeloxDev.Serialization` —— 一个生成式的 JSON 读写器加一套不反射的 JSON 树。**162 条 Newtonsoft 不是
   终点，可以不是**：当时写「要归零得换 `System.Text.Json` 源生成」，实际走的是自己生成的那条路。
   形状、闭世界、逐字节兼容、以及读入侧**实测确认消不掉的两处**，记在
   [`VeloxDev.Core.Extension/architecture.md`](../VeloxDev.Core.Extension/architecture.md) §八。
2. **Core 那 4 条**：`SlotEnumerator.ResolveTypeByName` 换成了目录查询（**实测**全绿，见上）；另三条
   （`CompileKeyNormalizer` 的路由键、`TransitionProperty.FindIndexer`、`Interpolator`）也都各自处理完了 ——
   前两处的换法**实测过会红**，所以是「按需声明」而不是消除，理由写在各自的注释里。

**已进仓库的**：`VeloxDev.Core` 与 `VeloxDev.Core.Extension` 的 csproj 都给 `net8.0` 那一档开了
`IsAotCompatible=true`（`VeloxDev.Core.csproj:5`、`VeloxDev.Core.Extension.csproj:5`），分析器在这一档常开。
`Examples/Workflow/Avalonia Trimmed/Directory.Build.props` 里那条 `TrimmerRootAssembly` **已经删了**，
但端到端的裁剪发布没验过 —— 桌面那条路被 `NETSDK1124` 挡着（`-p:PublishTrimmed=true` 是全局属性，会泄漏给
多目标的 Core/Extension），真正配了裁剪的是 Browser/Android 两条（此历史判断未再复核）。

### 这一轮踩到的坑（都在测试里钉住了）

- **`Anchor` / `Offset` / `Size` / `Scale` 是 class 不是 struct**，尽管名字像值对象。`GetTypeSchema` 的 `kind` 报的是 `class`，`IsValueType` 标志只对真的 struct（`CellKey`、`ThreadRef`）为真。
- **`TreeDefaultViewModel` 上没有 `Anchor`** —— 它是节点/插槽的属性。测「命令后备」那条要先把节点挂到树上（`tree.GetHelper().CreateNode(node)`），否则 `ResolveTree` 直接以「未挂载」拒绝。
- **`FriendlyTypeName` 只把「特殊类型」写短**：`ObservableCollection<VeloxDev.WorkflowSystem.IWorkflowNodeViewModel>` 里的类型实参仍是全名。这与旧实现逐字一致。
- **`TreeProperty.Get` 要吞异常**：生成的 `TryGet` 是裸读（`value = t.X`），一个会抛的 getter 会逃出去，而旧的反射遍历是 `try/catch` 包着的。
- **`GetType().Name` 不是反射警告源**：`Object.GetType` 与 `Type.Name`/`FullName` 都没有裁剪注解，所以那约 18 处「类型名标签」一行都不用改 —— 真正的警告来自 `GetProperties`/`GetProperty`/`GetMethod`/`GetInterfaces`/`GetCustomAttribute`/`Activator.CreateInstance` 这些**索取成员**的调用。

### 没有回退暴露出来的两条契约（不是 bug，是代价）

1. **夹具必须 `internal` 以上。** 生成器跳过私有 / `protected` / `file` 类型，所以任何要被 Agent 描述的类型都得可见 —— 测试里那批 `private sealed class` 夹具因此读不到说明，已改成 `internal`。真实场景同理：用户的组件类不能是嵌套私有的。
2. **`AgentTypeResolver.ResolveType` 成了封闭世界。** `ResolveType("System.String")` 返回 `null` —— 没有分片的类型解析不到。旧的实现扫 `AppDomain.CurrentDomain.GetAssemblies()`，那正是裁剪器跟不上的那一步。

### 生成期撞出来、改代码前必须知道的（都吃过一次）

- **生成器之间看不见彼此的产物。** 目录里的 `Channel` 来自 MVVM 生成器、`RunCommand` 来自 CommandWriter，`AIContextTree` 生成器看不到它们，只能复现命名规则 —— 规则抽在 `Base/AIContextNaming.cs`，`MVVMFieldAnalizer` 与 `CommandWriter` 都转调它。
- **MVVM 生成器产出的属性也在同一个编译里**，所以 `symbol.GetMembers()` 里既有私有字段又有它提升出的属性。不按名字去掉生成的那份，每个 `[VeloxProperty]` 会在目录里出现两次，顺序也被带偏。
- **同一个目录里不能有两个同名节点**：类型条目，与代表它成员目录的 Directory 占位。`List` 的去重方向一变就翻车（`SortedDictionary` 时代最后一个赢，换保序实现后第一个赢）。
- **`AIContextTreeRegistry.List` 不能排序** —— 排序会抹掉声明顺序，而表格逐字复现反射输出。
- **`Type.FullName` 与 Roslyn 显示名不同形**：嵌套用 `+` 不用 `.`、泛型带反引号元数、不带可空标注、不特殊化关键字（`System.Int32` 而非 `int`）。那个字符串既是索引键也是渲染出来那一行，所以 `Base/AIContextModel.cs` 里 `ReflectionFullName` / `TableType` 两个助手各管一头。**每个要去查目录的名字都必须走 `ReflectionFullName`** —— `BaseTypeName` 曾经图省事写成 `ToDisplayString(FullyQualifiedFormat)`，对嵌套基类型永远查不到，继承来的成员整条丢掉，而只有嵌套类型做基类时才现形（框架那 63 个类型全是顶层类，parity 一直是绿的）。
- **接口上的命令标注要在生成期带回实现类。** 目录只录类型自己声明的成员，而命令的 `[AgentContext]` / `[AgentCommandParameter]` 官方写在接口上；`AIContextModel.InterfaceCommandProperty` 按同名属性回查 `AllInterfaces` 补齐。**只对命令做** —— 对普通成员做会让 `AgentClass` 表多出行、打翻 parity。
- **`[VeloxCommand]` 方法在实现类里普遍是 `private`**，而生成出来的命令属性是公开的 —— 命令的收录不能按方法可见性过滤，否则整个命令面漏掉。
- **诊断要先滤掉 `object` 的四个成员**，否则 `VELOX_AI_TREE001` 会报在每个带强类型 `Equals` 的值类型上，下场是被整仓 `NoWarn` 掉。
- **缺省参数不用往目录里存默认值**：生成代码只需**少写一个实参**，C# 会在调用点补上声明的默认值。目录里只要一个 `AIContextFlags.Optional` 标志供描述符报 `IsOptional`，剩下的交给编译器 —— 字面量渲染那一整块复杂度因此不存在。

## 七·五、两个闭世界**不是同一个**，工具面正好卡在中间（2026-10-05）

`AIContextTree` 与归档序列化器各有一个闭世界，**收录条件不同**：

| | 谁进去 | 怎么进去 |
|---|---|---|
| **AI 闭世界** | 类型能被 `AgentTypeResolver.ResolveType` 解开 —— 即它有一条**类型条目**，因而有一个 `IAIContextAccessor` | 带 `[AgentContext]`（自己或成员）、是某成员的声明类型、被 `[SlotSelectors]` 之类**引用**到…… |
| **归档闭世界** | 类型能被 `VeloxJsonSerializer` 读写 —— 即生成器为它编了 reader/writer | 组件、`[VeloxProperty]`、`[Archivable]`、`[WorkflowBuilder.*]`，或从这些出发沿**成员的声明类型**走到 |

**能解析 ≠ 能重建。** 工具面有好几处是「模型给一段 JSON，宿主按类型名读回来」，那些地方要的是**两个闭世界同时成立** —— 而两个条件之间没有蕴含关系。迁移到自带序列化器之前，Newtonsoft 反射什么都能读，所以这条缝一直没露出来。

### 缝在哪（都实测过）

- **`SetEnumSlotCollection` 的非枚举路径**（`WorkflowAgentToolkit.cs`）：`TypeIntrospector.ResolveType(nonEnumTypeName)` 走的是 AI 闭世界，紧接着 `VeloxJsonSerializer.Deserialize(json, targetType)` 走的是归档闭世界。provider 是普通类（既不是组件也不是 `[VeloxProperty]` 持有者），**必须自己带 `[Archivable]`**。缺了它原来只会拿到引擎那句「has no registered JSON reader」—— 模型照着它去找宿主注册，什么也找不到。现在这句话会改成直接点名 `[Archivable]`。
- **`ComponentPatcher` / `CommandInvoker` 的枚举参数**：工具面**写**枚举用名字（`AppendScalarProperties`、`WorkflowStateTracker` 都写 `ToString()`），而归档引擎的运行期读法**刻意没有枚举分支**（`TryReadScalar` 对 enum 返回 null，生成的 reader 才用强制转换）。于是 `PatchNodeProperties {"CompileMode":"Static"}` —— 写在 `EnumSelectorNodeViewModel` 自己的 `[AgentContext]` 里的例子 —— 直接抛。**修在工具层**：`AgentJsonValue.Convert` 就地认名字或底层整数，引擎那条「没有独立枚举读法」的设计不动。
- **`VeloxJsonValue.From(context.Data)`**：运行载荷是宿主的业务对象，可以是任何一个归档不认识的类型，然后整个状态调用变成错误信封。`WorkflowAgentToolkit.DataJson` 现在兜底成文本。

### 两条**必须记住**的收录规则（比上面更常见）

- **`[SlotSelectors]` 只往目录里放一条*引用*，不放类型条目。** 所以一个只在 `[SlotSelectors]` 里被点名的枚举**解析不开**，`SetEnumSlotCollection` 会答「Type not found」，尽管属性自己把它列为允许。**选择器枚举要带 `[AgentContext]`**（demo 的 `DatasetSource` / `VoltageRange` 都带）。演示见 `SetEnumSlotCollectionTests.AnEnumTheTreeHoldsOnlyAsAReference_DoesNotResolve`。
- **非枚举 provider 要带 `[Archivable]`**，见上。

### 守卫：别再一条条靠模型发现

`ClosedWorldBoundaryTests` 把这两条**按声明整体扫**，而不是举一个例子：

- 扫四个程序集里**每个** `[SlotSelectors]`（**注意挂在成员上，不是类型上** —— 拿 `Type.GetCustomAttributes` 扫会一个都找不到，用例空过），非枚举的那类断言 `VeloxJsonRegistry.ReaderFor` 非空。
- 扫每个组件的命令参数类型，断言可读 / 是枚举 / 是标量 / 在一张**具名的接口允许表**里（`IWorkflowNodeViewModel`、`IWorkflowSlotViewModel`、`ITaskContext`、`IWorkflowActionPair` —— 它们各自有专用工具，不需要从 JSON 造）。

两条都带**自证守卫**（扫到 0 条就红），否则重命名一次属性就能让用例永远变绿。

### 工具返回的形状也要能让人停下来

`SetEnumSlotCollection` 的非枚举成功路径原来只回 `{ok, selectorType, property}` —— **不回它建了什么**。模型因此看不见自己那段 JSON 的结果，实测行为是**打转**：设一次、列一次、少两个端口再设一次、再列一次，跑了三五分钟。现在两条路径共用 `EnumeratorResult`，都带 `status: "ok"` / `count` / `slots`（id + label + value）。**改这个工具时别把 slots 拿掉。**

### 七·六、槽属性的三条判据链，以及它断在哪（2026-10-05）

一个槽属性要被工具面认出来，要连过三关：**生成器给出标志 → 目录带着标志 → 工具按标志解析**。这一轮断在第一关，而症状出现在第三关。

- **`AIContextFlags.IsSingleSlot` 由目录生成器算**，判据是「属性的类型实现了 `IWorkflowSlotViewModel`」。但 `[WorkflowBuilder.Slot<T>]` 声明的类型，那个接口是 **Workflow 生成器**在同一编译趟注入的 —— 生成器之间看不见彼此的产物，于是这些类型一律判 false。`EnumSelectorNodeViewModel.InputSlot` 就是这样。
- **断口的三个下游**（都是静默的）：`ListSlotProperties` 不列它；`BuildSlotPropertyMap` 不认它，`GetNodeDetail` 里那个槽因此没有 `prop` 字段；`ResolveSlotId` / `ConnectByProperty` 按属性名解析不到它 → 只能回落到 `ConnectSlotsById`。**还有一条**：`ComponentPatcher` 靠同一个标志拒绝对槽属性赋值，标志缺了它就放行 —— 那是唯一绕过 `CreateSlotCommand` 的写入口。
- **修在生成器**，且工具侧**同时**留了一道运行期兜底（`TreeProperty.HoldsASingleSlot(target)` = 标志 || 值当前是 `IWorkflowSlotViewModel`），因为生成器的改动进不了已经发布的包。**兜底实测不是死代码**：单独撤掉生成器那一行，七条用例仍绿；两处一起撤才红。
- `IsSlotCollection` 因此对消费方声明的 `SlotEnumerator<T>` 由 false 变 true（它确实是槽的集合）。这带出 `ResolveSlotId` 的一个既有毛病：它先查集合那条分支，而那条要求值是 `IList` —— 枚举器装的是 `ConditionalSlot`，不是 `IList`。**已补 `IsSlotEnumerator` 分支**。`AddSlotToCollection` / `RemoveSlotFromCollection` 也把枚举器单独分出来，否则它们会说「集合为空」。

守卫：`SlotPropertyResolutionTests`（七条）。生成器那张「谁认 `[WorkflowBuilder.*]`」的表见 [`VeloxDev.Core.Generator/architecture.md`](../VeloxDev.Core.Generator/architecture.md) §六·9。

### 七·七、工具面上的**两个坐标系**（2026-10-05）

`SetAnchorCommand` / `SetSizeCommand` 存的是**世界坐标**（字段直存，存档里也是世界值）；而 `node.Anchor` / `node.Size` 的 **getter 为了渲染按画布缩放折叠过**（`Anchor.Collapse`，即除以缩放）。查询工具原来读的正是这个 getter，于是：

- 写进去 (3120, 940)，读回来 (2836.36, 854.55)，**比例随用户缩放变**；
- 位置无法往返核对，两次不同缩放下的读数也无法互比。

`ListNodes` / `GetNodeDetail` 已改成乘回缩放报世界坐标（`WorldAnchor` / `WorldSize`），说明里也写明用的是哪个空间。**改动只在读侧** —— 渲染确实需要折叠值，错的是把渲染用的 getter 当成对外的位置读数。守卫在 `NodeGeometryToolTests`（非单位缩放的读数与往返各一条）。

### 七·八、目录的「谁算组件」判据也漏了同一个兜底（2026-10-05）

`AIContextModel.ComponentKindOf` 原来只查四个 `IWorkflow*ViewModel` 接口 —— 与 §七·六 同一个病根（接口是
另一个生成器注入的，看不见）。而 `BuildType` 的收录规则是「是组件 / 或被标注过 / 或成员带标注」，所以
**一个只写了 `[WorkflowBuilder.Node<T>]` 的节点类型整条不进目录**：`AgentTypeResolver.ResolveType` 解不开，
`CreateNode` / `GetTypeSchema` / `ListCreatableTypes` 对用户自己写的节点类型一律答「不在目录里」。

**demo 看不见这个缺口**，因为 demo 的每个节点都另外带了 `[AgentContext]`；它只在消费方第一次写下不带标注的
节点时出现。归档那一侧早就不这样：`VeloxJsonModel.RootReason` 第二条就是 `[WorkflowBuilder.*]`。

⚠ **这是对消费方目录的加宽**：他们声明的每个组件开始进目录（此前只有带标注的进）。方向是对的 —— 否则 Agent
建不了用户自己的节点 —— 但 prompt 体积会涨。回退点：`ComponentKindOf` 里那一行。

守卫：`DiscoveryCoverageTests`。同一条用例还钉住了**「枚举器不带 `[SlotSelectors]` = 不设限」**：白名单才是
限制，没有它就是放开；枚举/布尔一路、自定义 provider 一路，各验一次（`IsEnumTypeAllowed` 对两路的代码不同）。

### 七·九、目录没有、也不需要归档那三条「加宽」

归档的闭世界沿三条线扩：**派生类向下展开**（`BuildFamilyIndex`）、**字典的键**、**开放泛型根上的类型参数约束**。
AI 目录不需要其中任何一条，值得写下来免得下次照着搬：

- **向下展开**解决的是「成员声明成基类、装着派生类时 `$type` 要解得开」—— 那是**写读文档**的问题。目录是
  **给人/模型看的清单**，按类型各有一条，本来就没有「按声明类型去找运行期类型」这一步。派生类型要么自己带标注，
  要么经 §七·八 那条新判据进来。
- **字典的键**在目录里没有对应物：目录不描述容器的键类型。
- **开放泛型约束**同样没有对应物：`BuildType` 对开放泛型直接返回 null（生成的 cast 里 `T` 没有绑定）。

### 七·十、第二趟抬进了类，诊断也跟着收窄了（2026-10-05）

第二趟（「可达的成员类型」）原来只抬 **枚举与结构体**，于是最普通的载荷类型 —— 一个普通**类** —— 反而漏了：
Agent 在一处列表里读到 `List<X>`，去问 X 是「不在目录里」。现在 `Enum | Struct | Class`。只走一遍、
只认直接声明或泛型实参，所以是一跳、有界；`ListCreatableTypes` 只看 `Components/Nodes|Slots`，新进来的
`Data` 条目不会被当成可创建类型报给模型。

**放宽之后 `VeloxDev.Core.Extension` 自己多出 2 条 `VELOX_AI_TREE001`**（`AgentPipeline.Use`、
`WorkflowAgentScope.WithSkills`）—— 被成员类型捎带进来的宿主 API，此前不在目录里。于是 `DetectNotices`
多收一个 `declared` 集合（第一趟填的），只对**作者自己声明进目录**的类型报。理由：诊断是说给作者的行动建议
（「你的这个声明会被目录丢掉一部分」），一个只因为出现在成员的类型名里才进来的类型没有这样的行动，
而诊断落在它的源码行上，看上去像在说那个人写错了。此前枚举/结构体那一趟没产生过任何诊断，所以这条收窄
不改变任何既有输出。

> 写这个过滤时踩了一次真空引用：`if (type.Symbol is not null && !declared.Contains(type.Symbol)) continue;`
> —— `Symbol` 为 null 时不 continue，下一句就用它取 `Locations`。编译器 CS8602 报了；**nullable 警告在这条
> 路上是抓真 bug 的**，不要顺手 `!` 掉。

守则：`DiscoveryCoverageTests.AClassExposedByAnAnnotatedMember_IsResolvable`。

### 七·十一、**详细归说明，返回值保持简单**（2026-10-05，用户口述）

> 返回值要简单，工具描述可以详细。

理由在成本结构里：**返回值每次调用都进模型的上下文**（而且是累积的），说明是**静态的**、每条请求发一次。
所以「模型需要知道的东西」应该尽量搬到说明里，返回值只放这一次调用真正产出的东西。

本模块原来就有一部分这条原则 —— `WorkflowAgentToolkit` 的类注释写着 "All JSON output uses
`VeloxJsonFormat.Compact` to minimize token consumption"。这次是把它扩展到**要不要靠返回值做提示**上：

- 拒绝写「选择器驱动的节点建出来没有端口」，**不是**在 `CreateNode` 的返回值里加一句提醒，而是在它的
  `[Description]` 里写。
- 同理 `ConnectEnumSlot` 的两种接收口形态、`receiverCondition` 收什么值，都写进说明与参数说明，
  返回值不动。

⚠ **说明是声明，声明要有测试。** 照它写完之后必须能验证它说的是真的 —— 这一次是
`PortConnectionRouteTests` 三条（能连、拒绝语点名了那个参数、`count:0` 确实是 0）。一段没人验的说明，
只是把「模型不知道」换成了「模型被误导」，比原来更糟。

（这条目前记在模块记忆里。如果它要成为全仓的规矩 —— 例如将来有别的工具面 —— 应当升到
`memory/specifications/` 并在 `AGENTS.md` 的索引表里占一行。）
