# VeloxDev.Core.Extension — MCP 子系统

> 代码：`Src/Core/VeloxDev.Core.Extension/Agent/MCP/`（8 个文件）。
> 宿主样例：`Examples/Workflow/Common/Lib/ViewModels/Workflow/Helper/AgentHelper.cs`（预注册服务器在 `:50-80`）。

---

## 一、最承重的一条：安全边界是**代码挡的**，不是提示词挡的

`McpSelfServiceLevel`（`McpSelfServiceLevel.cs:15`）四级：`Closed = 0` / `RemoteConfirmed = 1` / `AllConfirmed = 2` / `Unrestricted = 3`。默认 `Closed`（`McpScope.cs:68`）。

**在 `Closed` 下，`AddMcpServer` 这个工具根本不被注册**（`McpAgentToolkit.cs:64-65`）—— 不是「注册了但会拒绝」。理由写在 `:62-63`：一个模型看得见却永远用不了的工具只浪费提示预算，还招来重试。

提示文案也是**按级别生成**的（`McpAgentToolkit.cs:91-113`），理由同样写在注释里：说「服务器不可添加」在 `Closed` 以上是撒谎，说「可以添加」在 `Closed` 是撒谎。

**两级门是独立的，都要过：**

1. `McpScope.CanAddServer(runMode)`（`:108`）—— 级别 + 运行模式的组合判定。`RemoteConfirmed` 只放 `Http`，`AllConfirmed` 放本地模式，`Unrestricted` 不问。`AddServer` 里第一件事就是查它（`McpAgentToolkit.cs:134`）。
2. 确认：在 `Unrestricted` 以下的级别，添加要用户同意（`RequiresConfirmationToAdd()`，`:120`；`ConfirmationResolver`，`:101`）。

**级别与确认处理器是两回事。** `WithSelfService` 只开级别；`WithConfirmationHandler`（`:92`）注册裁决者。**没注册处理器时，需要确认的级别会「拒绝」，而不是「放行」** —— 这条在 `skills/veloxdev-drive-workflow-with-ai/SKILL.md:179` 与 `references/mcp.md:112` 都写死了。

**接进 `WorkflowAgentScope` 时，你在 `McpScope` 上设的确认处理器会被顶掉。** `WithMcps`（`WorkflowAgentScope.cs:1478`）无条件执行 `mcp.WithConfirmationHandler(ResolveConfirmationAsync)`，注释明说「直接设在 MCP scope 上的处理器会被它替换」。这是刻意的：审批**只配一次**，工作流工具与 MCP 自服务共用同一个。所以宿主只需要在 **scope** 上调 `WithConfirmationHandler`（`WorkflowAgentScope.cs:521`）。

---

## 二、装载路径

```
LoadAsync(servers, ct)        McpScope.cs:570   ← 破坏性：先清空已装载的一切
AddAsync(config, ct)          McpScope.cs:603   ← 中途加一个，不清空
  └─ LoadOneAsync(config, root, ct)   :670
       ├─ TrackServerAsync(config)    :732   在编组块内建/复用一个状态行
       ├─ 本地模式(Npm/Pip)：Installing 状态 → 装运行时
       ├─ Connecting 状态
       ├─ ConnectServerAsync          :686
       └─ 成功 → 写 _loadedToolSets / _loadedClients / _loadedConfigs，Version++
          失败 → 该服务器状态置 Error、发 ServerError 事件、**Version 仍然 ++**、返回空工具数组
```

**失败也自增 `Version`**（`:710-712`）—— 这是个容易漏的点：失败的服务器不贡献工具，但它在状态列表里**多了一行 Error**，按 `Version` 缓存的渲染必须失效。

**本地装载会真的在用户机器上装东西**：`EnsureNpmPackageAsync` / `EnsurePipPackageAsync`（`:802`、`:838`），用 `CliWrap` 起子进程。**进程级一把静态锁** `s_installLock`（`SemaphoreSlim(1,1)`，`:53`）+ 进程级已装列表 `s_installed`（`:54`），且都是**先查表、再取锁、再查一次**的双检（`:802-807`）。所以给两个 `McpScope` 实例各装同一个包，只会装一次。

**运行目录**：默认 `.evn/mcp`（相对 `AppContext.BaseDirectory`，字段 `McpRootRelative`，`:49`），按运行模式分子目录 —— `GetRuntimeDir`（`:774-779`）：Npm/Npx → `node`，Pip/Uvx → `py`，Dotnet → `dotnet`，Exe → `exe`。

**卸载**：`UnloadServerAsync(name)`（`:447`）把状态重置为 `NotStarted` 并把 `ToolCount` 清零；它可以从「没装载过」的服务器上安全调用。`UnloadServer(name)`（`:439`）是阻塞包装，注释明说异步代码该用 `*Async`。

