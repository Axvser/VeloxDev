# VeloxDev.Core.Extension — MCP 子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/MCP/`（10 个文件，2026-10-08 起含 `HostedMcpServer.cs` 与 `McpToolProxy.cs`）。
> 宿主样例：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`（预注册服务器在 `:95-124`，注册点是 `:57`）。

---

## 一、最承重的一条：安全边界是**代码挡的**，不是提示词挡的

`McpSelfServiceLevel`（`McpSelfServiceLevel.cs:15`）四级：`Closed = 0` / `RemoteConfirmed = 1` / `AllConfirmed = 2` / `Unrestricted = 3`。默认 `Closed`（`McpScope.cs:69`）。

**会改动机器上软件的写工具按档位逐步出现，不到那一档就根本不注册**（`McpAgentToolkit.CreateTools`）：`AddMcpServer` 从 `RemoteConfirmed` 起（那一档只放 Http），`SetMcpServerArguments` 从 **`AllConfirmed`** 起 —— 比添加再高一档，理由独立：远程服务器**没有**启动参数，而本地服务器在 `AllConfirmed` 以下**不可重配**，所以在 `RemoteConfirmed` 注册它等于「存在但每次调用都被拒」，正是这个类刻意不做的事。这不是「注册了但会拒绝」——理由写在紧邻的注释里：一个模型看得见却永远用不了的工具只浪费提示预算，还招来重试。**`ToolNames` 里没有它们**（那是「每个非视图 scope 都注册的四个」），宿主要认得它们得自己去认那两个常量。

提示文案也是**按级别生成**的（`McpAgentToolkit.cs:110`），理由同样写在注释里：说「服务器不可添加」在 `Closed` 以上是撒谎，说「可以添加」在 `Closed` 是撒谎。

**两级门是独立的，都要过：**

1. `McpScope.CanAddServer(runMode)`（`:109`）—— 级别 + 运行模式的组合判定。`RemoteConfirmed` 只放 `Http`，`AllConfirmed` 放本地模式，`Unrestricted` 不问。`AddServer` 里第一件事就是查它（`McpAgentToolkit.cs:160`）。
2. 确认：在 `Unrestricted` 以下的级别，添加要用户同意（`RequiresConfirmationToAdd()`，`:121`；`ConfirmationResolver`，`:102`）。

**级别与确认处理器是两回事。** `WithSelfService` 只开级别；`WithConfirmationHandler`（`:93`）注册裁决者。**没注册处理器时，需要确认的级别会「拒绝」，而不是「放行」** —— 这条在 `skills/veloxdev-drive-workflow-with-ai/SKILL.md` 与同目录 `references/mcp.md` 里都写死了（那两份是**本仓给驱动 agent 看的**文档，不是 demo 模型读到的提示词 —— 模型读到的 MCP 措辞只有 `BuildPromptContext`）。

**接进 `WorkflowAgentScope` 时，你在 `McpScope` 上设的确认处理器会被顶掉。** `WithMcps`（`WorkflowAgentScope.cs:1552`）无条件执行 `mcp.WithConfirmationHandler(ResolveConfirmationAsync)`，注释明说「A handler set directly on the MCP scope is replaced by this」。这是刻意的：审批**只配一次**，工作流工具与 MCP 自服务共用同一个。所以宿主只需要在 **scope** 上调 `WithConfirmationHandler`（`WorkflowAgentScope.cs:753`）。

---

## 二、装载路径：服务器**被托管**在 Scope 里

```
LoadAsync(servers, ct)        McpScope.cs:749   ← 破坏性：先释放已装载的一切
AddAsync(config, ct)          McpScope.cs:786   ← 中途加一个，不清空
  └─ LoadOneAsync(config, root, ct)   :845
       ├─ TrackServerAsync(config)           在编组块内建/复用一个状态行
       ├─ 已有同名 HostedMcpServer ⇒ SetConfiguration + ReloadAsync（见下）
       ├─ 否则：本地模式(Npm/Pip) Installing → 装运行时 → Connecting
       ├─ new HostedMcpServer(...) → StartAsync → ConnectServerAsync   :1162
       └─ 成功 → _hosts[name] + _loadedToolSets[name] = 它的工具代理，Version++
          失败 → 该服务器状态置 Error、发 ServerError 事件、**Version 仍然 ++**、返回空工具数组
