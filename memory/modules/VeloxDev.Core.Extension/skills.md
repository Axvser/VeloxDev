# VeloxDev.Core.Extension — Skills 子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/Skills/`（11 个文件）。
> 内置技能语料：`Resources/Workflow/{en,zh}/Skills/*.md`（各 7 个：CompilerUsage / ConnectionValidation / DiscoveryFlow / NodeCreation / OperationOrdering / SlotEnumerator / SmartLayout）。

---

## 一、最承重的一条：提示形状由**来源种类**决定，不是由配置决定

| 来源 | `Kind` | 怎么进提示 |
|---|---|---|
| 嵌入资源 | `SkillSourceKind.Embedded`（`EmbeddedSkillSource.cs:47`） | **正文全文注入**，按名字序拼接（`SkillScope.cs:238` `BuildEmbeddedBlock`） |
| 文件系统 | `SkillSourceKind.File`（`FileSkillSource.cs:46`） | **只做广告**：名字 + 描述，包在 `<available_skills>` 的 Agent Skills 约定块里（`SkillScope.cs:299` `BuildAdvertisement`），正文要模型自己用 `load_skill` 取 |

两者**可以同时存在**，`SkillAgentContextProvider.BuildInstructions`（`SkillAgentContextProvider.cs:106-120`）把两个块依次拼起来；**两个都空时返回 `null`**，贡献一个「空标题」不如什么都不贡献（`:116-117`）。

`BuildEmbeddedBlock` 的注释（`SkillScope.cs:233-237`）给了一个可以直接拿来验的等价关系：**全部技能都启用时，它逐字节等于技能可开关之前提示里带的内容**。所以「技能开关坏了」的判据是这句话，不是回忆。

### 语料有三个可能的主人，但一时刻只有一个（本轮新增）

嵌入语料有第二条出口：`WorkflowAgentScope.ProvideProgressiveContextPrompt()` 在 **`Skills == null`** 时也会把整份语料拼进静态骨架。于是「骨架里一份 + provider 里一份」会让每篇技能文档进两次。规则是**后接的一方让位**：

- `WorkflowAgentScope` 用 `_embeddedSkillsFrozenIntoPrompt` 记住「有骨架已经带着语料出门了」（sticky —— 字符串已经交到宿主手里，撤不回）。
- `WithSkills(...)` 之后建的 provider 拿到这个事实（`SkillScope.CreateContextProvider(..., embeddedCorpusDelivered: true)`），于是**不再贡献 `BuildEmbeddedBlock`**，改为贡献 `SkillScope.BuildWithdrawnBlock`（`:267`）：**当前处于停用状态的嵌入技能名单**。
- 为什么不是干脆闭嘴：骨架冻住的是「当时的启用状态」，之后 `SetEnabled(name, false)` 若不发声就成了静默空操作。报一份「上面那几篇请忽略」既不说两遍正文，也不丢掉这次关闭。

**两种宿主顺序都能跑**，但语义不同：先 `WithSkills` 再取骨架 ⇒ 语料归子系统（默认推荐，`AgentHelper.cs` 就是这个顺序）；先取骨架再 `WithSkills` ⇒ 语料冻在骨架里，子系统此后只报差量。文件技能永远不进 `BuildWithdrawnBlock`（它们本就只做广告，不在提示里）。测试：`ComposedProvidersTests.cs` 的 `SkeletonBuiltBeforeSkills_*` / `SkeletonBuiltAfterSkills_*` 五条，以及 `SkillScopeTests.cs` 的 `WithdrawnBlock_*` 五条。

---

## 二、缓存与版本

- `SkillScope.Version`（`SkillScope.cs:61`）在**发现集合**或**任一启用标志**变化时自增。
- `PromptLanguage` 是**独立维度**：注释明说「语言不是别的地方可观察的」（`:75-78`），虽然 `WithPromptLanguage` 也自增 `Version`（`:83`），provider 仍然按 **`(Version, Language)` 元组**缓存。这是双保险，不是冗余。
- `SkillAgentContextProvider` 的 `_stateKeys` 用 **`scope.InstanceId`**（每个 scope 一个 `Guid`，`SkillScope.cs:73`）而不是像工作流那样用 `StateDiscriminator`。理由：一个 agent 上挂两个 scope 是正常布置，框架在**两个 provider 共用 state key** 时会抛。
- `Refresh()` **是宿主动作，不是每轮动作** —— `SkillScope.cs:153-158` 明说：它总是自增 `Version`，在 prompt provider 里每轮调它会直接废掉缓存。