---

## 三、`Options` 是白名单校验的，不是自由 blob

`McpServerConfiguration.Options`（`McpServerConfiguration.cs:62`）是个匿名对象，序列化后**按运行模式**过白名单（`EnsureKnownKeys`，`McpScope.cs:1059-1065`）：

| 模式 | 允许的键 | 位置 |
|---|---|---|
| Http | `headers` / `oauth` / `connectionTimeout` / `transportMode` / `ownsSession` | `McpScope.cs:1033` |
| Stdio（Npm/Npx/Pip/Uvx/Dotnet/Exe） | `env` / `workingDirectory` | `McpScope.cs:1034` |

**写了别的键 ⇒ 抛异常**（`:1059-1065`），错误信息会把允许列表列出来。所以要加一个新的 option 键，必须同时改 `HttpOptionKeys` / `StdioOptionKeys` **以及** 消费它的 `BuildHttpTransportOptions`（`:985`）/ `BuildStdioTransportOptions`（`:943`）。

`connectionTimeout` 有两级来源：`Options.connectionTimeout`（秒数或 TimeSpan 字符串）**优先于**作用域级的 `WithConnectionTimeout`（`ParseTimeout`，`:1078-1082`）。宿主样例两个值：Microsoft Learn 30 秒、一个故意不可达的示例服务器 8 秒（`AgentHelper.cs:60`、`:70`）。

---

## 四、`CreateContextProvider` 与工具

`McpScope.CreateContextProvider(ToolPipeline? tools, AgentPipeline? pipeline)`（`:426`）。

- `McpAgentToolkit.CreateTools()`（`McpAgentToolkit.cs:52`）返回**未包装**的四个管理工具（`ToolNames`，`:36`）；`CreateTools(ToolPipeline, AgentPipeline?)`（`:75`）返回包装过的 —— **provider 用的是后者**（`McpAgentContextProvider.cs` 内），所以 MCP 工具与内置工具受同一套预算闸门管。
- `CreateTools()` 里的 `ListServers` / `LoadServers` / `UnloadServer` / `DescribeServer` 是**按 `ToolNames` 的下标**取的（`:56-59`）。改 `ToolNames` 的顺序会静默改变工具名与实现的对应关系 —— 这是这里唯一一处「数组位置即契约」。
- `McpAgentContextProvider` 只按 **`scope.Version`** 缓存（**没有语言维度**）。`Version` 在装载 / 添加 / 卸载 / 新增预注册 / 服务器失败时自增（`:277`、`:345`、`:383`、`:699`、`:712`）。
- `BuildInventoryBlock()`（`:394`）读 **`Status.Snapshot`** 而不是绑定的 `ObservableCollection` —— 注释写明理由：提示是在 **agent 调用线程**上渲染的，不是 UI 线程。
- **这个 provider 只贡献 MCP 这一片，不含工作流内置工具**（`McpAgentContextProvider.cs:105-106`：「内置工具是工作流层的，两者都要的宿主就把两个 provider 都挂上」）。所以 `CreateContextProviders()` 返回的那个数组是「每一片各出一个 provider」的组合，不是「谁都带全套」。
- 服务器的工具（`McpClientTool` 派生自 `AIFunction`）**与内置工具同样被 `TrackedAIFunction` 包**（`:110-113`）；不是 `AIFunction` 的工具**原样放行**而不是被丢掉 —— 也就是说这类工具**不受预算与编组约束**，这是唯一一处「包装会漏」的地方。
- **MCP 管理工具的只读分类是缺的**：`ListMcpServers` / `LoadMcpServers` / `UnloadMcpServer` / `DescribeMcpServer` 都不在 `WorkflowAgentToolkit.BuildQueryToolNames` 的字面量集合里（`WorkflowAgentToolkit.cs:294-302`），而 `McpAgentToolkit.ToolNames` **没有**像 `SkillAgentToolkit.ToolNames` 那样被自动并入（只有技能那一条在 `:304`）。后果：它们被算作**写调用**，计入 `MaxWriteToolCalls`，并且在 `AutoMarkDirty = true` 时**会把工作流标脏**（`WorkflowAgentToolkit.cs:331`）。demo 之所以看不出问题，是因为它设了 `WithAutoMarkDirty(false)`。

**`McpScope.InstanceId`**（`:310`）是每 scope 一个 `Guid`，给 provider 做 state key。注释里有一条刻意设计：同一个 scope 造两个 provider 会**在 agent 构造时响亮地失败**，这是想要的；如果改成按 provider 生成 id，两个 provider 就会都通过，然后在同一轮里**把每个 MCP 工具和 inventory 块各塞两遍**。

---

