using Demo.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM.Serialization;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// The demo is a single compiled voltage-analysis chain on one canvas; the canvas origin (0,0) is at the top-left.
///
///   Controller → Ticker → Source Selector (STATIC: Synthetic) → Generate Dataset (the SampleFile option is pruned
///     at compile time, so the node behind it stops at Order = -1) → [Numeric Stats, Frequency Dist, Anomaly Scan]
///     → join Merge Report (GroupData) → Publish (its first delivery fails; the retry policy gets it through)
///     → Audit (rejects the quick pass: Error + redirect back to the tick)
///     → Enum Selector (DYNAMIC: routes by the computed grade) → [Report High, Report Low, Report Zero].
///
/// One press of Run therefore shows, in one graph: a chain, a compile-time-pruned static branch, a fan-out whose
/// branches interleave, a join that aggregates by source node, two warnings that do not stop anything, an error
/// that asks for a re-run instead of ending the run, a retry, and a second pass — with the log going to a file, the
/// run's timeline summarized by an observer, its failures collected as records, its place checkpointed, and a
/// compensator ready for a run that ends badly.
///
/// The Python nodes execute real scripts via CliWrap; their input/output ports are dynamic (PythonPortProvider).
/// </summary>
public sealed class WorkflowDemoSession : IDisposable
{
    // ── Built-in Python scripts (the "valuable" demo content) ──────────────────
    // Contract: python <script.py> <input.json> <output.json>; the script reads input.json and writes its
    // result JSON to output.json. Linear nodes receive the upstream output as-is; join nodes receive
    // { portName: upstreamOutput }.
    //
    // Two keys are added by the host to every object payload, and two more are read back from the result:
    //   _attempt  which pass over the graph this is (a redirect increments it, a retry does not)
    //   _drive    which time this node has been driven (a retry increments it)
    //   "warn"    a diagnostic line — reported, and the run carries on
    //   "error"   a failure — ends the run unless "redirect" names a node to fall back to
    private const string GenerateDatasetScript = """
        import json, sys, random, time
        # VeloxDev python node contract: argv[1] = input.json, argv[2] = output.json.
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        if int(d.get('_attempt', 1)) == 1:
            # First pass: a dozen flat samples, on purpose. Fast to produce, and the validator below is meant to
            # reject it -- which is what makes the redirect to this node happen.
            samples = [218.4, 221.0, 219.7, 220.6, 217.9, 222.3, 219.1, 220.0, 218.8, 221.5, 220.2, 219.4]
            note = 'quick pass: 12 flat samples'
        else:
            # The pass the redirect asked for: 40 samples with 3 injected anomalies, centred so that the grade the
            # merge computes differs from run to run -- a dynamic branch that always takes the same option is not
            # much of a demonstration. 175 → Low, 215 → Zero, 265 → High, each well clear of the thresholds.
            random.seed(int(time.time()))
            center = random.choice([175, 215, 265])
            samples = [round(random.gauss(center, 6), 2) for _ in range(40)]
            for _ in range(3):
                samples[random.randrange(len(samples))] = round(random.choice([150, 320, 55]), 2)
            note = f'full pass: 40 samples around {center} V with 3 anomalies'
        json.dump({"samples": samples, "count": len(samples), "unit": "V", "note": note,
                   "generated_at": time.time()}, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string LoadSampleFileScript = """
        import json, sys, os
        # The other option of the source selector. The shipped demo compiles that selector in Static mode with
        # Synthetic selected, so this node is pruned: it is on the canvas, and it is not in the compiled graph
        # (its compile Order is -1, which the node card shows as a stop sign).
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        path = os.path.join(os.path.dirname(os.path.abspath(sys.argv[2])), 'samples.json')
        if not os.path.exists(path):
            json.dump({'warn': f'no recorded sample set at {path}; falling back to 8 placeholder samples',
                       'samples': [220.0] * 8, 'count': 8, 'unit': 'V'}, open(sys.argv[2], 'w', encoding='utf-8'))
        else:
            json.dump(json.load(open(path, encoding='utf-8')), open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string NumericStatsScript = """
        import json, sys, statistics
        # stats: mean / median / stdev / extrema / p95 percentile
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        s = sorted(d.get('samples', []))
        p95 = s[int(len(s) * 0.95) - 1] if s else 0
        json.dump({
          'count': len(s),
          'mean': round(statistics.mean(s), 2) if s else 0,
          'median': round(statistics.median(s), 2) if s else 0,
          'stdev': round(statistics.pstdev(s), 2) if len(s) > 1 else 0,
          'min': min(s) if s else 0,
          'max': max(s) if s else 0,
          'p95': p95,
          'range': round(max(s) - min(s), 2) if s else 0
        }, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string FrequencyDistScript = """
        import json, sys
        from collections import Counter
        # binned histogram: Counter over voltage bands. A bin with nothing in it is worth saying out loud, not worth
        # stopping the run for -- that is the difference between a warning and an error.
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        bins = Counter()
        for v in d.get('samples', []):
            if v < 200: bins['<200'] += 1
            elif v < 220: bins['200-220'] += 1
            elif v <= 240: bins['220-240'] += 1
            else: bins['>240'] += 1
        hist = [{'bin': k, 'count': bins[k]} for k in ['<200', '200-220', '220-240', '>240']]
        out = {'histogram': hist, 'total': sum(bins.values())}
        empty = [h['bin'] for h in hist if h['count'] == 0]
        if empty:
            out['warn'] = 'empty histogram bin(s): ' + ', '.join(empty)
        json.dump(out, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string AnomalyScanScript = """
        import json, sys, statistics
        # Z-score anomaly detection: |z| > 2 is an outlier
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        s = d.get('samples', [])
        mean = statistics.mean(s) if s else 0
        sd = statistics.pstdev(s) if len(s) > 1 else 1.0
        if sd == 0: sd = 1.0
        anomalies = [{'index': i, 'value': v, 'z': round((v - mean) / sd, 2)} for i, v in enumerate(s) if abs((v - mean) / sd) > 2]
        out = {'anomalies': anomalies, 'count': len(anomalies), 'threshold_z': 2.0}
        if not anomalies:
            out['warn'] = f'no anomaly in {len(s)} samples: the set looks flat'
        json.dump(out, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string MergeReportScript = """
        import json, sys
        # join input: { portName: upstreamOutput }, e.g. {"stats": {...}, "dist": {...}, "anomalies": {...}}
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        stats = d.get('stats', {})
        dist = d.get('dist', {})
        anom = d.get('anomalies', {})
        mean = stats.get('mean', 0)
        # grade: mean > 240 → High; < 200 → Low; otherwise Zero
        grade = 'High' if mean > 240 else ('Low' if mean < 200 else 'Zero')
        report = {
          'summary': stats,
          'sample_count': stats.get('count', 0),
          'histogram': dist.get('histogram', []),
          'anomaly_count': anom.get('count', 0),
          'anomalies': anom.get('anomalies', []),
          'mean_voltage': mean,
          'grade': grade,
          # per-member 0/1 flags so the enum router can pick the branch by the computed grade
          'High': 1 if grade == 'High' else 0,
          'Low': 1 if grade == 'Low' else 0,
          'Zero': 1 if grade == 'Zero' else 0
        }
        json.dump(report, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string ValidateScript = """
        import json, sys
        # The audit, and the demo's IRedirectable node: a script cannot call the runtime, so it says what it wants
        # in its result -- "error" is the complaint, "redirect" names the node to fall back to. The engine re-runs
        # the whole graph from there, which is how this graph loops without a cycle in it.
        #
        # The generator is named by title, not by order: a node that sits inside a branch is still reachable from
        # here (the engine enters the branch and leaves the router alone, since the router is before the target),
        # so the run falls back to exactly the step that has to do the work again. It then sees _attempt == 2 and
        # produces the full set.
        #
        # It is the last node before the grade selector on purpose: a node that reports an error leaves null behind
        # it for the rest of that pass, so everything downstream of this one has to cope with an empty payload --
        # and the report nodes do, with a warning instead of a crash.
        d = json.load(open(sys.argv[1], encoding='utf-8')) or {}
        count = d.get('sample_count', 0)
        if count < 20:
            json.dump({'verified': False, 'sample_count': count,
                       'error': f'only {count} samples got published: too few to conclude anything from, run the pipeline again',
                       'redirect': 'Generate Dataset'}, open(sys.argv[2], 'w', encoding='utf-8'))
        else:
            # Pass the report through untouched: the selector below routes on the grade flags it carries.
            d['verified'] = True
            json.dump(d, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string ReportScript = """
        import json, sys, csv, os
        # final report: dump the stats summary to CSV (Python file/table handling).
        # The payload can be null: an audit that sent the run back leaves nothing for the rest of that pass, and a
        # script that crashed on it would turn one rejection into a page of stack traces.
        d = json.load(open(sys.argv[1], encoding='utf-8')) or {}
        if not d:
            json.dump({'warn': 'nothing to archive this pass: the audit sent the run back'},
                      open(sys.argv[2], 'w', encoding='utf-8'))
            sys.exit(0)
        grade = d.get('grade', 'unknown')
        # Beside this run's own scratch files: argv[2] is the node's output.json, which the host places under its
        # own pycache. A bare relative name would land in whatever directory the host process was started from --
        # the repository root, when a demo is launched from an IDE -- and dirty the working tree.
        path = os.path.join(os.path.dirname(os.path.abspath(sys.argv[2])), f"report_{grade.lower()}.csv")
        with open(path, 'w', newline='', encoding='utf-8') as f:
            w = csv.writer(f)
            w.writerow(['metric', 'value'])
            for k, v in d.get('summary', {}).items():
                w.writerow([k, v])
            w.writerow(['grade', grade])
            w.writerow(['anomaly_count', d.get('anomaly_count', 0)])
        json.dump({'saved_to': path, 'grade': grade, 'records': len(d.get('summary', {})) + 2}, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private const string PublishScript = """
        import json, sys, time
        # Delivery to a sink that is briefly unavailable. _drive counts how often this node has been driven in this
        # run: the first delivery fails, and the retry policy's second one gets through. (A retry does not change
        # _attempt -- it is not a new pass over the graph.)
        d = json.load(open(sys.argv[1], encoding='utf-8')) or {}
        if int(d.get('_drive', 1)) == 1:
            raise SystemExit('transient: the report sink refused the connection (simulated)')
        # Carries the report on: the audit behind this node reads it.
        d['published'] = True
        d['delivered_at'] = time.time()
        d['attempt'] = int(d.get('_attempt', 1))
        json.dump(d, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private WorkflowDemoSession(TreeViewModel tree, ControllerViewModel primary, string scratchDirectory)
    {
        Tree = tree;
        Controller = primary;
        LogPath = Path.Combine(scratchDirectory, "workflow.log");
        CheckpointPath = Path.Combine(scratchDirectory, "checkpoint.json");
        Checkpoints = new FileCheckpointStore(CheckpointPath);
        primary.CheckpointSource = ct => Checkpoints.LoadAsync(ct);

        // The run's capabilities are configured where the graph is built, so every platform demo's Run button shows
        // the same thing without a line of per-platform code.
        primary.ConfigureSessionWith(ConfigureRun);
    }

    public TreeViewModel Tree { get; }
    /// <summary>Primary controller (example C: compiled compute chain), for backward compatibility / single-graph hosts.</summary>
    public ControllerViewModel Controller { get; }

    /// <summary>Every failure the run recorded, as records rather than as lines — the demo's <c>IExecutionErrorSink</c>.</summary>
    public ObservableCollection<ExecutionError> Diagnostics { get; } = [];

    /// <summary>Where the run's log is appended (the writer is the complete record; the in-memory view is what the UI binds).</summary>
    public string LogPath { get; }

    /// <summary>Where the run's place is written after each node succeeds — the file an interrupted run resumes from.</summary>
    public string CheckpointPath { get; }

    /// <summary>
    /// The store the run checkpoints into. The controller's <c>ResumeCommand</c> reads it back and hands the
    /// checkpoint to <see cref="RuntimeEngine.RunAsync"/>, so a stopped run carries on from where it stopped.
    /// </summary>
    public IExecutionCheckpointStore Checkpoints { get; }

    /// <summary>Whether a checkpoint is on disk to carry on from — what a view enables its Resume control by.</summary>
    public bool HasCheckpoint => File.Exists(CheckpointPath);

    /// <summary>
    /// The pause point of a run: <see cref="ManualExecutionGate.Pause"/> holds it at the next node boundary,
    /// <see cref="ManualExecutionGate.Resume"/> lets it go. Releasable from any thread, including the one driving.
    /// </summary>
    public ManualExecutionGate Gate { get; } = new();

    private TextWriterLogWriter? _logWriter;   // 具体类型：接口上没有 Dispose，而文件是会话开的
    private int _nodesDriven;
    private int _retries;

    /// <summary>
    /// Closes the log this session opened. Who opened the file closes it — see <see cref="TextWriterLogWriter.For"/>
    /// — and the session is the one that did, so a host that swaps its tree should let the old session go.
    /// </summary>
    public void Dispose()
    {
        _logWriter?.Dispose();
        _logWriter = null;
    }

    /// <summary>
    /// Configures the session one Run drives. Nothing here is required by the engine — with all of it unset a run
    /// behaves exactly as it did before these capabilities existed — which is the point of showing them together:
    /// the same graph, one press, and the log says what each one did.
    /// </summary>
    private void ConfigureRun(RuntimeContext context)
    {
        // 一个 writer 给整个会话：谁开的文件谁 Dispose，而 demo 的会话活到进程结束。
        // 目录要先建出来：pycache 原本是第一个 python 节点跑起来才有的，而 writer 在运行之前就要开文件。
        _logWriter ??= TextWriterLogWriter.For(Scratch(LogPath));
        context.LogWriter = _logWriter;

        // 一次运行绝不带着上一次留下的暂停开始：门是宿主的手，而每次 Run 都是新的一轮。
        Gate.Resume();
        context.ExecutionGate = Gate;

        // The publish node's first delivery fails on purpose; this is what gets it through.
        context.RetryPolicy = new ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 200, factor: 2.0);
        context.Observer = new DelegateExecutionObserver(Observe);
        context.ErrorSink = new DelegateExecutionErrorSink(Diagnostics.Add);
        context.Compensation = new DelegateExecutionCompensation(
            c => Controller.RuntimeContext?.Log($"[Compensation] undo {NameOf(c.Node)} (attempt {c.Order})"));
        context.CheckpointStore = Checkpoints;
    }

    // 每条观察都进来，但只在收尾时写一行：一轮二三十个节点，逐条写会把日志本身淹掉。
    private void Observe(ExecutionObservation observation)
    {
        switch (observation.Kind)
        {
            case ExecutionObservationKind.NodeStarted:
                _nodesDriven++;
                break;
            case ExecutionObservationKind.NodeRetried:
                _retries++;
                break;
            case ExecutionObservationKind.RunEnded:
                var outcome = (Controller.RuntimeContext as RuntimeContext)?.Outcome;
                Controller.RuntimeContext?.Log(
                    $"[Observer] {_nodesDriven} nodes driven, {_retries} retried, pass {observation.Attempt}, " +
                    $"{observation.Elapsed.TotalSeconds:0.0}s, {outcome}");
                _nodesDriven = 0;
                _retries = 0;
                break;
        }
    }

    private static string NameOf(IWorkflowNodeViewModel node)
        => node is PythonScriptNodeViewModel python ? python.Title : node.GetType().Name;

    // 运行产物落在这个目录下：与 python 节点的临时文件同处，且不会脏了工作区（demo 从 IDE 启动时工作目录是仓库根）。
    private static string Scratch(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>Creates the demo's session.</summary>
    /// <param name="scratchDirectory">
    /// Where the run's log and checkpoint are written. Defaults to <c>pycache</c> beside the executable — next to
    /// the Python nodes' own scratch files, and never the working tree, which an IDE launch would otherwise dirty.
    /// Pass one when several runs must not share a place (a test suite, say).
    /// </param>
    public static WorkflowDemoSession Create(string? scratchDirectory = null)
    {
        scratchDirectory ??= Path.Combine(AppContext.BaseDirectory, "pycache");

        var tree = new TreeViewModel();
        tree.Layout.OriginSize = new Size(3600, 1000);
        var helper = tree.GetHelper();
        var controllers = new List<ControllerViewModel>();

        // Build one example: create the Controller (initiator node) and register it into the tree (its output slot is created after the example nodes' slots).
        ControllerViewModel NewController(string seed, double left, double top)
        {
            var c = new ControllerViewModel
            {
                Anchor = new Anchor(left, top, 0),
                SeedPayload = seed,
            };
            helper.CreateNode(c);
            return c;
        }

        // A Python node with explicitly-named dynamic ports (defaults are in1/out1).
        PythonScriptNodeViewModel NewPython(string title, string description, string script, double left, double top,
            string[] inputPorts, string[] outputPorts)
        {
            var p = new PythonScriptNodeViewModel
            {
                Title = title,
                Description = description,
                Script = script,
                Anchor = new Anchor(left, top, 0),
            };
            p.InputSlots.SetSelector(new PythonPortProvider { Ports = [.. inputPorts.Select(n => new PythonPort(n))] });
            p.OutputSlots.SetSelector(new PythonPortProvider { Ports = [.. outputPorts.Select(n => new PythonPort(n))] });
            helper.CreateNode(p);
            return p;
        }

        // ─────────────────────────────────────────────────────────────────────
        // One graph, the whole compiled-execution story. Left to right:
        //   Controller → Ticker → Source Selector (Static) → Generate Dataset → [3 analyzers] → Merge Report
        //     → Publish (retry) → Audit (redirect) → Enum Selector (Dynamic) → [3 reports].
        // ─────────────────────────────────────────────────────────────────────
        var controller = NewController("compute-chain", 60, 60);

        var timer = new TimerNodeViewModel
        {
            Title = "Ticker",
            Anchor = new Anchor(340, 110, 0),
        };
        helper.CreateNode(timer);

        // Compile-time-locked: only the selected option survives compilation, and the node behind the other one is
        // left at Order = -1 (the canvas shows it as a stop sign). That is the compiler's static pruning on display.
        var sourceSelector = new EnumSelectorNodeViewModel
        {
            Title = "Source Selector",
            Anchor = new Anchor(620, 100, 0),
        };
        sourceSelector.OutputSlots.SetSelector(typeof(DatasetSource));
        sourceSelector.SelectedValue = DatasetSource.Synthetic;
        sourceSelector.CompileMode = RouterCompileMode.Static;
        helper.CreateNode(sourceSelector);

        var generate = NewPython("Generate Dataset",
            "Produces the voltage samples. On the first pass it returns a deliberately flat 12-sample set; after the validator sends the run back here, the second pass produces the full 40 with 3 injected anomalies.",
            GenerateDatasetScript, 1000, 60, ["trigger"], ["dataset"]);
        var loadFile = NewPython("Load Sample File",
            "The source selector's other option: reads a recorded sample set. The shipped demo compiles the selector in Static mode with Synthetic selected, so this node is pruned (Order = -1) and never driven.",
            LoadSampleFileScript, 1000, 380, ["trigger"], ["dataset"]);
        var stats = NewPython("Numeric Stats",
            "Computes count / mean / median / stdev / min / max / p95 / range of the sample set.",
            NumericStatsScript, 1360, 10, ["dataset"], ["stats"]);
        var dist = NewPython("Frequency Dist",
            "Bins the samples into <200 / 200-220 / 220-240 / >240 histogram bands, and warns when a band is empty.",
            FrequencyDistScript, 1360, 320, ["dataset"], ["dist"]);
        var anom = NewPython("Anomaly Scan",
            "Z-score anomaly detection: flags samples with |z| > 2 as outliers, and warns when a flat set has none.",
            AnomalyScanScript, 1360, 620, ["dataset"], ["anomalies"]);
        var merge = NewPython("Merge Report",
            "Joins stats / histogram / anomalies and computes the voltage grade (High / Low / Zero).",
            MergeReportScript, 1720, 320, ["stats", "dist", "anomalies"], ["report"]);
        var audit = NewPython("Audit",
            "Audits what was just published. Under 20 samples it reports an error and names the node the run should fall back to — the engine re-runs the whole graph from there. This is the demo's IRedirectable node: a loop with no cycle in the graph.",
            ValidateScript, 2420, 320, ["report"], ["report"]);

        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Enum Selector",
            Anchor = new Anchor(2760, 320, 0),
        };
        enumSelector.OutputSlots.SetSelector(typeof(VoltageRange));
        enumSelector.SelectedValue = VoltageRange.Zero;   // default; Dynamic mode routes by the merge's computed grade
        helper.CreateNode(enumSelector);

        var reportHigh = NewPython("Report High", "Writes the stats summary of a High-grade result to a CSV file.",
            ReportScript, 3120, 10, ["report"], ["saved"]);
        var reportLow = NewPython("Report Low", "Writes the stats summary of a Low-grade result to a CSV file.",
            ReportScript, 3120, 320, ["report"], ["saved"]);
        var reportZero = NewPython("Report Zero", "Writes the stats summary of a Zero-grade result to a CSV file.",
            ReportScript, 3120, 620, ["report"], ["saved"]);

        // Ahead of the grade selector on purpose: a node that every option of a branch leads into is compiled into
        // one option only (the first the compiler walks), so a delivery step placed after the reports would simply
        // not run when the grade routed elsewhere.
        var publish = NewPython("Publish",
            "Delivers the merged report to a sink and carries it on for the audit. Its first attempt fails (a simulated transient refusal) and the retry policy gets it through — the log shows [Retry 1] and the node runs twice.",
            PublishScript, 2080, 320, ["report"], ["report"]);

        // Channels (standard SetChannelCommand path; never replace generator-preset slots).
        SetChannel(controller.OutputSlot, SlotChannel.OneTarget);
        SetChannel(timer.InputSlot, SlotChannel.OneSource);
        SetChannel(timer.OutputSlot, SlotChannel.OneTarget);
        SetChannel(sourceSelector.InputSlot, SlotChannel.OneSource);
        foreach (var slot in sourceSelector.OutputSlots.Items.Select(i => i.Slot))
            SetChannel(slot, SlotChannel.OneTarget);
        foreach (var source in new[] { generate, loadFile })
        {
            SetChannel(source.InputSlots.Items[0].Slot, SlotChannel.OneSource);
            SetChannel(source.OutputSlots.Items[0].Slot, SlotChannel.MultipleTargets);   // fan-out source
        }
        foreach (var a in new[] { stats, dist, anom })
        {
            SetChannel(a.InputSlots.Items[0].Slot, SlotChannel.OneSource);
            SetChannel(a.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        }
        foreach (var slot in merge.InputSlots.Items.Select(i => i.Slot))
            SetChannel(slot, SlotChannel.OneSource);
        SetChannel(merge.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        SetChannel(publish.InputSlots.Items[0].Slot, SlotChannel.OneSource);
        SetChannel(publish.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        SetChannel(audit.InputSlots.Items[0].Slot, SlotChannel.OneSource);
        SetChannel(audit.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        SetChannel(enumSelector.InputSlot, SlotChannel.OneSource);
        foreach (var r in new[] { reportHigh, reportLow, reportZero })
        {
            SetChannel(r.InputSlots.Items[0].Slot, SlotChannel.OneSource);
            SetChannel(r.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        }
        Connect(tree, controller.OutputSlot!, timer.InputSlot!);
        Connect(tree, timer.OutputSlot!, sourceSelector.InputSlot!);
        if (sourceSelector.GetSlotForValue(DatasetSource.Synthetic) is { } syntheticSlot)
            Connect(tree, syntheticSlot, generate.InputSlots.Items[0].Slot!);
        if (sourceSelector.GetSlotForValue(DatasetSource.SampleFile) is { } sampleSlot)
            Connect(tree, sampleSlot, loadFile.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, stats.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, dist.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, anom.InputSlots.Items[0].Slot!);
        Connect(tree, stats.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[0].Slot!);
        Connect(tree, dist.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[1].Slot!);
        Connect(tree, anom.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[2].Slot!);
        Connect(tree, merge.OutputSlots.Items[0].Slot!, publish.InputSlots.Items[0].Slot!);
        Connect(tree, publish.OutputSlots.Items[0].Slot!, audit.InputSlots.Items[0].Slot!);
        Connect(tree, audit.OutputSlots.Items[0].Slot!, enumSelector.InputSlot!);
        if (enumSelector.GetSlotForValue(VoltageRange.High) is { } highSlot) Connect(tree, highSlot, reportHigh.InputSlots.Items[0].Slot!);
        if (enumSelector.GetSlotForValue(VoltageRange.Low) is { } lowSlot) Connect(tree, lowSlot, reportLow.InputSlots.Items[0].Slot!);
        if (enumSelector.GetSlotForValue(VoltageRange.Zero) is { } zeroSlot) Connect(tree, zeroSlot, reportZero.InputSlots.Items[0].Slot!);

        controllers.Add(controller);

        return new WorkflowDemoSession(tree, controller, scratchDirectory);
    }

    /// <summary>
    /// Configures channels with the generator-preset default slot + SetChannelCommand.
    /// Never replace the default with a new SlotViewModel — that triggers the setter's Remove→DeleteCommand
    /// and produces ghost undo/redo entries. SetChannelCommand is a standard command path, non-undoable (no undo entry).
    /// </summary>
    private static void SetChannel(SlotViewModel slot, SlotChannel channel)
    {
        if (slot is null) return;
        slot.SetChannelCommand.Execute(channel);
    }

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }

    /// <summary>
    /// Creates a session from an already-deserialized <see cref="TreeViewModel"/>.
    /// The primary controller is the first <see cref="ControllerViewModel"/> in the tree.
    /// </summary>
    public static WorkflowDemoSession FromTree(TreeViewModel tree, string? scratchDirectory = null)
    {
        var controllers = tree.Nodes.OfType<ControllerViewModel>().ToList();
        var controller = controllers.FirstOrDefault() ?? new ControllerViewModel();
        return new WorkflowDemoSession(tree, controller, scratchDirectory ?? Path.Combine(AppContext.BaseDirectory, "pycache"));
    }
}