**provider 每次渲染做的一件事容易被漏掉**：它把 `_toolkit.Language` 重新赋成 `scope.PromptLanguage`（`SkillAgentContextProvider.cs:89`）。技能正文按提示语言读，所以 toolkit 必须跟着 scope 走，而不是跟着它被构造时的语言。改技能工具、忘了这一行，工具返回的正文就会和提示语言不一致。

---

## 三、发现与归属

`SkillScope.Refresh()`（`:160`）的规则：

1. 某个 source 的 `Discover()` 抛异常 ⇒ **不能拖垮其它 source**。它变成一个名字叫 `{Kind}-source` 的**错误描述符**（`:177-183`）—— 它**没有 `Description`**，所以在 UI 上只会显示成一条错误行。
2. **同名归属**：第一个声明某名字的 source 拥有它；**同一个 source 的多个同名描述符全部保留**（`:187-195`）—— 那是同一个技能的**分语言变体**。这条容易写反：不保留就会丢掉 `zh` 那一份。
3. `Apply(found, owners)` 重建状态，同时**按名字保留启用标志**。

`SetEnabled`（`:207-225`）三个非显然细节：

- **查找发生在编组块内部**（`:215-222`），因为 `Find` 走的是绑定集合 —— 宿主从后台线程切开关时不能在那里读它。
- 名字不存在 ⇒ 返回 `false`，**并且不会凭空加一条**。
- 值没变 ⇒ 返回 `true` 但**不自增 `Version`**。
- 对比 `Name` 的比较是 `OrdinalIgnoreCase`（`_owningSource`/`owners` 都是，`:34`/`:166`）。

---

## 四、两个内置 source

**`EmbeddedSkillSource`**（`EmbeddedSkillSource.cs`）

- 名字与描述**以 frontmatter 为准**；缺失时才从文件名派生 kebab-case 名字、从第一个标题派生描述（`DeriveDescription`，`:161`，会剥掉 🧭🛡️💡⚠ 与 `"Skill:"` 前缀）。
- **`ReadResource` 恒返回 `null`**（`:130`）—— 嵌入技能**没有** bundled resource。这是刻意的：资源是文件布局才有的概念。
- 文件路径由 `Agent/AgentEmbeddedResources.cs` 拼（`EmbeddedSkillSource.cs:103` 里的字符串 `Resources/{system}/{lang}/Skills/{name}.md` 只是**展示给宿主看的 Path**，真正的读走 `AgentEmbeddedResources`）。

**`FileSkillSource`**（`FileSkillSource.cs`）

- 只认两种布局：`<root>/SKILL.md`（单技能根）与 `<root>/<name>/SKILL.md`（`:25` `SkillFileName`）。**不做递归** —— 只有一层。
- 可读资源的扩展名白名单（`ResourceExtensions`）：`.md .json .yaml .yml .csv .xml .txt`。
- 文件夹名与 frontmatter 名不一致 ⇒ **报错**，但**单技能根布局下不报**（根目录没有名字可比）。
- `ReadResource` 有**路径穿越围堵**：解析后的路径必须仍在技能目录内。

**`SkillFrontmatter`**（`internal`）—— 手写的**行导向**读取器，**不引 YAML 依赖**（理由：netstandard2.0 目标）。限额：`MaxNameLength = 64`、`MaxDescriptionLength = 1024`（`:23-24`）；kebab-case 正则 `^[a-z0-9]([a-z0-9]*-[a-z0-9])*[a-z0-9]*$`；去 BOM。**这些限额与正则和 Agent Framework 自带的文件技能源保持一致** —— 改之前先确认对方是不是也改了。

---

## 五、技能工具

`SkillAgentToolkit.ToolNames`（`SkillAgentToolkit.cs:49`）= `["ListSkills","load_skill","UnloadSkill","read_skill_resource"]`。