## 五、状态驱动：`UpdateStatus`（发起即忘）vs `RunOnUIAsync`（等待）

`McpStatusViewModel.Servers` 是绑到宿主 UI 的 `ObservableCollection`，所以**读它也必须在编组块内**（`TrackServerAsync`，`:726-731` 的注释写明了这点）。

由此分出两条路：

| 方法 | 行为 | 用在哪 |
|---|---|---|
| `UpdateStatus(Action)`（`:506`） | **发起即忘**。没有 UI 上下文、或已经在 UI 上下文上时短路直接执行 | 状态刷新 —— 不需要等 |
| `RunOnUIAsync(Action)`（`:526`） | **await** | 装载/卸载各步 —— 注释说：「完成某一步装载的人要能看见这一步造成的状态」 |

---

## 六、扩展点

**加一个 MCP 管理工具**

1. 在 `McpAgentToolkit` 加一个 `private async Task<string> Xxx(...)`，用 `[Description]` 写清用途与前置条件。
2. 在 `CreateTools()`（`:52`）里 `AIFunctionFactory.Create(Xxx, "工具名")` 注册。
3. **如果它该算只读**：把名字加进 `WorkflowAgentToolkit.BuildQueryToolNames`（`WorkflowAgentToolkit.cs:290`）—— 注意这里**没有** `McpAgentToolkit.ToolNames` 的自动并入（只有技能工具是自动的，见 `:304`）。目前四个 MCP 管理工具**都不在**那个只读集合里。
4. 若它的行为随自服务级别变化，同步改 `BuildPromptContext()`（`:91`）的 switch —— 否则提示与实际能力脱节。

**加一个运行模式**

1. `McpServerRunMode.cs` 加枚举值。
2. `McpScope.GetRuntimeDir`（`:774`）加分支。
3. `McpScope` 里装载分支：需要装运行时的模式要在 `LoadOneAsync`（`:676`）的 `Installing` 段里接上；需要自己的 transport 要接 `ConnectServerAsync`。
4. `McpServerConfiguration` 的必填字段校验：`AddServer`（`McpAgentToolkit.cs:142-145`）与 `EnsureKnownKeys` 的白名单。
5. `McpAgentToolkit.BuildPromptContext` 的措辞、以及 `AddServer` 的 `[Description]`（`:121`，里面逐条列了模式名）。

**捷径（能编译，但是错的）**

| 捷径 | 为什么错 |
|---|---|
| 在宿主里把 `McpAgentToolkit.CreateTools()` 的结果手动塞进 agent 的工具列表 | 拿到的是**未包装**的一套（`McpAgentToolkit.cs:72-73` 明说「手动注册未包装的那套什么也得不到」）：没有编组、没有预算、没有回调；并且与 provider 贡献的那份**重名**，框架的并集**不去重**，模型会收到两遍 |
| 在 `McpScope` 之外自己 `new McpClient` 连服务器 | 不会进 `_loadedToolSets`，于是 `LoadedTools`、`BuildInventoryBlock()`、状态面板、卸载路径全都看不见它 |
| 把「拒绝」实现成返回一句友好文本而不返回 `{"status":"error"}` 信封 | `TrackedAIFunction` 的 `Error()` 信封（`TrackedAIFunction.cs:130`）是模型识别失败的唯一约定；纯文本会被当成成功结果 |
| 在 `UpdateStatus` 里 await 别的东西 | 它设计成发起即忘且可短路；在 UI 上下文之外调用时行为不同 |
| 让两个 provider 共用一个 scope 却不给唯一的 `InstanceId` | 框架在 state key 冲突时抛（`McpScope.cs:305-307`），这正是想要的失败 |
| 认为 `WithSelfService` 开了级别就够了 | 还需 `WithConfirmationHandler`；否则 `RemoteConfirmed`/`AllConfirmed` 下**一律拒绝** |

---

## 七、死面 / 仓库内零真实使用者

- **`WithSelfService` 没有任何非测试调用者。** 调用点全部在 `Src/Core/VeloxDev.Core.Extension.Test/Agent/MCP/McpSelfServiceTests.cs` 与 `McpAgentContextProviderTests.cs:119`。三个 demo 都停在 `Closed`，`AgentHelper.cs:211` 只是注释里提到「升档会加 AddMcpServer」。所以 **`AddMcpServer` 的完整路径（含确认、含四个级别的 prompt 分支）只有测试在跑**。
- `McpServerRunMode.Pip` / `Uvx` / `Dotnet` / `Exe` 在 demo 里都没有实例 —— demo 只配了 `Http` 与 `Npx`（`AgentHelper.cs:50-80`）。
- `McpScope.WithMcpRoot`（`:58`）在仓库内无调用者；`.evn/mcp` 是唯一被用到的根。
