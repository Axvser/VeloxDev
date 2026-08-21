using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// The demo is a single compiled voltage-analysis chain on one canvas; the canvas origin (0,0) is at the top-left.
///
///   Controller → Timer → Generate Dataset (plain-node fan-out) → [Numeric Stats, Frequency Dist, Anomaly Scan]
///     → join Merge Report (GroupData aggregation, computes a grade) → Enum Selector (routes by the grade)
///     → [Report High, Report Low, Report Zero] (CSV output).
/// The Python nodes execute real scripts via CliWrap; their input/output ports are dynamic (PythonPortProvider).
/// The plain-node fan-out and the join are compiled by the compiler's fan-out/join support.
/// </summary>
public sealed class WorkflowDemoSession
{
    // ── Built-in Python scripts (the "valuable" demo content) ──────────────────
    // Contract: python <script.py> <input.json> <output.json>; the script reads input.json and writes its
    // result JSON to output.json. Linear nodes receive the upstream output as-is; join nodes receive
    // { portName: upstreamOutput }.
    private const string GenerateDatasetScript = """
        import json, sys, random, time
        # VeloxDev python node contract: argv[1] = input.json, argv[2] = output.json.
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        random.seed(int(time.time()))
        # 40 samples around 220 V (normal distribution) with 3 random anomalies injected (short-circuit/overvoltage)
        samples = [round(random.gauss(220, 6), 2) for _ in range(40)]
        for _ in range(3):
            samples[random.randrange(len(samples))] = round(random.choice([150, 320, 55]), 2)
        json.dump({"samples": samples, "count": len(samples), "unit": "V", "generated_at": time.time()}, open(sys.argv[2], 'w', encoding='utf-8'))
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
        # binned histogram: Counter over voltage bands
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        bins = Counter()
        for v in d.get('samples', []):
            if v < 200: bins['<200'] += 1
            elif v < 220: bins['200-220'] += 1
            elif v <= 240: bins['220-240'] += 1
            else: bins['>240'] += 1
        hist = [{'bin': k, 'count': bins[k]} for k in ['<200', '200-220', '220-240', '>240']]
        json.dump({'histogram': hist, 'total': sum(bins.values())}, open(sys.argv[2], 'w', encoding='utf-8'))
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
        json.dump({'anomalies': anomalies, 'count': len(anomalies), 'threshold_z': 2.0}, open(sys.argv[2], 'w', encoding='utf-8'))
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

    private const string ReportScript = """
        import json, sys, csv
        # final report: dump the stats summary to CSV (Python file/table handling)
        d = json.load(open(sys.argv[1], encoding='utf-8'))
        grade = d.get('grade', 'unknown')
        path = f"report_{grade.lower()}.csv"
        with open(path, 'w', newline='', encoding='utf-8') as f:
            w = csv.writer(f)
            w.writerow(['metric', 'value'])
            for k, v in d.get('summary', {}).items():
                w.writerow([k, v])
            w.writerow(['grade', grade])
            w.writerow(['anomaly_count', d.get('anomaly_count', 0)])
        json.dump({'saved_to': path, 'grade': grade, 'records': len(d.get('summary', {})) + 2}, open(sys.argv[2], 'w', encoding='utf-8'))
        """;

    private WorkflowDemoSession(TreeViewModel tree, ControllerViewModel primary,
        IReadOnlyList<ControllerViewModel> controllers, IEnumerable<NodeViewModel> nodes)
    {
        Tree = tree;
        Controller = primary;
        Controllers = controllers;
        Nodes = [.. nodes];
    }

    public TreeViewModel Tree { get; }
    /// <summary>Primary controller (example C: compiled compute chain), for backward compatibility / single-graph hosts.</summary>
    public ControllerViewModel Controller { get; }
    /// <summary>Each example's own initiator node (Controller).</summary>
    public IReadOnlyList<ControllerViewModel> Controllers { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; }

    public static WorkflowDemoSession Create()
    {
        var tree = new TreeViewModel();
        tree.Layout.OriginSize = new Size(2400, 850);
        var helper = tree.GetHelper();
        var controllerSize = new Size(220, 340);
        var timerSize = new Size(200, 140);
        var pythonSize = new Size(280, 260);
        var enumSize = new Size(280, 380);
        var controllers = new List<ControllerViewModel>();

        // Build one example: create the Controller (initiator node) and register it into the tree (its output slot is created after the example nodes' slots).
        ControllerViewModel NewController(string seed, double left, double top)
        {
            var c = new ControllerViewModel
            {
                Size = controllerSize,
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
                Size = pythonSize,
                Anchor = new Anchor(left, top, 0),
            };
            p.InputSlots.SetSelector(new PythonPortProvider { Ports = [.. inputPorts.Select(n => new PythonPort(n))] });
            p.OutputSlots.SetSelector(new PythonPortProvider { Ports = [.. outputPorts.Select(n => new PythonPort(n))] });
            helper.CreateNode(p);
            return p;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Example: voltage-analysis chain.
        //   Controller → Timer → Generate Dataset (plain-node fan-out) → [Numeric Stats, Frequency Dist, Anomaly Scan]
        //     → join Merge Report (GroupData) → Enum Selector (routes by the computed grade) → [Report High/Low/Zero].
        // ─────────────────────────────────────────────────────────────────────
        var controller = NewController("compute-chain", 60, 60);

        var timer = new TimerNodeViewModel
        {
            Title = "Ticker",
            Size = timerSize,
            Anchor = new Anchor(380, 110, 0),
        };
        helper.CreateNode(timer);

        var generate = NewPython("Generate Dataset",
            "Generates 40 voltage samples (~220 V, normal distribution) with 3 injected anomalies (short-circuit / overvoltage).",
            GenerateDatasetScript, 700, 80, ["trigger"], ["dataset"]);
        var stats = NewPython("Numeric Stats",
            "Computes count / mean / median / stdev / min / max / p95 / range of the sample set.",
            NumericStatsScript, 1050, 10, ["dataset"], ["stats"]);
        var dist = NewPython("Frequency Dist",
            "Bins the samples into <200 / 200-220 / 220-240 / >240 histogram bands.",
            FrequencyDistScript, 1050, 280, ["dataset"], ["dist"]);
        var anom = NewPython("Anomaly Scan",
            "Z-score anomaly detection: flags samples with |z| > 2 as outliers.",
            AnomalyScanScript, 1050, 550, ["dataset"], ["anomalies"]);
        var merge = NewPython("Merge Report",
            "Joins stats / histogram / anomalies and computes the voltage grade (High / Low / Zero).",
            MergeReportScript, 1400, 280, ["stats", "dist", "anomalies"], ["report"]);

        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Enum Selector",
            Size = enumSize,
            Anchor = new Anchor(1750, 280, 0),
        };
        enumSelector.OutputSlots.SetSelector(typeof(VoltageRange));
        enumSelector.SelectedValue = VoltageRange.Zero;   // default; Dynamic mode routes by the merge's computed grade
        helper.CreateNode(enumSelector);

        var reportHigh = NewPython("Report High", "Writes the stats summary of a High-grade result to a CSV file.",
            ReportScript, 2100, 10, ["report"], ["saved"]);
        var reportLow = NewPython("Report Low", "Writes the stats summary of a Low-grade result to a CSV file.",
            ReportScript, 2100, 280, ["report"], ["saved"]);
        var reportZero = NewPython("Report Zero", "Writes the stats summary of a Zero-grade result to a CSV file.",
            ReportScript, 2100, 550, ["report"], ["saved"]);

        // Channels (standard SetChannelCommand path; never replace generator-preset slots).
        SetChannel(controller.OutputSlot, SlotChannel.OneTarget);
        SetChannel(timer.InputSlot, SlotChannel.OneSource);
        SetChannel(timer.OutputSlot, SlotChannel.OneTarget);
        SetChannel(generate.InputSlots.Items[0].Slot, SlotChannel.OneSource);
        SetChannel(generate.OutputSlots.Items[0].Slot, SlotChannel.MultipleTargets);   // fan-out source
        foreach (var a in new[] { stats, dist, anom })
        {
            SetChannel(a.InputSlots.Items[0].Slot, SlotChannel.OneSource);
            SetChannel(a.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        }
        foreach (var slot in merge.InputSlots.Items.Select(i => i.Slot))
            SetChannel(slot, SlotChannel.OneSource);
        SetChannel(merge.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        SetChannel(enumSelector.InputSlot, SlotChannel.OneSource);
        foreach (var r in new[] { reportHigh, reportLow, reportZero })
        {
            SetChannel(r.InputSlots.Items[0].Slot, SlotChannel.OneSource);
            SetChannel(r.OutputSlots.Items[0].Slot, SlotChannel.OneTarget);
        }

        Connect(tree, controller.OutputSlot!, timer.InputSlot!);
        Connect(tree, timer.OutputSlot!, generate.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, stats.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, dist.InputSlots.Items[0].Slot!);
        Connect(tree, generate.OutputSlots.Items[0].Slot!, anom.InputSlots.Items[0].Slot!);
        Connect(tree, stats.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[0].Slot!);
        Connect(tree, dist.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[1].Slot!);
        Connect(tree, anom.OutputSlots.Items[0].Slot!, merge.InputSlots.Items[2].Slot!);
        Connect(tree, merge.OutputSlots.Items[0].Slot!, enumSelector.InputSlot!);
        if (enumSelector.GetSlotForValue(VoltageRange.High) is { } highSlot) Connect(tree, highSlot, reportHigh.InputSlots.Items[0].Slot!);
        if (enumSelector.GetSlotForValue(VoltageRange.Low) is { } lowSlot) Connect(tree, lowSlot, reportLow.InputSlots.Items[0].Slot!);
        if (enumSelector.GetSlotForValue(VoltageRange.Zero) is { } zeroSlot) Connect(tree, zeroSlot, reportZero.InputSlots.Items[0].Slot!);

        controllers.Add(controller);

        return new WorkflowDemoSession(tree, controller, controllers, []);
    }

    /// <summary>
    /// Legacy example graph (base routing: worker → Bool selector → join → Enum selector → handlers → Finalize),
    /// kept as a **stable test fixture** only — the UI demo now shows <see cref="Create"/> (C + S). It exercises
    /// the same compiler paths (linear segments, static/dynamic branch pruning, join) without any external
    /// dependency (no Python), so framework-behavior tests stay hermetic.
    /// </summary>
    public static WorkflowDemoSession CreateLegacy()
    {
        var tree = new TreeViewModel();
        tree.Layout.OriginSize = new Size(3100, 1750);
        var helper = tree.GetHelper();
        var nodeSize = new Size(300, 260);
        var controllerSize = new Size(220, 340);
        var allNodes = new List<NodeViewModel>();
        var controllers = new List<ControllerViewModel>();

        NodeViewModel CreateNode(string title, int delayMilliseconds, double left, double top, int priority = 0)
            => new()
            {
                Title = title,
                DelayMilliseconds = delayMilliseconds,
                Size = nodeSize,
                Anchor = new Anchor(left, top, 0),
            };

        ControllerViewModel NewController(string seed, double left, double top)
        {
            var c = new ControllerViewModel
            {
                Size = controllerSize,
                Anchor = new Anchor(left, top, 0),
                SeedPayload = seed,
            };
            helper.CreateNode(c);
            return c;
        }

        var controller = NewController("demo-request-chain", 60, 60);

        var loadSeed = CreateNode("Load Seed", 900, 400, 80, priority: 1);
        var boolSelector = new BoolSelectorNodeViewModel
        {
            Title = "Cache Valid?",
            Condition = true,
            Size = new Size(260, 250),
            Anchor = new Anchor(760, 80, 0),
        };
        var hot = CreateNode("Hot Path", 800, 1120, 60, priority: 1);
        var cold = CreateNode("Cold Path", 1200, 1120, 380, priority: 2);
        var aggregate = CreateNode("Aggregate", 400, 1480, 220, priority: 0);
        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Method Router",
            Size = new Size(280, 380),
            Anchor = new Anchor(1860, 160, 0),
        };
        enumSelector.SelectedValue = NetworkRequestMethod.Get;
        var handleGet = CreateNode("GET Handler", 600, 2260, 40, priority: 1);
        var handlePost = CreateNode("POST Handler", 900, 2260, 320, priority: 2);
        var handlePut = CreateNode("PUT Handler", 700, 2260, 600, priority: 3);
        var handleDelete = CreateNode("DELETE Handler", 500, 2260, 880, priority: 4);
        var finalize = CreateNode("Finalize", 700, 2660, 460, priority: 0);

        foreach (var n in new IWorkflowNodeViewModel[]
        {
            loadSeed, boolSelector, hot, cold, aggregate, enumSelector,
            handleGet, handlePost, handlePut, handleDelete, finalize,
        })
            helper.CreateNode(n);

        SetChannel(controller.OutputSlot, SlotChannel.MultipleTargets);
        SetChannel(loadSeed.InputSlot, SlotChannel.OneSource);
        SetChannel(loadSeed.OutputSlot, SlotChannel.OneTarget);
        SetChannel(boolSelector.InputSlot, SlotChannel.OneSource);
        SetChannel(hot.InputSlot, SlotChannel.OneSource);
        SetChannel(hot.OutputSlot, SlotChannel.OneTarget);
        SetChannel(cold.InputSlot, SlotChannel.OneSource);
        SetChannel(cold.OutputSlot, SlotChannel.OneTarget);
        SetChannel(aggregate.InputSlot, SlotChannel.MultipleSources);
        SetChannel(aggregate.OutputSlot, SlotChannel.OneTarget);
        SetChannel(enumSelector.InputSlot, SlotChannel.OneSource);
        SetChannel(handleGet.InputSlot, SlotChannel.OneSource);
        SetChannel(handleGet.OutputSlot, SlotChannel.OneTarget);
        SetChannel(handlePost.InputSlot, SlotChannel.OneSource);
        SetChannel(handlePost.OutputSlot, SlotChannel.OneTarget);
        SetChannel(handlePut.InputSlot, SlotChannel.OneSource);
        SetChannel(handlePut.OutputSlot, SlotChannel.OneTarget);
        SetChannel(handleDelete.InputSlot, SlotChannel.OneSource);
        SetChannel(handleDelete.OutputSlot, SlotChannel.OneTarget);
        SetChannel(finalize.InputSlot, SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, loadSeed.InputSlot!);
        Connect(tree, loadSeed.OutputSlot!, boolSelector.InputSlot!);
        Connect(tree, boolSelector.TrueSlot!, hot.InputSlot!);
        Connect(tree, boolSelector.FalseSlot!, cold.InputSlot!);
        Connect(tree, hot.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, cold.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, aggregate.OutputSlot!, enumSelector.InputSlot!);
        var baseGet = enumSelector.GetSlotForValue(NetworkRequestMethod.Get);
        if (baseGet is not null) Connect(tree, baseGet, handleGet.InputSlot!);
        var basePost = enumSelector.GetSlotForValue(NetworkRequestMethod.Post);
        if (basePost is not null) Connect(tree, basePost, handlePost.InputSlot!);
        var basePut = enumSelector.GetSlotForValue(NetworkRequestMethod.Put);
        if (basePut is not null) Connect(tree, basePut, handlePut.InputSlot!);
        var baseDelete = enumSelector.GetSlotForValue(NetworkRequestMethod.Delete);
        if (baseDelete is not null) Connect(tree, baseDelete, handleDelete.InputSlot!);
        Connect(tree, handleGet.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handlePost.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handlePut.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handleDelete.OutputSlot!, finalize.InputSlot!);

        controllers.Add(controller);
        allNodes.AddRange([loadSeed, hot, cold, aggregate, handleGet, handlePost, handlePut, handleDelete, finalize]);

        return new WorkflowDemoSession(tree, controller, controllers, allNodes);
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
    public static WorkflowDemoSession FromTree(TreeViewModel tree)
    {
        var controllers = tree.Nodes.OfType<ControllerViewModel>().ToList();
        var controller = controllers.FirstOrDefault() ?? new ControllerViewModel();
        var nodes = tree.Nodes.OfType<NodeViewModel>();
        return new WorkflowDemoSession(tree, controller, controllers, nodes);
    }
}
