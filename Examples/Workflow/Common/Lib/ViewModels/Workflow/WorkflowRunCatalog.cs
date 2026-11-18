using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>Where a task row came from: the host's own controls, or a tool call the Agent made.</summary>
public enum WorkflowRunSource
{
    /// <summary>The Run button on a controller card, or the host's own session configuration.</summary>
    Manual,

    /// <summary>A compile or run tool call the Agent issued.</summary>
    Agent,
}

/// <summary>What a task row is doing right now. A paused run stays <see cref="Running"/> and says so separately.</summary>
public enum WorkflowRunState
{
    /// <summary>The graph was compiled but has not been driven.</summary>
    Compiled,

    /// <summary>The run is in flight (or held at a node boundary).</summary>
    Running,

    /// <summary>The run walked every entry it was given.</summary>
    Completed,

    /// <summary>The host (or the Agent) cancelled it.</summary>
    Cancelled,

    /// <summary>A failure ended it early.</summary>
    Failed,
}

/// <summary>
/// One row of the run manager: a compiled graph awaiting a run, or a run session that is in flight or finished.
/// </summary>
/// <remarks>
/// <para>
/// The row owns the output page's content. Everything it is told — a run observation, a log line, a state read from
/// the session — is marshalled onto the synchronization context the catalog was built on, so a bound collection is
/// never touched from the engine's thread.
/// </para>
/// <para>
/// A compiled row and the run that replaces it share a <see cref="Key"/>: compiling twice updates one row, and
/// pressing Run turns it into the run rather than adding a second row beside it.
/// </para>
/// </remarks>
public sealed class WorkflowRunTaskViewModel : INotifyPropertyChanged
{
    private readonly SynchronizationContext? _ui;
    private readonly Action<WorkflowRunTaskViewModel, string>? _lineSink;
    private readonly Action<IWorkflowNodeViewModel?>? _nodeObserved;

    private WorkflowRunState _state = WorkflowRunState.Compiled;
    private bool _isPaused;
    private int _attempt;
    private int _segments;
    private int _nodesDriven;
    private DateTime _startedAt = DateTime.Now;
    private DateTime? _endedAt;
    private ManualExecutionGate? _gate;
    private Func<bool>? _stop;
    private Action? _start;
    private RuntimeContext? _context;

