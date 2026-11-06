# VeloxDev.Cli — 扩展

> 代码：`Src/Cli/VeloxDev.Cli/`。这份回答「要加一个 X，动哪几处」。

## 一、加一个图工具

**不要**在 CLI 里写新的工具实现。图工具的唯一来源是 `WorkflowAgentToolkit`：

1. 在 `Src/Core/VeloxDev.Core.Extension/Agent/Workflow/Functions/WorkflowAgentToolkit.cs` 加方法并配
   `[Description]`；
2. 在 `CreateAllTools` 里用 `T(...)` 注册，选对类别（`Query` / `Mutation` / `Execution` / …）；
3. 如果它属于只读语义，把名字并入 `BuildQueryToolNames` 的集合（决定读预算与 `AutoMarkDirty`）；
4. CLI 侧：`WorkflowSession.BaseCategories` 已覆盖六类，执行类由开关控制；只有新类别才需要改那里。
   如果这是 CLI 不该暴露的工具（需要 UI、需要换树、需要预算出口），把名字加进 `WorkflowSession.Excluded`。

CLI 的 MCP 工具面是 `TrackedAIFunction`（**internal**）转成 `McpServerTool.Create(AIFunction, options)`；
测试/扩展里只能以公开基类 `AIFunction` 持有它，不能引用内部类型。

## 二、加一个归档级工具

在 `ArchiveTools` 加一个方法：

```csharp
[McpServerTool(Name = "Something", ReadOnly = true)]
[Description("...")]
public string Something([Description("...")] string arg) => ...;
```

`McpHost` 用 `.WithTools(new ArchiveTools(session))` 注册它，不需要改别处。返回值是字符串；JSON 用
`System.Text.Json` 自己拼，和工具的 `{"status":"ok"|"error"}` 约定保持一致。

## 三、加一个清单字段

1. `WorkflowArchive.ReadManifest` 读取并校验；路径类字段必须走 `ResolveInside`；
2. 把值带进 `WorkflowArchive` 的属性，`WorkflowSession.Create` 决定怎么用；
3. 测试放 `WorkflowArchiveTests`，至少覆盖「合法值」「越界路径」「类型错误」。
4. 清单是**归档自带的**输入：不要让它把一个字段变成任意路径/任意代码的入口。

现有字段：`assemblies`（相对路径、锁在清单目录内）、`allowExecution`、`rootType`（具体树类型）。
`rootType` 存在的原因是**文档不总带根类型**：序列化器只在运行时类型 ≠ 声明类型时写 `$type`，宿主用
具体树类型调 `Serialize()` 时根节点没有标记；不给 `rootType` 就会退化成 `TreeDefaultViewModel`。
`WorkflowSession.ResolveRootType` 的查找顺序是注册表 → `Type.GetType` → 已加载程序集；`--root-type`
覆盖清单值。

## 四、加一个 CLI 选项

1. `CliOptions` 的属性 + `switch` 分支 + `Usage` 文本；
2. 谁消费它（`WorkflowSession.Create` / `McpHost` / `RunOnceAsync`）；
3. `CliOptionsTests` 加一条解析与一条拒绝用例。

## 五、执行隔离与重定向

**执行面有两套，二选一。** 默认是 `ExecutionTools` + `WorkerRunner`：两个工具
（`RunCompiledWorkflow` / `GetNodeResult`）各起一个 `veloxdev run` 子进程。
`--in-process-execution` 才让 `WorkflowSession.CreateMcpTools(host)` 把工具包的 `Execution` 类别加回工具面
（句柄、暂停/续跑、`ExecuteNode`）。**不要在隔离模式下也注册工具包的 Execution 类别** —— 两边同名同 schema，
MCP 客户端会看到重复工具；`WorkflowSession` 的 `InProcessExecution` 就是这条分叉。

**换档（LoadArchive）与会话代理**：

- 原生工具一律是 `SessionBoundAIFunction`：schema 来自启动会话，调用经 `WorkflowSessionHost.InvokeToolAsync`
  落到当前会话。**不要让任何工具方法捕获 `WorkflowSession` 字段**；归档/重定向/执行工具都拿
  `WorkflowSessionHost`，每次调用 `Acquire()` 后从 lease 取会话。
- `LoadArchive` 走 `WorkflowSessionHost.ReloadAsync`：先建新会话（失败则旧会话原样保留），再在锁内换引用；
  旧会话等最后一个 lease 释放后销毁。in-process 模式直接拒绝。
- 工具列表在启动时定死：执行/重定向工具是否注册、工具的 schema 都来自启动会话。换档不会改变这些全局开关；
  新归档的 `allowExecution` 不会额外打开执行。要改全局模式就重启。
