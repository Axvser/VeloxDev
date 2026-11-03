# VeloxDev.Cli — 架构

> 代码：`Src/Cli/VeloxDev.Cli/`。一个无头宿主，同时是一个 stdio MCP 服务。
> 它不重写图模型、命令、编译器或运行时 —— 它把 `VeloxDev.Core.Extension` 已经治理过的那套工具原样发出去。

---

**构建**：项目总是 `ProjectReference` 工作区的 `VeloxDev.Core.Extension`（它不是包）。Debug 直接构建；Release
还要 `-p:VeloxDevLocalCore=true`，否则 Extension 的 Release 档会引用 NuGet 上尚未发布的 Core 11.0.0 与
Generator 11.0.0，CLI 编译失败。`Src/Directory.Build.targets` 现在在该属性下同时把 Core 和 Generator 换成
工作区工程（`Examples/Directory.Build.targets` 对示例做同一件事）。新版本发布后这条可以去掉。

**Agent 使用指南**：`skills/veloxdev-use-workflow-cli/`（`SKILL.md` + `references/practices/{launch,tools}.md`
+ `references/pitfalls/runtime.md`）。安装/启动、工具循环、执行与 job 语义、路径/重定向/日志规则都在这份里；
CLI 的行为改了要同步它，否则 agent 会按旧工具名或旧语义操作。

## 一、它解决什么问题

把「工作流归档 + 依赖程序集」变成一个 agent 可以直接操作和执行的进程：

```
graph.json + lib/*.dll
        │
        ├─ veloxdev mcp   →  stdio MCP：检查 / 编辑 / 编译 / 执行 / 保存
        └─ veloxdev run   →  一次性执行，给人和 CI 用
```

没有 GUI，没有手写 JSON，也不需要宿主自己接 chat client、确认处理器或 UI 线程。

## 二、分层与数据流

| 层 | 类型 | 职责 |
|---|---|---|
| 参数 | `CliOptions` | 解析 `mcp` / `run`，拒绝未知项；`--archive` 必填，`run` 还要 `--start` |
| 归档 | `WorkflowArchive` | 读文档与 sidecar 清单；把程序集路径解析为绝对路径并锁在清单目录内 |
| 依赖 | `PluginLoader` | 把程序集载入默认 ALC；`AssemblyDependencyResolver` + 目录探测；强制跑模块初始化器 |
| 会话 | `WorkflowSession` | 反序列化树、建 `WorkflowAgentScope` / `WorkflowAgentToolkit`、生成工具面、保存归档 |
| 会话宿主 | `WorkflowSessionHost` / `SessionBoundAIFunction` / `SessionLease` | 持有当前会话；工具是稳定代理，`LoadArchive` 换会话时不重注册工具；旧会话等最后一个 lease 释放再销毁 |
| 宿主 | `McpHost` | 建 Host、把日志改去 stderr、`AddMcpServer().WithStdioServerTransport()` |
| 归档工具 | `ArchiveTools` | `ArchiveInfo` / `LoadArchive` / `SaveArchive` / `LoadAssemblies` 四个非图命令 |
| 执行隔离 | `WorkerRunner` / `ExecutionTools` / `WorkerJobRegistry` | 默认把节点代码放到 `veloxdev run` 子进程里跑；子进程拿的是当前内存图的临时快照，跑完写回 post-run 树（`stateFile`）。后台任务用 `StartWorkflow` 起，`GetWorkflowJob`/`CancelWorkflowJob` 管理 |
| 重定向 | `RedirectPolicy` / `RedirectTools` | 会话级的 `RuntimeContext.HostRedirect`，只对没有 `IRedirectable` 的失败节点生效；MCP 面是 `SetRedirectOnError` / `ClearRedirectOnError`，仅在执行开启时注册 |
| 上下文 | `SessionResources` / `SessionPrompts` | 只读 resources（`veloxdev://archive/info`、`.../manifest`）与 `workflow-operator` prompt；都走 `host.Acquire()`，换档后读新会话 |
| 路径沙箱 | `WorkspaceRule` | `--workspace` 可选；启动路径、`LoadArchive`、`SaveArchive`、`LoadAssemblies`、检查点、日志、`--output-tree` 都必须在其下 |