    internal WorkflowRunTaskViewModel(
        WorkflowRunSource source,
        string rootLabel,
        string key,
        int segments,
        SynchronizationContext? ui,
        Action<WorkflowRunTaskViewModel, string>? lineSink,
        Action<IWorkflowNodeViewModel?>? nodeObserved)
    {
        Source = source;
        RootLabel = rootLabel;
        Key = key;
        _segments = segments;
        _ui = ui;
        _lineSink = lineSink;
        _nodeObserved = nodeObserved;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The logical identity of the row; a compile row and the run that replaces it use the same key.</summary>
    internal string Key { get; }

    /// <summary>Whether the host started this task or the Agent did.</summary>
    public WorkflowRunSource Source { get; }

    /// <summary>The root node the graph was compiled from, in the canvas's own naming.</summary>
    public string RootLabel { get; }

    /// <summary>The rows the output page shows for this task: observations and raw log lines, in order.</summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>Which process started the task, as the row's badge.</summary>
    public string SourceText => Source == WorkflowRunSource.Manual ? "手动" : "Agent";

    /// <summary>The current state, with a glyph the table can read at a glance.</summary>
    public string StateText => _state switch
    {
        WorkflowRunState.Compiled => "▣ 已编译",
        WorkflowRunState.Running => _isPaused ? "Ⅱ 已暂停" : "● 运行中",
        WorkflowRunState.Completed => "✓ 已完成",
        WorkflowRunState.Cancelled => "■ 已停止",
        WorkflowRunState.Failed => "✗ 失败",
        _ => "—",
    };

    /// <summary>When the task started, in the run manager's short form.</summary>
    public string StartedText => _startedAt.ToString("HH:mm:ss");

    /// <summary>How long it has been running, or how long it ran.</summary>
    public string ElapsedText
    {
        get
        {
            if (_state is WorkflowRunState.Compiled) return "—";
            var elapsed = (_endedAt ?? DateTime.Now) - _startedAt;
            return elapsed.TotalSeconds < 60
                ? $"{elapsed.TotalSeconds:0.0} s"
                : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
    }

    /// <summary>The pass count, the driven-node count and the segment count, in one line.</summary>
    public string ProgressText
    {
        get
        {
            if (_state == WorkflowRunState.Compiled)
                return _segments > 0 ? $"编译 {_segments} 段" : "已编译";

            var progress = $"第 {Math.Max(1, _attempt)} 趟 · 驱动 {_nodesDriven} 次";
            // Agent 运行用的是它自己的编译器，宿主这边的段数可能还是 0；没有就别说。
            return _segments > 0 ? $"{progress} · {_segments} 段" : progress;
        }
    }

    /// <summary>Whether the run is still in flight (held counts as in flight).</summary>
    public bool IsRunning => _state == WorkflowRunState.Running;

    /// <summary>Whether the run is parked at a node boundary.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Whether the hold control applies.</summary>
    public bool CanPause => IsRunning && !_isPaused && _gate is not null;

    /// <summary>Whether the release control applies.</summary>
    public bool CanResume => IsRunning && _isPaused && _gate is not null;

    /// <summary>Whether the stop control applies. A synchronous Agent tool run has no cancellation source the host can reach.</summary>
    public bool CanStop => IsRunning && _stop is not null;

    /// <summary>Whether the run control applies (a compiled graph, or a finished run that can start again).</summary>
    public bool CanStart => !IsRunning && _start is not null;

    /// <summary>Whether the row can leave the manager.</summary>
    public bool CanRemove => !IsRunning;

    /// <summary>The state the output page highlights.</summary>
    public WorkflowRunState State => _state;

    /// <summary>Holds the run at its next node boundary.</summary>
    public void Pause()
    {
        if (!CanPause) return;
        _gate!.Pause();
        _isPaused = true;
        AddLine($"{Stamp()} 已请求暂停 · 停在下一个节点边界");
        NotifyState();
    }

    /// <summary>Lets a held run continue.</summary>
    public void Resume()
    {
        if (!CanResume) return;
        _gate!.Resume();
        _isPaused = false;
        AddLine($"{Stamp()} 已继续");
        NotifyState();
    }

    /// <summary>Asks the run to stop.</summary>
    public void Stop()
    {
        if (!CanStop) return;
        var requested = _stop!.Invoke();
        AddLine(requested
            ? $"{Stamp()} 已请求停止 · 当前节点结束后收尾"
            : $"{Stamp()} 这个任务不能从宿主停止 · 它是 Agent 正在等待的同步调用");
    }

    /// <summary>Starts the compiled graph again, when the row came with a way to do it.</summary>
    public void Start()
    {
        if (CanStart) _start!.Invoke();
    }

    /// <summary>Binds the row to the session that is about to drive it.</summary>
    internal void Attach(RuntimeContext context, Func<bool>? stop, Action? start)
    {
        _context = context;
        _gate = context.ExecutionGate as ManualExecutionGate;
        _stop = stop;
        _start = start;
        _startedAt = DateTime.Now;
        _endedAt = null;
        _state = WorkflowRunState.Running;
        _isPaused = _gate?.IsPaused == true;
    }

    /// <summary>Fills a compiled row with its outline and the way to run it.</summary>
    internal void SetCompiled(IEnumerable<string> lines, Action? start)
    {
        _start = start;
        _state = WorkflowRunState.Compiled;
        _isPaused = false;
        _endedAt = null;
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
        NotifyState();
    }

    /// <summary>One observation from the engine, in the order the run made it.</summary>
    internal void OnObserved(ExecutionObservation observation) => Post(() => Apply(observation));

    /// <summary>One line from the session's log writer.</summary>
    internal void AppendLog(string line) => Post(() => AddLine($"{Stamp()}   │ {line}"));

    /// <summary>Clears the output when a new run of the same row begins.</summary>
    internal void ClearLines() => Post(() =>
    {
        Lines.Clear();
        _attempt = 0;
        _nodesDriven = 0;
        NotifyState();
    });

    /// <summary>Refreshes the clock and reads a hold the Agent may have applied through its own tool call.</summary>
    internal void TickElapsed()
    {
        if (!IsRunning) return;

        var paused = _gate?.IsPaused == true;
        if (paused != _isPaused)
        {
            _isPaused = paused;
            NotifyState();
        }

        OnPropertyChanged(nameof(ElapsedText));
    }

    private void Apply(ExecutionObservation observation)
    {
        switch (observation.Kind)
        {
            case ExecutionObservationKind.RunStarted:
                _attempt = Math.Max(1, observation.Attempt);
                AddLine($"{Stamp()} 运行开始 · {RootLabel}");
                break;

            case ExecutionObservationKind.BranchStarted:
                AddLine($"{Stamp()} 分支开始 · {observation.Detail}");
                break;

            case ExecutionObservationKind.NodeStarted:
                _attempt = Math.Max(1, observation.Attempt);
                _nodesDriven++;
                AddLine($"{Stamp()} 开始 {NodeRef(observation.Node)}");
                _nodeObserved?.Invoke(observation.Node);
                break;

            case ExecutionObservationKind.NodeSucceeded:
                AddLine($"{Stamp()} 完成 {NodeRef(observation.Node)} · {Duration(observation.Elapsed)}");
                _nodeObserved?.Invoke(observation.Node);
                break;

            case ExecutionObservationKind.NodeFailed:
                AddLine($"{Stamp()} 失败 {NodeRef(observation.Node)} · {observation.Detail}");
                _nodeObserved?.Invoke(observation.Node);
                break;

            case ExecutionObservationKind.NodeRetried:
                AddLine($"{Stamp()} 重试 {NodeRef(observation.Node)} · {observation.Detail}");
                _nodeObserved?.Invoke(observation.Node);
                break;

            case ExecutionObservationKind.RunEnded:
                Finish(observation.Elapsed);
                return;
        }

        OnPropertyChanged(nameof(ProgressText));
    }

    private void Finish(TimeSpan elapsed)
    {
        if (!IsRunning) return;

        var outcome = _context?.Outcome ?? RunOutcome.Unknown;
        _state = outcome switch
        {
            RunOutcome.Failed => WorkflowRunState.Failed,
            RunOutcome.Cancelled => WorkflowRunState.Cancelled,
            RunOutcome.Completed => WorkflowRunState.Completed,
            _ => _context?.Status == "Completed" ? WorkflowRunState.Completed : WorkflowRunState.Cancelled,
        };

        _endedAt = DateTime.Now;
        var status = _context?.Status ?? _state.ToString();
        // Ends the page the way the run really ended: a cancellation and a failure both read "Stopped" in Status.
        AddLine($"{Stamp()} 运行结束 · {status} · 第 {Math.Max(1, _attempt)} 趟 · {Duration(elapsed)} · 驱动 {_nodesDriven} 次");
        NotifyState();
    }

    private void AddLine(string line)
    {
        Lines.Add(line);
        _lineSink?.Invoke(this, line);
    }

    private void Post(Action action)
    {
        if (_ui is null || ReferenceEquals(_ui, SynchronizationContext.Current))
        {
            action();
            return;
        }

        _ui.Post(_ => action(), null);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

    private static string Duration(TimeSpan elapsed)
        => elapsed.TotalSeconds < 60 ? $"{elapsed.TotalSeconds:0.0} s" : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";

    // 节点在页面上叫它自己的标题；没有标题的类型退回类型名，去掉 ViewModel 后缀。
    private static string NodeRef(IWorkflowNodeViewModel? node)
    {
        if (node is null) return "运行";

        var order = (node as ICompileTimeAware)?.CompileContext?.Order ?? -1;
        var prefix = order >= 0 ? $"#{order + 1} " : string.Empty;
        return prefix + WorkflowElementCatalog.Describe(node);
    }
}

/// <summary>
/// The host-facing list of compile and run tasks: what was compiled, what is running, what the Agent started.
/// </summary>
/// <remarks>
/// <para>
/// Two sources feed it and they meet on the session rather than on a tool result. A manual run's session is
/// configured by <see cref="Demo.Workflow.WorkflowDemoSession"/>; an Agent run's session goes through the scope's
/// session configuration, which the demo points at the same method. Either way <see cref="AttachSession"/> wraps
/// the session's observer and log writer, so the row sees exactly what the engine does — no polling, no parsing of
/// tool results.
/// </para>
/// <para>
/// Compile-only tool calls have no session. Those are read from the tool result's own outline, which is the same
/// shape the Agent was given, so the compile row and the compiled-structure panel cannot disagree.
/// </para>
/// <para>
/// The selected row is mirrored into the tree's <c>ExecutionLog</c>, the collection every demo already binds to its
/// output page. That keeps the six existing output panes working while the richer manager lives beside them.
/// </para>
/// </remarks>
public sealed class WorkflowRunCatalog : INotifyPropertyChanged
{
    private readonly Func<ObservableCollection<string>> _outputProvider;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly Timer _clock;
    private WorkflowRunTaskViewModel? _selected;
    private volatile bool _hasRunning;

    /// <summary>Creates a catalog that mirrors its selected task into the tree's execution log.</summary>
    /// <param name="output">Returns the collection to mirror into — read per write, because deserialization may
    /// replace the tree's <c>ExecutionLog</c> property with a fresh collection.</param>
    public WorkflowRunCatalog(Func<ObservableCollection<string>> output)
    {
        _outputProvider = output ?? throw new ArgumentNullException(nameof(output));
        _clock = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a row is added, removed, selected or changes state.</summary>
    public event EventHandler? Changed;

    /// <summary>The rows, most recent first.</summary>
    public ObservableCollection<WorkflowRunTaskViewModel> Tasks { get; } = [];

    /// <summary>The tree the manager belongs to; used to name the root of an Agent compile.</summary>
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>The row the output page follows.</summary>
    public WorkflowRunTaskViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!IsOnUiThread)
            {
                Post(() => Selected = value);
                return;
            }

            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            Mirror();
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasSelection));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether a row is selected (the output page has something to show).</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>Whether any row is running.</summary>
    public bool AnyRunning => Tasks.Any(task => task.IsRunning);

    /// <summary>Whether the manager has no rows at all.</summary>
    public bool IsEmpty => Tasks.Count == 0;

    /// <summary>Whether the manager has at least one row.</summary>
    public bool HasTasks => Tasks.Count > 0;

    /// <summary>A one-line count for the panel header.</summary>
    public string Summary
    {
        get
        {
            var running = Tasks.Count(task => task.IsRunning);
            var compiled = Tasks.Count(task => task.State == WorkflowRunState.Compiled);
            var finished = Tasks.Count - running - compiled;
            return $"运行中 {running} · 已编译 {compiled} · 已结束 {finished} · 共 {Tasks.Count}";
        }
    }

    /// <summary>Adds or replaces a compile row for a controller the host compiled itself.</summary>
    /// <param name="key">Stable identity of the controller, so a re-compile updates one row.</param>
    /// <param name="rootLabel">The controller's label on the canvas.</param>
    /// <param name="outline">The compiled graph's flat outline.</param>
    /// <param name="start">How the row may start the graph again, or <see langword="null"/> when it may not.</param>
    public void NoteManualCompile(
        string key,
        string rootLabel,
        IReadOnlyList<CompiledOutlineRow> outline,
        Action? start)
    {
        Post(() =>
        {
            var lines = new List<string> { $"{Stamp()} 编译完成 · {rootLabel} · {outline.Count} 段" };
            foreach (var row in outline)
            {
                var indent = new string(' ', Math.Max(0, row.Depth) * 2);
                lines.Add($"{Stamp()}   {indent}· {row.Kind} · {row.Label}");
            }

            var task = new WorkflowRunTaskViewModel(
                WorkflowRunSource.Manual, rootLabel, key, outline.Count, _ui, OnLine, null);
            task.SetCompiled(lines, start);
            AddRow(task);
        });
    }

    /// <summary>Reads the two compile tools' results into a compile row. Every other tool is ignored.</summary>
    /// <param name="toolName">The Agent tool that just succeeded.</param>
    /// <param name="result">Its JSON result.</param>
    public void NoteAgentToolCall(string toolName, string result)
    {
        if (toolName is not ("CompileWorkflow" or "CompileNodeResult")) return;

        VeloxJsonObject? document;
        try { document = VeloxJsonValue.Parse(result) as VeloxJsonObject; }
        catch (FormatException) { return; }

        if (document?["status"]?.Materialize() as string != "ok") return;

        var role = document["role"]?.Materialize() as string ?? "Root";
        var entries = document["entries"] as VeloxJsonArray;
        var segmentCount = entries?.Count ?? 0;
        var rootIndex = FirstOrderZero(document["nodeOrders"] as VeloxJsonArray);
        var root = RootAt(rootIndex) ?? (role == "Terminal" ? "目标节点结果" : "Agent 编译");
        var label = role == "Terminal" ? $"Agent 编译 · {root}（结果）" : $"Agent 编译 · {root}";
        var key = $"agent-compile:{role}:{root}";

        var lines = new List<string> { $"{Stamp()} Agent 编译完成 · {label} · {segmentCount} 段" };
        if (entries is not null) AppendEntries(lines, entries);

        // A root compile usually starts at a controller, and that controller's own Run is the same path the
        // canvas's Run button takes. A terminal cone, or a root that is not a controller, only reports its outline.
        Action? start = null;
        if (role == "Root" && ControllerAt(rootIndex) is { } controller)
            start = () => controller.RunCommand.Execute(null);

        Post(() =>
        {
            var task = new WorkflowRunTaskViewModel(
                WorkflowRunSource.Agent, label, key, segmentCount, _ui, OnLine, null);
            task.SetCompiled(lines, start);
            AddRow(task);

            // 编译身份可能落在任意节点上；这是低频事件，整页刷一次即可。
            (Tree as TreeViewModel)?.RefreshElementDetails();
        });
    }

    /// <summary>
    /// Binds a row to the session that is about to drive it, and wraps the session's observer and log writer so the
    /// row sees what the engine does.
    /// </summary>
    /// <param name="context">The run's session.</param>
    /// <param name="source">Who started it.</param>
    /// <param name="reuseKey">The compile row this run replaces, or <see langword="null"/> for a new row.</param>
    /// <param name="rootLabel">The root node's label.</param>
    /// <param name="segments">How many segments the graph has, when known.</param>
    /// <param name="stop">How to ask the run to stop, or <see langword="null"/> when it cannot be stopped from here.</param>
    /// <param name="start">How to start it again after it finishes, or <see langword="null"/>.</param>
    public void AttachSession(
        RuntimeContext context,
        WorkflowRunSource source,
        string? reuseKey,
        string rootLabel,
        int segments,
        Func<bool>? stop,
        Action? start)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var key = reuseKey ?? $"run:{context.Uid:N}";
        var task = new WorkflowRunTaskViewModel(source, rootLabel, key, segments, _ui, OnLine, ObserveNode);
        task.Attach(context, stop, start);

        // 观察者与写入器都是引擎在驱动线程上直接调的：先接上，再让引擎开跑，一行都不漏。
        var innerObserver = context.Observer;
        context.Observer = innerObserver is null
            ? new DelegateExecutionObserver(task.OnObserved)
            : new DelegateExecutionObserver(observation =>
            {
                try { innerObserver.OnObservedAsync(observation, CancellationToken.None).GetAwaiter().GetResult(); }
                finally { task.OnObserved(observation); }
            });

        var innerWriter = context.LogWriter;
        context.LogWriter = new DelegateLogWriter(line =>
        {
            // 文件写者可能在换树时已经被释放；行仍要进输出页，所以 finally 里喂给行。
            try { innerWriter?.Write(line); }
            finally { task.AppendLog(line); }
        });

        Post(() => AddRow(task));
    }