- 插件的 `LoadArchive` 共用同一个 `PluginLoader` 和日志 writer；不要为每个会话新建（否则日志会被旧 writer
  锁住、依赖重复加载）。

**加一个隔离执行操作**：

1. `WorkerRunner` 加方法（构造命令行、快照、插件路径、重定向、检查点都在 `BuildStartInfo`）；
2. `ExecutionTools` 加 `[McpServerTool]`，参数与工具包里同名工具保持一致；
3. `Program` 的 `run` 子命令必须支持对应的选择参数（例如 `--node-result`）—— worker 就是同一个可执行文件，
   父进程能做的必须先能被 `run` 做；
4. 测试走 `McpProtocolTests` 的 stdio 路径，而不是直接调 `WorkerRunner`。

**worker 的硬约束**：

- 快照必须来自 `Scope.Tree.Serialize()`（**当前内存图**，不是磁盘归档；编辑可能还没保存）。它按接口静态类型
  序列化，会写根 `$type`，所以子进程不需要 `--root-type`；自定义类型仍要靠 `--assembly` 载入。
- 依赖从 `Plugins.LoadedPaths` 全量传给子进程；漏一个，子进程会在反序列化前报缺 reader。
- 命令行用 `ProcessStartInfo.ArgumentList`，不要拼字符串；路径里的空格、引号、种子载荷都不该被 shell 解析。
- 子进程的 `WorkingDirectory` 是 CLI 自己的输出目录（`typeof(WorkerRunner).Assembly.Location` 所在处），
  否则它的 `deps.json` / 运行时依赖可能解析到别处。
- 超时用 `timeoutMs + session.WorkerKillGraceMs`（默认 5000，`--worker-kill-grace-ms` 可调）的父级上限兜底，
  到点 `Kill(entireProcessTree: true)`；MCP 的取消 token 同样 kill。**杀掉不是错误**：返回
  `status:ok, runStatus:Stopped, outcome:Cancelled`（带 `cancelled`/`timedOut`），与引擎自己的取消结果同形。
- `--max-concurrent-runs` 由宿主的 `SemaphoreSlim` 实现；阻塞工具在起 worker 前等槽，`finally` 释放。
  后台任务由 `WorkerJobRegistry` 管：**Start 时拿 lease**（快照语义），任务里等槽，结束后释放槽/lease。
  不要把信号量放到静态字段，否则测试/多宿主会互相压制。
- 后台任务的状态用 per-job 锁；`Cancel` 只触发 CTS，由任务自己落到 `Cancelled`。`ClearFinished` 只清终态；
  服务 Dispose 会取消并等待所有任务。
- 子进程的 stdin 必须重定向并关闭，否则它会继承 MCP 的协议流；stdout/stderr 也重定向。
- 临时快照放 `%TEMP%/veloxdev-worker`，执行完删除；**post-run 树**（`--output-tree`）保留，结果里叫
  `stateFile`，由客户端用 `LoadArchive` 显式采纳。不要自动采纳 —— 会覆盖运行期间的编辑。
- **`--output-tree` 写失败是命令失败**（2026-11-06）：结果保留 run 字段并加 `stateFileError`，stderr 说明原因，
  进程 exit 1。不要退回「只警告、exit 0」—— CI 无法只凭退出码发现 artifact 缺失。
- **两种 timeout 语义不要混。** `run --timeout-ms` 走 toolkit 的 `RunCompiledRoleAsync`，它把自身超时返回
  `status:error`（不是 `ok + Cancelled`，见该方法的注释）；只有 MCP 隔离层在**杀掉不响应取消的 worker** 时才
  合成 `status:ok, outcome:Cancelled, timedOut:true`。`AHangingWorker_IsKilled...` 测后者，
  `ARunWithATimeout_ReportsTheToolkitsTimeoutError` 测前者。
- 节点代码写 stdout 会污染 worker 的 JSON 结果；in-process 模式则污染协议流。

**重定向**：`RedirectPolicy` 是会话级对象，`WorkflowSession.Create` 把它装进
`WithSessionConfiguration(ctx => ctx.HostRedirect = redirect.Resolve)`。引擎只在节点**没有** `IRedirectable`
时问它；节点契约优先。`Set` 时按当前树校验索引，引擎再按每次失败校验上游关系。不要让 `RunCompiledWorkflow`
多一个 `redirectTo` 参数 —— 那会把一个会话配置变成每次调用的魔法值，也不会传给 worker（worker 通过
`--redirect-on-error` 拿同一个会话设置）。

## 六、上下文资源、工作区与打包