- 工具名故意用 Agent Skills 约定的 `load_skill` / `read_skill_resource`（下划线而非 Pascal），其余是 Pascal。
- **这组名字必须被工作流工具包读到**：`WorkflowAgentToolkit.BuildQueryToolNames`（`WorkflowAgentToolkit.cs:346`）把它们整体并进「只读」集合，于是技能工具**无论从哪来**（宿主注册的 or provider 贡献的）都**计入读预算、且永不标脏**（`WorkflowAgentToolkit.cs:323-330`）。**所以在这里加一个新工具名，只读分类会自动跟上 —— 但只有名字进了 `ToolNames` 才行。**
- `CreateTools()`（`:53`）不包装，`CreateTools(ToolPipeline, AgentPipeline?)`（`:69`）包装。provider 用的是**后者**（`SkillAgentContextProvider.cs:92`），所以从 provider 走出来的技能工具同样受预算闸门管。

---

## 六、扩展点

**加一个内置技能**（最简单的一条路）

1. 在 `Resources/Workflow/en/Skills/` 加 `<Name>.md`；**同一内容也要加到 `zh/Skills/`**（两语言目录是并列的，缺一边那份在那边就回退到 en，见 `AgentEmbeddedResources.cs` 的 fallback）。
2. 顶部写 YAML frontmatter（至少 `name`、`description`）。**不写也能用**（名字从文件名 kebab-case 派生），但描述会变成从标题猜的，质量差很多。
3. 不需要改任何 C#。`EmbeddedSkillSource.Discover()` 扫目录。
4. 想验证：`WorkflowAgentScope.WithSkills(...)` 之后，`SkillScope.BuildEmbeddedBlock(AgentLanguages.English)` 里应出现该名字。

**加一个外部 source**

1. 实现 `ISkillSource`（`ISkillSource.cs:17`）：`Kind` / `Discover()` / `ReadBody()` / `ReadResource()`。
2. `new SkillScope().WithSource(src)` 或 `WorkflowAgentScope.WithSkills(SkillScope)`（`WorkflowAgentScope.cs:1460`）。
3. **`Discover()` 必须无副作用、且绝不能返回技能正文** —— 正文只在 `ReadBody` 里给（这样广告模式才可能只发名字）。
4. 名字冲突时你会**输给先注册的 source**；分语言变体靠**同一个 source 返回多个同名 `SkillDescriptor`**（各带 `Language`，`SkillDescriptor.cs:31`）。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 把文件技能写成「直接返回全文的 source」 | 会让文件技能也全文注入，绕过了 ad 模式与 `load_skill`；`BuildAdvertisement` 只读 `SkillDescriptor.Name/Description`（`SkillScope.cs:302-308`），你的正文永远到不了提示 |
| 在 prompt provider 里调 `Refresh()` | 每轮自增 `Version`，缓存彻底失效（`SkillScope.cs:153-158`） |
| 在 `SetEnabled` 之外直接改 `Status.Skills[i].IsEnabled` | `Version` 不自增 ⇒ provider 不重渲染。注意 `SkillMemberViewModel`（Dashboard）就是**故意**不直写这个属性的，见 `dashboard.md` |
| 用 `ConfigureAwait(false)` / 线程池线程切开关 | `Find` 读的是 `ObservableCollection`；`SetEnabled` 的查找被刻意放在编组块里（`SkillScope.cs:215-222`） |
| 让 provider **无条件**贡献 `BuildEmbeddedBlock` | 骨架已经带过语料时（宿主先取骨架后 `WithSkills`）每篇技能文档进两遍。要按 `embeddedCorpusDelivered` 分叉到 `BuildWithdrawnBlock` —— 见 §一末 |
| 改 `SkillAgentToolkit.ToolNames` 之外另起一套技能工具名 | 那些名字不会被 `BuildQueryToolNames` 收进去 ⇒ 计成写操作、还可能触发标脏 |
| 只加 `en/Skills/X.md` | 中文会话下该技能回退到英文正文（`ReadWithFallback`），不是缺失，但语言不一致 |

---

## 七、死面

本目录**没有零调用者的成员**。唯一需要留意的是 `Agent/Skills/` 与 `Agent/Workflow/Functions/` 共享的一个约定：`WorkflowAgentToolkit.BuildQueryToolNames` 是**唯一**把 `SkillAgentToolkit.ToolNames` 接进来的地方（`WorkflowAgentToolkit.cs:346`），它排在字面量集合之后 —— 重名时字面量赢，但两边目前无重名。