`run` 与 `mcp` 走同一个 `WorkflowSession`，并且 `run` 调的是同一个 `RunCompiledWorkflow` AIFunction ——
两条路径的执行语义不会分叉。

## 三、不变量

1. **依赖必须先于反序列化载入。** 序列化器通过 `VeloxJsonRegistry.TypeOf(name)` 解 `$type`，**没有**
   `Type.GetType` 兜底。插件的生成注册代码在模块初始化器里；`PluginLoader.Load` 用
   `RuntimeHelpers.RunModuleConstructor` 强制它跑，否则文档里的自定义类型会报「没有注册 reader」。
   **根类型不在文档里**：序列化器只在「运行时类型 ≠ 声明类型」时写 `$type`，宿主用
   `treeViewModel.Serialize()`（声明类型就是具体树）时根节点没有标记。CLI 因此支持清单的 `rootType`
   与 `--root-type`；缺省才是 `TreeDefaultViewModel`。`WorkflowSession.ResolveRootType` 先查注册表、
   再查 `Type.GetType`、最后扫已加载程序集，所以插件类型只要已载入就能被指名。
2. **保存会补上根 `$type`。** `SaveArchive` 通过 `Scope.Tree.Serialize()`（静态类型是
   `IWorkflowTreeViewModel`）写盘，运行时类型与声明类型不同，所以保存的文件自带根类型标记；下次加载不再
   依赖清单的 `rootType`。这不是 bug，是「写回时把文档补完整」。
3. **换归档是换会话，不是换树。** 工具包的 `LoadWorkflow` 仍被排除（`WorkflowStateTracker` 持有构造时的树）。
   `LoadArchive` 新建整个 `WorkflowSession`，由 `WorkflowSessionHost` 原子换引用；原生工具是
   `SessionBoundAIFunction` 代理，schema 来自启动会话、调用落到当前会话，所以工具列表不用变。旧会话在最后
   一个 `SessionLease` 释放后销毁。**in-process 模式拒绝换档**：工具包的运行句柄属于旧会话，没有安全迁移。
   隔离 worker 是独立快照，换档不影响它们。
4. **执行工具是显式开关，而且默认在子进程里跑。** `--allow-execution` 或清单的 `allowExecution` 才开放执行；
   默认工具面只有 `RunCompiledWorkflow` / `GetNodeResult`，由 `ExecutionTools` → `WorkerRunner` 起一个
   `veloxdev run` 子进程。临时快照是 `Scope.Tree.Serialize()`（带根 `$type`），依赖路径来自
   `Plugins.LoadedPaths`，重定向设置与检查点文件一并传给子进程。`--in-process-execution` 才把工具包的
   `Execution` 类别放回来（可用暂停/续跑/句柄），代价是节点代码回到服务进程。`run` 模式本身就是短命进程，
   强制 in-process。
5. **stdout 是协议。** Hosting 默认的 console logger 写 stdout，`McpHost` 必须
   `ClearProviders()` + `LogToStandardErrorThreshold = Trace`；否则一行日志就破坏 JSON-RPC 流。
6. **工具名不另起一套。** 图工具直接从 `CreateTools(...)` 取 `AIFunction`，用启动会话的模板做 schema，
   再包成 `SessionBoundAIFunction` 交给 `McpServerTool.Create`。新增工具应改工具包，而不是在 CLI 里写第二份。
7. **编辑只改内存。** `SaveArchive` 才落盘（临时文件 + 替换）。MCP 的编辑工具没有隐式写文件。
8. **默认的执行边界是进程。** `WorkerRunner` 的子进程死掉只会得到一个 `{status:error, stderr}` 结果，
   MCP 服务继续活着；超时/取消后父进程 `Kill(entireProcessTree)`，返回 `outcome: Cancelled`（不是基础设施
   错误）。`--max-concurrent-runs` 用宿主级 `SemaphoreSlim` 限流；`--worker-kill-grace-ms` 控制超时后的
   强杀宽限。后台任务在 `WorkerJobRegistry` 里：Start 时**立即拿一个会话 lease**（快照语义，之后换档不影响它），
   再等 worker 槽；任务结束才释放 lease/槽。`--in-process-execution` 明确放弃这条边界。