- **resources / prompts**：`SessionResources`（`[McpServerResourceType]` + `[McpServerResource(UriTemplate=..., Name=..., MimeType=...)]`）
  与 `SessionPrompts`（`[McpServerPromptType]` + `[McpServerPrompt(Name=...)]`）由 `McpHost` 用
  `.WithResources(instance)` / `.WithPrompts(instance)` 注册。方法体必须 `host.Acquire()` 取当前会话，**不能**
  捕获 `WorkflowSession` 字段，否则换档后读到旧归档。prompt 返回 `string` 即可，SDK 会包成 prompt message。
- **工作区**：`WorkspaceRule.Normalize` 要求目录已存在并去掉尾分隔符；`Ensure` 用平台相关大小写比较。
  在创建目录/日志 writer **之前**校验，否则会先在沙箱外留下文件再抛。`WorkflowSession` 的启动/换档/保存/
  加载程序集/检查点/日志/`--output-tree` 都有检查。worker 子进程不传 `--workspace`，它的 `%TEMP%` 快照与
  state 文件不能被误判。**不解析符号链接**：`Path.GetFullPath` 只做字符串规范化，workspace 内一个指向外部的
  symlink/junction 仍能通过前缀比较。若沙箱要承担对抗性输入的安全职责，需要逐段 `ResolveLinkTarget` —— 当前没有。
- **打包**：`PackAsTool` + `ToolCommandName=veloxdev`；`dotnet pack -c Release -p:VeloxDevLocalCore=true` 会把
  ProjectReference 的 Core/Extension/MCP 依赖一起放进 `tools/net8.0/any/`（实测安装并运行通过）。不要把它改成
  PackageReference，否则 Release 会拉 NuGet 上尚未包含新 API 的 Core。包图标由 csproj 的
  `PackageIcon=veloxdev-logo.png` 与 `None Include="..\..\..\Assets\veloxdev-logo.png"` 两条共同提供，
  缺任一条 `dotnet pack` 都不会报错，但 nuspec 里没有图标。

## 七、坑（带依据）

- **模块初始化器不会自动跑。** `VeloxJsonRegistry` 只查注册表，`TypeOf` 没有反射兜底
  （`Serialization/VeloxJsonRegistry.cs` 的 `TypeOf`）。`PluginLoader.Load` 必须调
  `RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle)`，否则自定义类型会在反序列化时
  报「no registered JSON reader」。
- **加载 dll 就是执行代码。** 模块初始化器在 `Load` 里就跑完了；「执行开关」只控制 agent 能不能调
  `RunCompiledWorkflow`，不是加载边界。文档与清单必须这样写。
- **不要从另一个项目的输出目录启动 net8 的 CLI。** 测试工程是 net10，输出目录会把共享依赖压平成 net10
  变体；在那里 `dotnet VeloxDev.Cli.dll` 会报 `System.Runtime, Version=10.0.0.0` 加载失败。集成测试从
  CLI 自己的 `bin/<Config>/net8.0` 启动（`McpProtocolTests` 的路径计算）。
- **`PluginLoader` 的路径去重要按文件系统语义。** `_loaded` / `_directories` 用 `OrdinalIgnoreCase` 在 Linux 上
  会把仅大小写不同的两个目录当成同一个，漏掉第二个目录里的依赖。现在走 `PathComparer`（Windows 忽略大小写、
  Unix 区分）；程序集简单名的比较仍可保持忽略大小写，那是 CLR 自己的规则（2026-11-06）。
- **CLI 测试在干净环境要先塞一个 dummy `API_KEY_DEEPSEEK`。** demo `TreeViewModel` 构造时安装 AgentHelper，
  没有 key 会在后台任务里抛未处理异常、直接把测试宿主带崩（37 个测试后 abort）。`TestAssemblySetup` 的
  `[AssemblyInitialize]` 只在变量为空时写占位值；不要删掉它来「跑真实模型」。
- **工具包会无条件附带 `ResetToolCallLimit` 与自定义工具。** `CreateTools(category)` 不是纯类别过滤；
  CLI 必须按名字排除预算出口，否则会给出一个永远用不上的工具。
- **Hosting 的 console logger 默认写 stdout。** stdio MCP 下必须
  `ClearProviders()` 并 `LogToStandardErrorThreshold = Trace`；否则日志会污染协议流。
- **换树会让状态跟踪器指向旧图。** `WorkflowStateTracker` 在构造时捕获树；所以 `LoadWorkflow` 不在
  MCP 面里，一个进程只服务一个归档。
- **`run` 与 `mcp` 共用 `RunCompiledWorkflow`。** 新增执行入口时不要绕过工具包——否则预算、权限、
  `AgentToolCallCompleted` 事件与重定向语义会在两条路径上分叉。
