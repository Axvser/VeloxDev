using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// The demo is **multiple examples on one large canvas**; the canvas origin (0,0) is at the top-left, examples
/// are laid out row by row without overlapping. Each example has its own initiator node (Controller).
///
/// Current examples (the Compiler mechanism is being rewritten from scratch, so branch selectors are ordinary
///   nodes for now and no longer handle routing): A. Node-graph capability — base graph: Controller → worker →
///   Bool/Enum selector (ordinary node) → branch → join → handler; R. In-chain fallback — a node implementing
///
///   IRedirectable in a linear chain falls back to an earlier compile state from the runtime context.
/// Layout: examples start at (0,0) and fill row by row; links flow left-to-right / top-to-bottom where possible.
/// Method Router stays on NetworkRequestMethod (Get/Post/Put/Delete); compile mode defaults to Dynamic (keeps all branches, routes at runtime by the selected value), switchable to Static (compile only the selected branch) via the card dropdown.
/// </summary>
public sealed class WorkflowDemoSession
{
    private WorkflowDemoSession(TreeViewModel tree, ControllerViewModel primary,
        IReadOnlyList<ControllerViewModel> controllers, IEnumerable<NodeViewModel> nodes)
    {
        Tree = tree;
        Controller = primary;
        Controllers = controllers;
        Nodes = [.. nodes];
    }

    public TreeViewModel Tree { get; }
    /// <summary>Primary controller (example A: node-graph capability), for backward compatibility / single-graph hosts.</summary>
    public ControllerViewModel Controller { get; }
    /// <summary>Each example's own initiator node (Controller).</summary>
    public IReadOnlyList<ControllerViewModel> Controllers { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; }

    public static WorkflowDemoSession Create()
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

        // ─────────────────────────────────────────────────────────────────────
        // Example A: node-graph capability — Controller → worker → Bool/Enum selector (ordinary node) → branch + join + handler
        // ─────────────────────────────────────────────────────────────────────
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

        // Note: the controller was already created by NewController — never CreateNode it again. Re-creating would Delete + rebuild and clear the Parent of its slots.
        foreach (var n in new IWorkflowNodeViewModel[]
        {
            loadSeed, boolSelector, hot, cold, aggregate, enumSelector,
            handleGet, handlePost, handlePut, handleDelete, finalize,
        })
            helper.CreateNode(n);   // register the node first (installs the helper), then create slots

        // Configure channels with the generator-preset default InputSlot/OutputSlot + SetChannelCommand (do not replace them with new ones, to avoid ghost undo/redo entries)
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

        // ─────────────────────────────────────────────────────────────────────
        // Example R: in-chain fallback — Controller → Start → Prepare → RedirectGate → Process → Sink.
        // RedirectGate implements IRedirectable: the first FailCount passes fall back RedirectBackSteps (to Prepare)
        // and re-execute, then pass through; the target is a visible mid-chain checkpoint so retries are clear.
        // ─────────────────────────────────────────────────────────────────────
        var redirectController = NewController("redirect-chain", 60, 1300);
        var rStart = CreateNode("Start", 500, 400, 1300, priority: 1);
        var rPrepare = CreateNode("Prepare", 500, 800, 1350, priority: 2);
        var redirectGate = new RedirectGateNodeViewModel
        {
            Title = "Redirect Gate",
            Size = new Size(300, 260),
            Anchor = new Anchor(1200, 1300, 0),
            FailCount = 2,
            RedirectBackSteps = 1,
        };
        var rProcess = CreateNode("Process", 600, 1600, 1400, priority: 3);
        var rSink = CreateNode("Redirect Sink", 400, 2050, 1650, priority: 4);

        foreach (var n in new IWorkflowNodeViewModel[] { rStart, rPrepare, redirectGate, rProcess, rSink })
            helper.CreateNode(n);

        SetChannel(redirectController.OutputSlot, SlotChannel.OneTarget);
        SetChannel(rStart.InputSlot, SlotChannel.OneSource);
        SetChannel(rStart.OutputSlot, SlotChannel.OneTarget);
        SetChannel(rPrepare.InputSlot, SlotChannel.OneSource);
        SetChannel(rPrepare.OutputSlot, SlotChannel.OneTarget);
        SetChannel(redirectGate.InputSlot, SlotChannel.OneSource);
        SetChannel(redirectGate.OutputSlot, SlotChannel.OneTarget);
        SetChannel(rProcess.InputSlot, SlotChannel.OneSource);
        SetChannel(rProcess.OutputSlot, SlotChannel.OneTarget);
        SetChannel(rSink.InputSlot, SlotChannel.OneSource);

        Connect(tree, redirectController.OutputSlot!, rStart.InputSlot!);
        Connect(tree, rStart.OutputSlot!, rPrepare.InputSlot!);
        Connect(tree, rPrepare.OutputSlot!, redirectGate.InputSlot!);
        Connect(tree, redirectGate.OutputSlot!, rProcess.InputSlot!);
        Connect(tree, rProcess.OutputSlot!, rSink.InputSlot!);

        controllers.Add(redirectController);
        allNodes.AddRange([rStart, rPrepare, redirectGate, rProcess, rSink]);

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