9. **重定向是会话级宿主策略。** `RedirectPolicy` 装在 `WithSessionConfiguration` 里，`HostRedirect` 只被
   引擎用于**没有** `IRedirectable` 的失败节点；节点自己的契约（包括返回 null 继续）优先。非法宿主目标
   会结束整轮而不是被忽略；策略抛异常按 Redirect 档失败。引擎每次失败都会重新校验上游关系，所以单个
   会话设置不会替所有失败背书。
10. **检查点默认在内存。** `--checkpoint-file` 换 `FileCheckpointStore`；隔离模式下子进程写同一个文件，
    但目前只有 in-process 面暴露 `ContinueCompiledWorkflow`。
11. **日志与状态都有落盘出口。** `--log-file` 在 in-process 模式下由宿主持有 `TextWriterLogWriter`（跨换档
    不关）；**隔离模式宿主不打开 writer**，只解析路径并传给 worker —— `TextWriterLogWriter.For` 用
    `FileShare.Read`，父子各开一个会冲突。运行结果因此带 `logFile`；否则结果只有受 64 KiB 上限约束的日志。
    隔离 worker 跑完写 post-run 树，结果带 `stateFile`，由 `LoadArchive` 显式采纳 —— **不自动合并**，
    避免覆盖运行期间的编辑。
12. **插件版本不匹配要说出来。** 默认 ALC 按简单名统一，`PluginLoader` 加载后检查直接引用的
    `VeloxDev.Core` / `VeloxDev.Core.Extension` 版本，写进 `Warnings`，由 `ArchiveInfo.pluginWarnings` 暴露。
13. **路径沙箱是 opt-in。** `WorkspaceRule.Normalize` 要求工作区目录存在；`Ensure` 用平台相关的大小写比较
    和 `工作区 + 分隔符` 前缀。`WorkflowSession` 在启动、换档、保存、加载程序集、检查点/日志/输出树处都调用
    它。worker 子进程**不**带 `--workspace`（它的快照/状态在 `%TEMP%`），所以内部路径不受限。
14. **只读上下文也走 MCP。** `SessionResources` 提供 archive info/manifest 两个资源，`SessionPrompts`
    提供 `workflow-operator`；两者都用 lease 取当前会话，换档后自然读新归档。
15. **工具包是本地包。** `PackAsTool` + `ToolCommandName=veloxdev`，`dotnet pack -c Release
    -p:VeloxDevLocalCore=true` 会生成 `VeloxDev.Cli.<version>.nupkg`；包内已带 Core/Extension/MCP 依赖
    （实测 tool install + run 通过）。发布仍走仓库的 release skill。

## 四、工具面的取舍

暴露：`Query | Mutation | Graph | Layout | Analytics | State`，执行开启时再加 `Execution`。
排除（`WorkflowSession.Excluded`）：`LoadWorkflow`、`MarkDirty`、`ResetToolCallLimit`、`Locate*`、
交互 / Plan 工具（无 handler 也不注册）。`Command` 类别（任意命令）不暴露；专用编辑工具已覆盖正常路径。

`ArchiveTools` 是唯一始终注册的手写 MCP 工具组，因为归档级操作不属于图命令：报告状态、写回文件、动态补依赖。
`RedirectTools` 只在执行开启时注册（没有 run 就没有可配置的 fallback）；`ExecutionTools` 只在**隔离**执行时
注册，in-process 模式用工具包自己的 `Execution` 类别。

## 五、执行与重定向

- `RunCompiledWorkflow` / `GetNodeResult` 等由工具包提供，CLI 不包装。
- 重定向是**节点契约**：`IRuntimeContext.Error` 或异常触发，节点实现 `IRedirectable` 时由
  `ResolveRedirectAsync` 返回上游节点；引擎映射到编译 Order、结束本趟、从目标重跑，并保留目标之前的产物；
  上限 50 次；非上游目标忽略并记一行日志。
- Agent 侧目前能做：看 `attempts` / `failures` / 日志；换一个上游节点重新 `RunCompiledWorkflow`；
  修复后用 `ContinueCompiledWorkflow` 从内存检查点续跑。**不能**在运行时指定重定向目标 —— 这是刻意的，
  见 `Src/Cli/VeloxDev.Cli/README.md` 的「Execution and redirects」。
- 若将来要宿主级重定向，正确缝是 `RuntimeContext` 上再加一个可选解析器（与 pause gate / observer /
  retry / error sink / compensator 并列），在 run 启动时配置，而不是给每次工具调用加参数。