```

**每个连接过的服务器由一个 `HostedMcpServer`（`HostedMcpServer.cs`）托管** —— 它持有 client、当前提供的
工具、以及**每个服务器一把 `SemaphoreSlim(1,1)`**。`McpScope._hosts` 存的是这个托管对象，**不是 client**：
client 由它独占，因为「工具引用着 client、client 在 stdio 下持有子进程」这条不变量（`:151-158`）
只有单一所有者才守得住。`_loadedClients` / `_loadedConfigs` 两张表已经删掉，就是被它取代的。

**失败也自增 `Version`** —— 这是个容易漏的点：失败的服务器不贡献工具，但它在状态列表里**多了一行 Error**，
按 `Version` 缓存的渲染必须失效。

### 二·一、工具的**身份是稳定的**：`McpToolProxy`

Agent 拿到的从来不是 `McpClientTool`，而是 scope 自己的 `McpToolProxy`（`McpToolProxy.cs`）：一个
`AIFunction`，名字固定，`Description` / `JsonSchema` 每次都向「当前连接」取。`Reconcile()`（`HostedMcpServer.cs:305`）
保证**同名工具跨重建是同一个实例**。

于是**改一个服务器的启动参数对模型完全透明**：工具列表不变、`Version` 不跳、模型手里那份引用不作废，
换掉的只是代理后面的 client。只有当**工具名集合**变了，`RefreshOfferedTools`（`McpScope.cs:968`）才
`Version++`，provider 随之重渲染。

**代理挂在 `TrackedAIFunction` 里面**，所以 `McpAgentContextProvider.BuildTools` 一行都不用改：
它只问 `LoadedTools` 里是不是 `AIFunction` 再包一层。顺序是 `TrackedAIFunction(McpToolProxy)`。

### 二·二、改参数：惰性 + 先建后换（2026-10-08）

`WithServers`（`McpScope.cs:333`）同名覆盖注册时，**只**把新配置交给已有的托管对象
（`SetConfiguration`，只标脏）；`AddMcpServer` 传同一个名字走的是同一条路。真正的重建发生在
**下一次调用**里（`HostedMcpServer.InvokeAsync`，`:168`），在锁内：

1. **先连新配置**。连上了才释放旧 client —— 所以**一份写错的参数不会把正在工作的服务器打掉**，
   旧连接继续服务，失败记在状态行上并发 `ServerError`。
2. 一份配置**只试一次**（`_attemptedFor`）：坏参数不会让之后每次调用都去重连一次。
3. 旧 client 不是立刻销毁而是**退下来**（`_retired`），等最后一个在途调用退出才释放 —— 否则那次调用会被
   从一个正在被销毁的 client 上打下来。

**锁只在「有待重建」时才拿**：无需重建的调用一次锁都不碰，所以同一服务器上的并发调用照常并行；
而撞上同一次待重建的调用会排队，于是**只连一次、只销毁一次**。

**本地装载会真的在用户机器上装东西**：`EnsureNpmPackageAsync` / `EnsurePipPackageAsync`（`:973`、`:1009`），用 `CliWrap` 起子进程。**进程级一把静态锁** `s_installLock`（`SemaphoreSlim(1,1)`，`:53`）+ 进程级已装列表 `s_installed`（`:54`），且都是**先查表、再取锁、再查一次**的双检（`:977-982`）。所以给两个 `McpScope` 实例各装同一个包，只会装一次。

**运行目录**：默认 `.evn/mcp`（相对 `AppContext.BaseDirectory`，字段 `McpRootRelative`，`:49`），按运行模式分子目录 —— `GetRuntimeDir`（`:949`）：Npm/Npx → `node`，Pip/Uvx → `py`，Dotnet → `dotnet`，Exe → `exe`。

**卸载**：`UnloadServerAsync(name)`（`:554`）把状态重置为 `NotStarted` 并把 `ToolCount` 清零；它可以从「没装载过」的服务器上安全调用。`UnloadServer(name)`（`:545`）是阻塞包装，注释明说异步代码该用 `*Async`。

---

## 三、`Options` 是白名单校验的，不是自由 blob

`McpServerConfiguration.Options`（`McpServerConfiguration.cs:73`）是个字典，序列化后**按运行模式**过白名单（`EnsureKnownKeys`，`McpScope.cs:1238`）：

| 模式 | 允许的键 | 位置 |
|---|---|---|
| Http | `headers` / `oauth` / `connectionTimeout` / `transportMode` / `ownsSession` | `McpScope.cs:1212` |
| Stdio（Npm/Npx/Pip/Uvx/Dotnet/Exe） | `env` / `workingDirectory` | `McpScope.cs:1213` |

**写了别的键 ⇒ 抛异常**（`:1238`），错误信息会把允许列表列出来。所以要加一个新的 option 键，必须同时改 `HttpOptionKeys` / `StdioOptionKeys` **以及** 消费它的 `BuildHttpTransportOptions`（`:1161`）/ `BuildStdioTransportOptions`（`:1118`）。

`connectionTimeout` 有两级来源：`Options.connectionTimeout`（秒数或 TimeSpan 字符串）**优先于**作用域级的 `WithConnectionTimeout`（`ParseTimeSpan`，`:1248`/`:1287`）。宿主样例两个值：Microsoft Learn 30 秒、一个故意不可达的示例服务器 8 秒（`AgentHelper.cs:103`、`:112`）。

---

## 四、`CreateContextProvider` 与工具

`McpScope.CreateContextProvider(ToolPipeline? tools, AgentPipeline? pipeline)`（`:533`）。

- `McpAgentToolkit.CreateTools()`（`McpAgentToolkit.cs:68`）返回**未包装**的四个管理工具（`ToolNames`，`:41`）；`CreateTools(ToolPipeline, AgentPipeline?)`（`:93`）返回包装过的 —— **provider 用的是后者**（`McpAgentContextProvider.cs:108`），所以 MCP 工具与内置工具受同一套预算闸门管。
- `CreateTools()` 里靠 `ToolNames` **下标**取的只剩 `LoadServers`（`ToolNames[1]`，`:74`）与 `UnloadServer`（`ToolNames[2]`，`:75`）—— `ListServers` / `DescribeServer` 各有常量 `ListName`（`:44`）/ `DescribeName`（`:47`）。这处不对称是授权视图逼出来的：它只注册那两个读工具，按下标取会在一个子集上错位。所以「数组位置即契约」如今只覆盖头两个位置，改它们的顺序仍会静默改变工具名与实现的对应关系；末尾那两个名字退化成「宿主需要认得」而已（`ToolNames` 自己的注释 `:34-38` 就是这么写的）。
- `McpAgentContextProvider` 只按 **`scope.Version`** 缓存（**没有语言维度**）。`Version` 在装载 / 添加 / 卸载 / 新增预注册 / 服务器失败时自增（`:346`、`:580`、`:874`、`:887`）。
- `BuildInventoryBlock()`（`:395`）读 **`Status.Snapshot`** 而不是绑定的 `ObservableCollection` —— 注释写明理由：提示是在 **agent 调用线程**上渲染的，不是 UI 线程。
- **这个 provider 只贡献 MCP 这一片，不含工作流内置工具**（`McpAgentContextProvider.cs:101-104`：「内置工具是工作流层的，两者都要的宿主就把两个 provider 都挂上」）。所以 `CreateContextProviders()` 返回的那个数组是「每一片各出一个 provider」的组合，不是「谁都带全套」。
- `LoadedTools` 交给它的**是 `McpToolProxy`**（不是 `McpClientTool`），`AIFunction` 所以**与内置工具同样被 `TrackedAIFunction` 包**（`:110-113`）；不是 `AIFunction` 的工具**原样放行**而不是被丢掉 —— 也就是说这类工具**不受预算与编组约束**，这是唯一一处「包装会漏」的地方。**`SeedLoadedTools` 塞进去的是裸 `AIFunction`**，没有托管对象、也就没有重建。
- **参数是能读能写的**（2026-10-08 起）：`ListMcpServers` 每个服务器多返回 `description` / `package` / `version` / `arguments` / `endpoint`；
  `SetMcpServerArguments`（`McpAgentToolkit.cs:251`）只换 `Arguments`，**保留 runMode / package / Options**，
  并立刻生效（`McpScope.ReconfigureAsync`，`:372` —— `WithServers` 只标脏，它额外调一次 `ReloadAsync`）。
  所以模型改一个目录不必重述 package，写错也改不动别的字段。
  ⚠ **`Options` 永远不回给模型**：它装着 `headers.Authorization`、`oauth.clientSecret`、`env`。
  实测一行 `VeloxJsonValue.From(config.Options)` 就能把 `Bearer …` 与 client secret 原样序列化进提示词 ——
  守卫是 `McpParameterSwitchTests.ListMcpServers_NeverCarriesTheHostsOptionBag`（按**原始 JSON 串**断言，
  所以对将来新加的字段也成立）。
- **MCP 管理工具的只读分类是缺的**：`ListMcpServers` / `LoadMcpServers` / `UnloadMcpServer` / `DescribeMcpServer` 都不在 `WorkflowAgentToolkit.BuildQueryToolNames` 的字面量集合里（`WorkflowAgentToolkit.cs:374-384`），而 `McpAgentToolkit.ToolNames` **没有**像 `SkillAgentToolkit.ToolNames`（`:386`）与 `SubAgentAgentToolkit.ToolNames`（`:389`）那样被自动并入。后果：它们被算作**写调用**，计入 `MaxWriteToolCalls`，并且在 `AutoMarkDirty = true` 时**会把工作流标脏**（`WorkflowAgentToolkit.cs:412`）。demo 之所以看不出问题，是因为它设了 `WithAutoMarkDirty(false)`。

**`McpScope.InstanceId`**（`:311`）是每 scope 一个 `Guid`，给 provider 做 state key。注释里有一条刻意设计：同一个 scope 造两个 provider 会**在 agent 构造时响亮地失败**，这是想要的；如果改成按 provider 生成 id，两个 provider 就会都通过，然后在同一轮里**把每个 MCP 工具和 inventory 块各塞两遍**。

---

## 五、状态驱动：`UpdateStatus`（发起即忘）vs `RunOnUIAsync`（等待）

`McpStatusViewModel.Servers` 是绑到宿主 UI 的 `ObservableCollection`，所以**读它也必须在编组块内**（`TrackServerAsync`，`:905` 的注释写明了这点）。

由此分出两条路：

| 方法 | 行为 | 用在哪 |
|---|---|---|
| `UpdateStatus(Action)`（`:613`） | **发起即忘**。没有 UI 上下文、或已经在 UI 上下文上时短路直接执行 | 状态刷新 —— 不需要等 |
| `RunOnUIAsync(Action)`（`:633`） | **await** | 装载/卸载各步 —— 注释说：「完成某一步装载的人要能看见这一步造成的状态」 |

---

## 六、扩展点

**加一个 MCP 管理工具**

1. 在 `McpAgentToolkit` 加一个 `private async Task<string> Xxx(...)`，用 `[Description]` 写清用途与前置条件。
2. 在 `CreateTools()`（`:78`）里 `AIFunctionFactory.Create(Xxx, "工具名")` 注册。**同时决定它归哪一类**：
   「每个非视图 scope 都注册」的进 `ToolNames`；**只在高档位存在**的（像 `AddMcpServer` / `SetMcpServerArguments`）
   另起一个 `const` 并放进 `:92-96` 那个 `if` 里 —— 顺手塞进 `ToolNames` 会让
   `McpAgentContextProviderTests.BuildContext_ContributesTheManagementToolsAndTheirDescription` 当场红（它会红是对的：
   那条断言就是 `ToolNames` 的定义）。
3. **如果它该算只读**：把名字加进 `WorkflowAgentToolkit.BuildQueryToolNames`（`WorkflowAgentToolkit.cs:372`）—— 注意这里**没有** `McpAgentToolkit.ToolNames` 的自动并入（技能工具在 `:386`、子代理工具在 `:389` 才是自动的）。目前四个 MCP 管理工具**都不在**那个只读集合里。
4. 若它的行为随自服务级别变化，同步改 `BuildPromptContext()`（`:123`）的 switch —— 否则提示与实际能力脱节。

**加一个运行模式**

1. `McpServerRunMode.cs` 加枚举值。
2. `McpScope.GetRuntimeDir`（`:949`）加分支。
3. `McpScope` 里装载分支：需要装运行时的模式要在 `LoadOneAsync`（`:845`）的 `Installing` 段里接上；需要自己的 transport 要接 `ConnectServerAsync`（`:1055`）。
4. `McpServerConfiguration` 的必填字段校验：`AddServer`（`McpAgentToolkit.cs:168-171`）与 `EnsureKnownKeys` 的白名单。
5. `McpAgentToolkit.BuildPromptContext` 的措辞、以及 `AddServer` 的 `[Description]`（`:148`，里面逐条列了模式名）。

**静态 `[Description]` 与动态档位**

档位可以在运行期任意时刻改（`WithSelfService` 会 `Interlocked.Increment(ref _version)`，`BuildPromptContext()` 与 `CreateTools()` 都在调用时读它），所以**提示词与工具集永远同步**。但 `[Description]` 是**静态字符串**，跟不上 —— 凡是「哪一档允许什么」这类事实，说明里只能**让位给提示词**（「which run modes the CURRENT level allows is stated in your prompt」），不要在说明里枚举被允许的模式：写死了就会在某几档上撒谎。`AddServer` 原来说明里把六种模式平铺直叙，就是这样一处。

**给重建/动态切换加测试**

用 `McpScope.ConnectOverride`（`McpScope.cs:911`，`internal`）—— 它替换掉「连接」这一步，测试给一个假连接器
就能驱动整条重建路径，不用起进程、不用连服务器。**这是唯一的入口**：`SeedLoadedTools` 只能伪造「工具已经存在」，
伪造不了重连，所以它测不到重建。假连接器要做两件事：把「用哪份参数连的」盖进它返回的工具结果里
（于是「这次调用走的是新连接」是事实而不是推断），以及实现 `IAsyncDisposable` 好让用例断言旧连接被释放了几次。
`McpParameterSwitchTests.cs` 就是这么写的。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在宿主里把 `McpAgentToolkit.CreateTools()` 的结果手动塞进 agent 的工具列表 | 拿到的是**未包装**的一套（`McpAgentToolkit.cs:89-91` 明说「registering the unwrapped set by hand gets none of it」）：没有编组、没有预算、没有回调；并且与 provider 贡献的那份**重名**，框架的并集**不去重**，模型会收到两遍 |
| 在 `McpScope` 之外自己 `new McpClient` 连服务器 | 不会进 `_loadedToolSets`，于是 `LoadedTools`、`BuildInventoryBlock()`、状态面板、卸载路径全都看不见它 |
| 把「拒绝」实现成返回一句友好文本而不返回 `{"status":"error"}` 信封 | `TrackedAIFunction` 的 `Error()` 信封（`TrackedAIFunction.cs:141`）是模型识别失败的唯一约定；纯文本会被当成成功结果 |
| 在 `UpdateStatus` 里 await 别的东西 | 它设计成发起即忘且可短路；在 UI 上下文之外调用时行为不同 |
| 让两个 provider 共用一个 scope 却不给唯一的 `InstanceId` | 框架在 state key 冲突时抛（`McpScope.cs:306-308`），这正是想要的失败 |
| 认为 `WithSelfService` 开了级别就够了 | 还需 `WithConfirmationHandler`；否则 `RemoteConfirmed`/`AllConfirmed` 下**一律拒绝** |

---

## 七、死面 / 仓库内零真实使用者

- **`WithSelfService` 没有任何非测试调用者。** 调用点全部在 `Src/Core/VeloxDev.Core.Extension.Test/Agent/MCP/McpSelfServiceTests.cs` 与 `McpAgentContextProviderTests.cs:130`。七家 demo 都停在 `Closed`，`AgentHelper.cs:281-283` 只是注释里提到「升档会加 AddMcpServer」。所以 **`AddMcpServer` 的完整路径（含确认、含四个级别的 prompt 分支）只有测试在跑**。
- **改参数的两条路都没有真实宿主使用者**：demo 通过 `WithServers` 一次性预注册（`AgentHelper.cs:94-124`），之后从不改；`WithServers` 的「同名 = 重配」也是为这条新路径加的。所以**动态切换目前只有 `McpParameterSwitchTests`（9 条）在跑** —— 它是这条路径唯一的守卫。
- `McpServerRunMode.Pip` / `Uvx` / `Dotnet` / `Exe` 在 demo 里都没有实例 —— demo 只配了 `Http` 与 `Npx`（`AgentHelper.cs:95-124`）。
- `McpScope.WithMcpRoot`（`:59`）在仓库内无调用者；`.evn/mcp` 是唯一被用到的根。