    /// <summary>Removes finished rows, leaving the running ones alone.</summary>
    public void ClearFinished()
    {
        Post(() =>
        {
            // 只清真正结束的三类；已编译未运行的行还在等一次 Run，不该被“清除已完成”顺手删掉。
            foreach (var task in Tasks.Where(task => task.State
                         is WorkflowRunState.Completed or WorkflowRunState.Cancelled or WorkflowRunState.Failed).ToList())
            {
                task.PropertyChanged -= OnTaskPropertyChanged;
                Tasks.Remove(task);
            }

            if (_selected is not null && !Tasks.Contains(_selected))
                Selected = Tasks.FirstOrDefault();

            NotifyAggregates();
        });
    }

    /// <summary>Drops every row, for a tree that is being replaced.</summary>
    public void Clear()
    {
        Post(() =>
        {
            foreach (var task in Tasks) task.PropertyChanged -= OnTaskPropertyChanged;
            Tasks.Clear();
            Selected = null;
            NotifyAggregates();
        });
    }

    /// <summary>Removes one row when it is not running.</summary>
    public void Remove(WorkflowRunTaskViewModel task)
    {
        if (!task.CanRemove) return;
        Post(() =>
        {
            task.PropertyChanged -= OnTaskPropertyChanged;
            Tasks.Remove(task);
            if (ReferenceEquals(_selected, task)) Selected = Tasks.FirstOrDefault();
            NotifyAggregates();
        });
    }

