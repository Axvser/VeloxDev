using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using VeloxDev.AI;
using VeloxDev.AI.Workflow;
using VeloxDev.AI.Workflow.Functions;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli;

/// <summary>
/// One loaded archive and the native surface that operates it.
/// <para>
/// The scope and its toolkit are the same objects an embedded host uses. The MCP server adds a transport, not a
/// second implementation: every tool the client sees is the library's own <see cref="AIFunction"/>, wrapped with
/// the same tool wrapper, permission gate and argument validation as an in-process Agent.
/// </para>
/// </summary>
internal sealed class WorkflowSession : IAsyncDisposable
{
    /// <summary>
    /// Tools the CLI deliberately does not expose.
    /// <para>
    /// <c>LoadWorkflow</c> would replace the tree behind the state tracker the toolkit already built;
    /// <c>MarkDirty</c> and <c>Locate*</c> exist for a GUI host; the interaction and plan tools need a human
    /// dialog; and <c>ResetToolCallLimit</c> guards a budget the CLI never sets.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoadWorkflow",
        "MarkDirty",
        "ResetToolCallLimit",
        "LocateNode",
        "LocateSlot",
        "LocateLink",
        "RequestSelection",
        "RequestConfirmation",
        "EnterPlanMode",
        "ExitPlanMode",
    };

    private const WorkflowToolCategory BaseCategories =
        WorkflowToolCategory.Query
        | WorkflowToolCategory.Mutation
        | WorkflowToolCategory.Graph
        | WorkflowToolCategory.Layout
        | WorkflowToolCategory.Analytics
        | WorkflowToolCategory.State;

    private readonly TextWriterLogWriter? _ownedLogWriter;
    private readonly Dictionary<string, AIFunction> _tools;

    private WorkflowSession(
        string archivePath,
        string? manifestPath,
        string? checkpointFile,
        string? logFile,
        WorkflowAgentScope scope,
        PluginLoader plugins,
        RedirectPolicy redirect,
        bool executionEnabled,
        bool inProcessExecution,
        int workerKillGraceMs,
        string? workspace,
        bool singleArchive,
        bool runCsv,
        TextWriterLogWriter? ownedLogWriter)
    {
        ArchivePath = archivePath;
        ManifestPath = manifestPath;
        CheckpointFile = checkpointFile;
        LogFile = logFile;
        Scope = scope;
        Toolkit = scope.CreateToolkit();
        Plugins = plugins;
        Redirect = redirect;
        ExecutionEnabled = executionEnabled;
        InProcessExecution = inProcessExecution;
        WorkerKillGraceMs = workerKillGraceMs;
        Workspace = workspace;
        SingleArchive = singleArchive;
        RunCsvEnabled = runCsv;
        _ownedLogWriter = ownedLogWriter;
        _tools = BuildExposedFunctions().ToDictionary(
            t => t.Function.Name, t => t.Function, StringComparer.OrdinalIgnoreCase);
    }

    public string ArchivePath { get; private set; }

    public string? ManifestPath { get; }

    public WorkflowAgentScope Scope { get; }

    public WorkflowAgentToolkit Toolkit { get; }

    public PluginLoader Plugins { get; }

    /// <summary>The host-level redirect fallback every run session reads.</summary>
    public RedirectPolicy Redirect { get; }

    /// <summary>Where checkpoints go, or <c>null</c> for the scope's in-memory store.</summary>
    public string? CheckpointFile { get; }

    /// <summary>Where the run log is appended, or <c>null</c> when the host did not ask for a file.</summary>
    public string? LogFile { get; }

    public bool ExecutionEnabled { get; }

    /// <summary>
    /// Whether node code runs in this process. The default is a worker; <c>--in-process-execution</c> keeps the
    /// toolkit's handle-based execution tools but gives up crash isolation.
    /// </summary>
    public bool InProcessExecution { get; }

    /// <summary>How long the server waits past a worker's timeout before killing it.</summary>
    public int WorkerKillGraceMs { get; }

    /// <summary>The optional path sandbox every read/write is checked against.</summary>
    public string? Workspace { get; }

    /// <summary>
    /// When true the process serves exactly one document: <c>LoadArchive</c> is refused, so runtime IDs stay
    /// valid for the whole process lifetime.
    /// </summary>
    public bool SingleArchive { get; }

    /// <summary>Whether every execution result also gets a CSV log under the task directory.</summary>
    public bool RunCsvEnabled { get; }

    /// <summary>The per-process task directory, or <c>null</c> before a dependency or run log created it.</summary>
    public string? TaskDirectory => Plugins.TaskDirectory;

    /// <summary>The directory execution CSVs go to, or <c>null</c> before the task directory exists.</summary>
    public string? RunLogDirectory => Plugins.TaskDirectory is { } directory
        ? Path.Combine(directory, "runs")
        : null;

    /// <summary>What the MCP surface does about execution.</summary>
    public string ExecutionMode => !ExecutionEnabled
        ? "disabled"
        : InProcessExecution ? "in-process" : "isolated";

    /// <summary>Single-session convenience entry used by <c>run</c> and by tests.</summary>
    public static WorkflowSession Create(CliOptions options, bool forceExecution = false)
    {
        var workspace = WorkspaceRule.Normalize(options.Workspace);
        WorkspaceRule.Ensure(workspace, options.Archive, "Archive");
        WorkspaceRule.Ensure(workspace, options.Manifest, "Manifest");
        WorkspaceRule.Ensure(workspace, options.CheckpointFile, "Checkpoint file");
        WorkspaceRule.Ensure(workspace, options.LogFile, "Log file");
        foreach (var assembly in options.Assemblies)
            WorkspaceRule.Ensure(workspace, assembly, "Dependency assembly");

        var plugins = new PluginLoader();
        plugins.StrictVersions = options.StrictPluginVersions;
        plugins.CopyAssemblies = !options.NoAssemblyCopy;
        string? logFile = null;
        TextWriterLogWriter? logWriter = null;
        if (!string.IsNullOrWhiteSpace(options.LogFile))
        {
            logFile = Path.GetFullPath(options.LogFile);
            var directory = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // A worker opens the same file itself; opening it here would take a writer share the child cannot get.
            if (forceExecution || options.InProcessExecution)
                logWriter = TextWriterLogWriter.For(logFile);
        }

        var request = new ArchiveRequest(options.Archive!, options.Manifest, options.Assemblies, options.RootType);
        return Create(
            options, request, plugins, logWriter, logFile, options.RedirectOnError,
            executionEnabled: forceExecution ? true : null,
            inProcessExecution: forceExecution ? true : null,
            ownsLogWriter: logWriter is not null);
    }

    /// <summary>
    /// Builds a session over one archive. Assemblies are loaded into the caller's shared loader, and the log writer
    /// is owned by the caller unless <paramref name="ownsLogWriter"/> says otherwise — that is what lets the MCP
    /// host keep one log file across archive reloads.
    /// </summary>
    internal static WorkflowSession Create(
        CliOptions options,
        ArchiveRequest request,
        PluginLoader plugins,
        TextWriterLogWriter? logWriter,
        string? logFile,
        int? redirectOnError,
        bool? executionEnabled = null,
        bool? inProcessExecution = null,
        bool ownsLogWriter = false)
    {
        var workspace = WorkspaceRule.Normalize(options.Workspace);
        WorkspaceRule.Ensure(workspace, request.Path, "Archive");
        WorkspaceRule.Ensure(workspace, request.Manifest, "Manifest");
        WorkspaceRule.Ensure(workspace, options.CheckpointFile, "Checkpoint file");
        WorkspaceRule.Ensure(workspace, logFile, "Log file");

        var archive = WorkflowArchive.Load(request.Path, request.Manifest);

        foreach (var assembly in archive.Assemblies)
        {
            WorkspaceRule.Ensure(workspace, assembly, "Dependency assembly");
            plugins.Load(assembly);
        }
        foreach (var assembly in request.Assemblies ?? [])
        {
            WorkspaceRule.Ensure(workspace, assembly, "Dependency assembly");
            plugins.Load(assembly);
        }

        var rootType = ResolveRootType(request.RootType ?? archive.ManifestRootType);

        IWorkflowTreeViewModel tree;
        try
        {
            tree = VeloxJsonSerializer.Deserialize(archive.Json, rootType) as IWorkflowTreeViewModel
                ?? throw new InvalidDataException(
                    $"The document did not deserialize to a tree of type '{rootType.FullName}'.");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"The workflow archive could not be read: {archive.Path}. If the document names a component type, " +
                $"load the assembly that declares it with --assembly (or list it in the archive manifest). Cause: {ex.Message}",
                ex);
        }

        var execution = executionEnabled ?? (options.AllowExecution || archive.ManifestAllowsExecution);
        var inProcess = inProcessExecution ?? options.InProcessExecution;
        var scope = new WorkflowAgentScope(tree);
        if (execution) scope.WithAllowNodeExecution(true);

        // Loaded assemblies register their [AgentContext] types in their module initializers; this copies them
        // into the prompt-facing customer sets. It is idempotent, so LoadAssemblies can call it again later.
        scope.WithAutoDiscovery(AgentLanguages.English);

        // Every compiled run gets the same host policy object; the agent can change its target between runs.
        var redirect = new RedirectPolicy(() => scope.Tree);
        scope.WithSessionConfiguration(context => context.HostRedirect = redirect.Resolve);
        if (redirectOnError is { } redirectIndex) redirect.Set(redirectIndex);

        if (logWriter is not null)
            scope.WithLogWriter(logWriter);

        string? checkpointFile = null;
        if (!string.IsNullOrWhiteSpace(options.CheckpointFile))
        {
            checkpointFile = System.IO.Path.GetFullPath(options.CheckpointFile);
            var directory = System.IO.Path.GetDirectoryName(checkpointFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            scope.WithCheckpointStore(new FileCheckpointStore(checkpointFile));
        }

        return new WorkflowSession(
            archive.Path, archive.ManifestPath, checkpointFile, logFile ?? logWriter?.Path, scope, plugins, redirect,
            execution, inProcess, options.WorkerKillGraceMs, workspace, options.SingleArchive,
            !options.NoRunCsv, ownsLogWriter ? logWriter : null);
    }

    /// <summary>
    /// The tools this archive exposes over MCP, annotated for the client's approval UI.
    /// <para>
    /// Each tool is a stable proxy bound to <paramref name="host"/>, not to this session's toolkit. That is what
    /// makes <c>LoadArchive</c> possible without re-registering tools: the proxy resolves the current session at
    /// call time, while the name, description and schema come from this (the startup) session.
    /// </para>
    /// </summary>
    public IReadOnlyList<McpServerTool> CreateMcpTools(WorkflowSessionHost host)
    {
        var tools = new List<McpServerTool>();
        foreach (var (function, mutating, executable) in BuildExposedFunctions())
        {
            tools.Add(McpServerTool.Create(new SessionBoundAIFunction(host, function), new McpServerToolCreateOptions
            {
                ReadOnly = !mutating && !executable,
                Destructive = mutating || executable,
                OpenWorld = executable,
            }));
        }

        return tools;
    }

    /// <summary>The exposed native tool with this name, or <c>null</c> when this session does not have it.</summary>
    internal AIFunction? FindTool(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;

    // 批处理只允许图编辑与布局：归档工具会换会话，执行工具会起进程，查询工具没有写语义。
    internal bool IsBatchableTool(string name)
        => NamesOf(WorkflowToolCategory.Mutation).Contains(name)
           || NamesOf(WorkflowToolCategory.Layout).Contains(name);

    private static readonly HashSet<string> ExecutionResultToolNames = new(StringComparer.Ordinal)
    {
        "RunCompiledWorkflow",
        "GetNodeResult",
        "ContinueCompiledWorkflow",
    };

    /// <summary>True for the execution tools whose result carries the run log.</summary>
    internal bool IsExecutionResultTool(string name) => ExecutionResultToolNames.Contains(name);

    /// <summary>Writes the result's log/failures to a CSV and adds <c>csvFile</c>; best-effort.</summary>
    internal string AttachRunCsv(string tool, string json)
    {
        if (!RunCsvEnabled) return json;

        var path = RunLogCsv.TryWrite(Plugins.EnsureTaskDirectory(), tool, json);
        return path is null ? json : RunLogCsv.Attach(json, path);
    }

    internal ValueTask<object?> InvokeToolAsync(
        string name, AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var tool = FindTool(name)
            ?? throw new InvalidOperationException($"Tool '{name}' is not available in the current session.");
        return tool.InvokeAsync(arguments, cancellationToken);
    }

    private IEnumerable<(AIFunction Function, bool Mutating, bool Executable)> BuildExposedFunctions()
    {
        var categories = BaseCategories;
        if (ExecutionEnabled && InProcessExecution) categories |= WorkflowToolCategory.Execution;

        var mutationNames = NamesOf(WorkflowToolCategory.Mutation);
        var executionNames = NamesOf(WorkflowToolCategory.Execution);

        // Without in-process runs there is no run registry to list or clear; the tools would always answer empty.
        var excluded = new HashSet<string>(Excluded, StringComparer.OrdinalIgnoreCase);
        if (!InProcessExecution)
        {
            excluded.Add("ListCompiledRuns");
            excluded.Add("ClearFinishedRuns");
        }

        foreach (var function in Toolkit.CreateTools(categories).OfType<AIFunction>())
        {
            if (excluded.Contains(function.Name)) continue;
            yield return (function, mutationNames.Contains(function.Name), executionNames.Contains(function.Name));
        }
    }

    /// <summary>Sets or clears the host-level redirect fallback. The index is validated against the live tree.</summary>
    public void SetRedirectOnError(int? nodeIndex) => Redirect.Set(nodeIndex);

    /// <summary>Loads additional dependency assemblies and refreshes the agent-visible type registry.</summary>
    public IReadOnlyList<string> LoadAssemblies(IEnumerable<string> paths)
    {
        var loaded = new List<string>();
        foreach (var path in paths)
        {
            var full = System.IO.Path.GetFullPath(path);
            WorkspaceRule.Ensure(Workspace, full, "Dependency assembly");
            Plugins.Load(full);
            loaded.Add(full);
        }

        Scope.WithAutoDiscovery(AgentLanguages.English);
        return loaded;
    }

    // dryRun 只序列化并报告，不碰磁盘、不改 ArchivePath；backup 在替换前留一份 .bak；
    // compact 使用无空白格式，默认开启，避免每次保存都把 13 MB 的缩进文档写出去。
    public SaveArchiveResult SaveArchive(
        string? path = null,
        bool dryRun = false,
        bool backup = true,
        bool compact = true)
    {
        var target = string.IsNullOrWhiteSpace(path)
            ? ArchivePath
            : System.IO.Path.GetFullPath(path);

        WorkspaceRule.Ensure(Workspace, target, "Archive");
        var json = compact
            ? Scope.Tree.Serialize(SerializationOptions.Create().WithCompact())
            : Scope.Tree.Serialize();
        var bytes = System.Text.Encoding.UTF8.GetByteCount(json);
        var format = compact ? "compact" : "indented";

        if (dryRun)
        {
            return new SaveArchiveResult(
                target,
                bytes,
                format,
                null,
                ArchivePath,
                Scope.Tree.Nodes.Count,
                Scope.Tree.Links.Count);
        }

        var directory = System.IO.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // 先把新文档写到临时文件，再备份旧文件，最后替换：任何一步失败都不会留下半份目标文件。
        var temp = target + ".tmp";
        File.WriteAllText(temp, json);

        string? backupPath = null;
        if (backup && File.Exists(target))
        {
            backupPath = target + ".bak";
            File.Copy(target, backupPath, overwrite: true);
        }

        File.Move(temp, target, overwrite: true);
        ArchivePath = target;
        return new SaveArchiveResult(
            target,
            bytes,
            format,
            backupPath,
            ArchivePath,
            Scope.Tree.Nodes.Count,
            Scope.Tree.Links.Count);
    }

    // 一次回答「这个进程现在能做什么」：工具面、执行模式、结果上限、兼容状态，省去逐个工具猜测。
    public string DescribeCapabilities(WorkflowSessionHost host)
    {
        var tools = new List<string>(CreateMcpTools(host).Select(tool => tool.ProtocolTool.Name));

        // 归档级工具与隔离执行工具由 McpHost 直接注册，不在 CreateMcpTools 的会话工具面里；这里补上，
        // 否则 GetCapabilities 报出的工具清单比客户端实际看到的少。
        tools.AddRange(
        [
            "ArchiveInfo", "LoadArchive", "SaveArchive", "LoadAssemblies",
            "GetCapabilities", "GetAuditLog", "ApplyBatch",
        ]);
        if (ExecutionEnabled)
        {
            // RedirectTools 在两种执行模式下都由 McpHost 直接注册；漏掉它们会让 GetCapabilities 比客户端实际看到的少两个。
            tools.AddRange(["SetRedirectOnError", "ClearRedirectOnError"]);
        }
        if (ExecutionEnabled && !InProcessExecution)
        {
            tools.AddRange(
            [
                "RunCompiledWorkflow", "GetNodeResult", "StartWorkflow", "GetWorkflowJob",
                "ListWorkflowJobs", "CancelWorkflowJob", "ClearFinishedJobs",
            ]);
        }

        var orderedTools = tools
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            status = "ok",
            archive = ArchivePath,
            rootType = Scope.Tree.GetType().FullName,
            nodeCount = Scope.Tree.Nodes.Count,
            linkCount = Scope.Tree.Links.Count,
            executionEnabled = ExecutionEnabled,
            executionMode = ExecutionMode,
            inProcessExecution = InProcessExecution,
            singleArchive = SingleArchive,
            runtimeIdScope = SingleArchive ? "process" : "loaded-graph",
            maxToolResultChars = Scope.MaxToolResultChars,
            workspace = Workspace,
            assemblyCopy = Plugins.CopyAssemblies,
            assemblyStaging = Plugins.StagingDirectory,
            taskDirectory = TaskDirectory,
            runLogDirectory = RunLogDirectory,
            runCsv = RunCsvEnabled,
            compatibility = new
            {
                status = Plugins.Compatibility.Status,
                mismatchCount = Plugins.Compatibility.Mismatches.Count,
            },
            toolCount = orderedTools.Length,
            tools = orderedTools,
        });
    }

    /// <summary>The state a client needs before it starts editing.</summary>
    public string Describe()        => JsonSerializer.Serialize(new
        {
            status = "ok",
            archive = ArchivePath,
            manifest = ManifestPath,
            rootType = Scope.Tree.GetType().FullName,
            singleArchive = SingleArchive,
            runtimeIdScope = SingleArchive ? "process" : "loaded-graph",
            idStability = SingleArchive
                ? "stable for the process lifetime"
                : "stable until LoadArchive replaces the graph; start a new process to keep IDs",
            nodeCount = Scope.Tree.Nodes.Count,
            linkCount = Scope.Tree.Links.Count,
            executionEnabled = ExecutionEnabled,
            executionMode = ExecutionMode,
            redirectOnError = Redirect.NodeIndex,
            checkpointFile = CheckpointFile,
            logFile = LogFile,
            workspace = Workspace,
            assemblies = Plugins.LoadedPaths,
            assemblyCopy = Plugins.CopyAssemblies,
            assemblyStaging = Plugins.StagingDirectory,
            taskDirectory = TaskDirectory,
            runLogDirectory = RunLogDirectory,
            runCsv = RunCsvEnabled,
            pluginWarnings = Plugins.Warnings,
            compatibility = new
            {
                status = Plugins.Compatibility.Status,
                mismatches = Plugins.Compatibility.Mismatches.Select(mismatch => new
                {
                    assembly = mismatch.Assembly,
                    reference = mismatch.Reference,
                    wanted = mismatch.Wanted?.ToString(),
                    loaded = mismatch.Loaded?.ToString(),
                    majorMismatch = mismatch.MajorMismatch,
                }),
            },
            // Two separate assemblies: the host bundles both, and a plugin may drift from either. The old
            // single coreVersion read the Extension assembly, which made a Core-only mismatch look fine.
            coreVersion = typeof(VeloxDev.WorkflowSystem.IWorkflowTreeViewModel).Assembly.GetName().Version?.ToString(),
            extensionVersion = typeof(WorkflowAgentScope).Assembly.GetName().Version?.ToString(),
        });

    /// <summary>The instructions the server sends during initialization.</summary>
    public string BuildInstructions()
    {
        var execution = !ExecutionEnabled
            ? "Node execution is disabled for this process. Inspect and edit the graph; run it with a process started with --allow-execution."
            : InProcessExecution
                ? "Node execution is enabled in-process: RunCompiledWorkflow, StartCompiledWorkflow, pause/resume/status and the node-level execution tools are available."
                : "Node execution is enabled in an isolated worker: RunCompiledWorkflow and GetNodeResult run in a child process. Use SetRedirectOnError to install a host-level fallback for nodes that do not implement IRedirectable.";

        return $"""
            You are operating the VeloxDev workflow currently loaded in this server.
            The graph is live in this process; do not edit the JSON file directly while this server is running.

            Orient with GetWorkflowSummary and ListNodes. Use ListCreatableTypes before CreateNode, and GetNodeDetail
            before changing a node you did not just create. Every structural edit goes through a component command, so
            Undo and Redo work exactly as they do in the editor.

            Save with SaveArchive when the graph should survive this process. LoadArchive switches to another
            document; unsaved edits to the current graph are discarded. Runtime IDs belong to the loaded graph:
            they stay valid while it lives, and with --single-archive LoadArchive is refused so they last for the
            process. {execution}

            Dependencies are copied to tasks/<taskId>_<timestamp>/ beside this executable before they are loaded,
            so the source project's build output stays unlocked. ArchiveInfo.assemblyStaging names that directory;
            --no-assembly-copy disables the copy. Execution results are also written as
            tasks/<taskId>_<timestamp>/runs/<timestamp>.csv; the result's csvFile names the file, and --no-run-csv
            disables it. Read ArchiveInfo or GetCapabilities for taskDirectory / runLogDirectory / runCsv.
            """;
    }

    /// <summary>
    /// The concrete tree type to read the document into. When nothing names one, the framework base is the
    /// contract the serializer was written against; a derived tree must be named by the manifest or the CLI.
    /// </summary>
    private static Type ResolveRootType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return typeof(TreeDefaultViewModel);

        // The registry's names are the ones the document itself carries ("Full.Type.Name, Assembly"), so a
        // plugin type loaded a moment ago is found here before any reflection fallback.
        var type = VeloxJsonRegistry.TypeOf(name);

        if (type is null)
        {
            try { type = Type.GetType(name, throwOnError: false); }
            catch { /* a malformed name is reported below with the actionable message */ }
        }

        type ??= AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly =>
            {
                try { return assembly.GetType(name, throwOnError: false); }
                catch { return null; }
            })
            .FirstOrDefault(candidate => candidate is not null);

        if (type is null)
            throw new InvalidDataException(
                $"Root type '{name}' was not found. If it lives in a dependency, load that assembly with --assembly " +
                "or list it in the archive manifest.");

        if (!typeof(IWorkflowTreeViewModel).IsAssignableFrom(type))
            throw new InvalidDataException($"Root type '{name}' does not implement IWorkflowTreeViewModel.");

        return type;
    }

    private HashSet<string> NamesOf(WorkflowToolCategory category)
        => [.. Toolkit.CreateTools(category).Select(t => t.Name)];

    public async ValueTask DisposeAsync()
    {
        await Scope.DisposeAsync().ConfigureAwait(false);
        _ownedLogWriter?.Dispose();
    }
}

// SaveArchive 的结果；dryRun 时 BackupPath 为 null，ArchivePathAfterSave 仍是旧路径。
internal sealed record SaveArchiveResult(
    string Path,
    int Bytes,
    string Format,
    string? BackupPath,
    string ArchivePathAfterSave,
    int NodeCount,
    int LinkCount);