    private void AddRow(WorkflowRunTaskViewModel task)
    {
        var existing = Tasks.FirstOrDefault(row => row.Key == task.Key);
        if (existing is not null)
        {
            existing.PropertyChanged -= OnTaskPropertyChanged;
            Tasks.Remove(existing);
        }

        Tasks.Insert(0, task);
        task.PropertyChanged += OnTaskPropertyChanged;
        Selected = task;
        NotifyAggregates();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e) => Post(NotifyAggregates);

    private void OnLine(WorkflowRunTaskViewModel task, string line)
    {
        if (ReferenceEquals(task, _selected)) Output.Add(line);
    }

    // 被驱动的节点可能在属性页上正被描述；只让这一行重读，而不是让每一行都收到通知。
    private void ObserveNode(IWorkflowNodeViewModel? node)
        => (Tree as TreeViewModel)?.RefreshElementDetails(node);

    private void Mirror()
    {
        var output = Output;
        output.Clear();
        if (_selected is null) return;
        foreach (var line in _selected.Lines) output.Add(line);
    }

    // 读的时候才取集合：反序列化可能把 TreeViewModel.ExecutionLog 换成新的一个实例。
    private ObservableCollection<string> Output => _outputProvider();

    private void NotifyAggregates()
    {
        _hasRunning = Tasks.Any(task => task.IsRunning);
        _clock.Change(_hasRunning ? 250 : Timeout.Infinite, _hasRunning ? 250 : Timeout.Infinite);

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AnyRunning));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasTasks));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        if (!_hasRunning) return;
        Post(() =>
        {
            foreach (var task in Tasks) task.TickElapsed();
        });
    }

    private bool IsOnUiThread => _ui is null || ReferenceEquals(_ui, SynchronizationContext.Current);

    private void Post(Action action)
    {
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _ui!.Post(_ => action(), null);
    }

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string? RootAt(int index)
    {
        if (Tree is null || index < 0 || index >= Tree.Nodes.Count) return null;
        return WorkflowElementCatalog.Describe(Tree.Nodes[index]);
    }

    private ControllerViewModel? ControllerAt(int index)
    {
        if (Tree is null || index < 0 || index >= Tree.Nodes.Count) return null;
        return Tree.Nodes[index] as ControllerViewModel;
    }

    // nodeOrders 里 order 0 的那个节点就是这次编译的入口：Root 角色下通常就是控制器。
    private static int FirstOrderZero(VeloxJsonArray? orders)
    {
        if (orders is null) return -1;
        foreach (var item in orders.OfType<VeloxJsonObject>())
        {
            if ((item["order"]?.Materialize() as long? ?? -1) != 0) continue;
            return (int)(item["i"]?.Materialize() as long? ?? -1);
        }
        return -1;
    }

    private static void AppendEntries(List<string> lines, VeloxJsonArray entries)
    {
        foreach (var item in entries.OfType<VeloxJsonObject>())
        {
            var depth = (int)(item["depth"]?.Materialize() as long? ?? 0);
            var indent = new string(' ', Math.Max(0, depth) * 2);
            var kind = item["type"]?.Materialize() as string ?? "Segment";
            var detail = kind switch
            {
                "Execute" => Names(item["nodes"] as VeloxJsonArray),
                "Branch" => $"{item["router"]?.Materialize() as string ?? "router"} · " +
                            ((item["isDynamic"]?.Materialize() as bool? ?? false) ? "动态" : "静态"),
                "Parallel" => $"{item["branches"]?.Materialize() ?? 0} 分支",
                _ => string.Empty,
            };
            lines.Add($"{Stamp()}   {indent}· {kind}{(detail.Length > 0 ? " · " + detail : string.Empty)}");
        }
    }

    private static string Names(VeloxJsonArray? nodes)
    {
        if (nodes is null) return string.Empty;
        var names = nodes.Select(node => (node.Materialize() as string) ?? string.Empty)
                         .Where(name => name.Length > 0)
                         .Select(name => name.Replace("ViewModel", string.Empty));
        return string.Join(" → ", names);
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");
}
