using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

public sealed class WorkflowDemoSession
{
    private WorkflowDemoSession(TreeViewModel tree, ControllerViewModel controller, IEnumerable<NodeViewModel> nodes)
    {
        Tree = tree;
        Controller = controller;
        Nodes = [.. nodes];
    }

    public TreeViewModel Tree { get; }
    public ControllerViewModel Controller { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; }

    public static WorkflowDemoSession Create()
    {
        var tree = new TreeViewModel();
        tree.Layout.OriginSize = new Size(3200, 1600);

        var helper = tree.GetHelper();
        var nodeSize = new Size(300, 260);
        var controllerSize = new Size(220, 170);

        NodeViewModel CreateNode(string title, int delayMilliseconds, double left, double top)
            => new()
            {
                Title = title,
                DelayMilliseconds = delayMilliseconds,
                Size = nodeSize,
                Anchor = new Anchor(left, top, 0),
            };

        var controller = new ControllerViewModel
        {
            Size = controllerSize,
            Anchor = new Anchor(60, 460, 0),
            SeedPayload = "demo-request-chain",
        };

        // Row 1: initial fan-out
        var loadSeed = CreateNode("Load Seed", 900, 340, 120);
        var warmCache = CreateNode("Warm Cache", 1400, 340, 620);

        // Row 2: Bool selector routes warmCache output
        var boolSelector = new BoolSelectorNodeViewModel
        {
            Title = "Cache Valid?",
            Condition = true,
            Size = new Size(260, 200),
            Anchor = new Anchor(680, 620, 0),
        };

        // Row 2: loadSeed fan-out
        var branchA = CreateNode("Branch A", 2200, 700, 20);
        var branchB = CreateNode("Branch B", 1200, 700, 280);

        // Row 3: bool selector targets
        var joinHot = CreateNode("Hot Path", 800, 1020, 520);
        var joinCold = CreateNode("Cold Path", 2000, 1020, 780);

        // Row 4: aggregate all branches
        var aggregate = CreateNode("Aggregate", 2600, 1380, 360);

        // Row 5: Enum selector routes aggregate output by HTTP method
        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Method Router",
            Size = new Size(280, 380),
            Anchor = new Anchor(1740, 200, 0),
        };
        enumSelector.SelectedValue = NetworkRequestMethod.Get;

        // Row 6: enum selector targets — one node per method to showcase routing
        var handleGet = CreateNode("GET Handler", 600, 2100, 20);
        var handlePost = CreateNode("POST Handler", 900, 2100, 300);
        var handlePut = CreateNode("PUT Handler", 700, 2100, 580);
        var handleDelete = CreateNode("DELETE Handler", 500, 2100, 860);

        // Row 7: final merge
        var finalize = CreateNode("Finalize", 700, 2480, 360);

        foreach (var node in new IWorkflowNodeViewModel[]
        {
            controller,
            loadSeed,
            warmCache,
            boolSelector,
            branchA,
            branchB,
            joinHot,
            joinCold,
            aggregate,
            enumSelector,
            handleGet,
            handlePost,
            handlePut,
            handleDelete,
            finalize,
        })
        {
            helper.CreateNode(node);
        }

        // --- Slots ---
        controller.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);

        loadSeed.InputSlot = CreateInputSlot();
        loadSeed.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);

        warmCache.InputSlot = CreateInputSlot();
        warmCache.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);

        boolSelector.InputSlot = CreateInputSlot();

        branchA.InputSlot = CreateInputSlot();
        branchA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        branchB.InputSlot = CreateInputSlot();
        branchB.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);

        joinHot.InputSlot = CreateInputSlot();
        joinHot.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        joinCold.InputSlot = CreateInputSlot();
        joinCold.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);

        aggregate.InputSlot = CreateInputSlot(SlotChannel.MultipleSources);
        aggregate.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);

        enumSelector.InputSlot = CreateInputSlot();

        handleGet.InputSlot = CreateInputSlot();
        handleGet.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        handlePost.InputSlot = CreateInputSlot();
        handlePost.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        handlePut.InputSlot = CreateInputSlot();
        handlePut.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        handleDelete.InputSlot = CreateInputSlot();
        handleDelete.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);

        finalize.InputSlot = CreateInputSlot(SlotChannel.MultipleSources);

        // --- Connections ---

        // Controller fans out to two seed paths
        Connect(tree, controller.OutputSlot!, loadSeed.InputSlot!);
        Connect(tree, controller.OutputSlot!, warmCache.InputSlot!);

        // loadSeed fans out to branchA / branchB
        Connect(tree, loadSeed.OutputSlot!, branchA.InputSlot!);
        Connect(tree, loadSeed.OutputSlot!, branchB.InputSlot!);

        // warmCache → Bool selector (sole route — showcases bool routing)
        Connect(tree, warmCache.OutputSlot!, boolSelector.InputSlot!);
        Connect(tree, boolSelector.TrueSlot!, joinHot.InputSlot!);
        Connect(tree, boolSelector.FalseSlot!, joinCold.InputSlot!);

        // All branches merge into aggregate
        Connect(tree, branchA.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, branchB.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, joinHot.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, joinCold.OutputSlot!, aggregate.InputSlot!);

        // Aggregate → Enum selector (sole route — showcases enum routing)
        Connect(tree, aggregate.OutputSlot!, enumSelector.InputSlot!);

        // Connect each enum output to its handler
        var getSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Get);
        if (getSlot is not null)
            Connect(tree, getSlot, handleGet.InputSlot!);

        var postSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Post);
        if (postSlot is not null)
            Connect(tree, postSlot, handlePost.InputSlot!);

        var putSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Put);
        if (putSlot is not null)
            Connect(tree, putSlot, handlePut.InputSlot!);

        var deleteSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Delete);
        if (deleteSlot is not null)
            Connect(tree, deleteSlot, handleDelete.InputSlot!);

        // All handlers merge into finalize
        Connect(tree, handleGet.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handlePost.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handlePut.OutputSlot!, finalize.InputSlot!);
        Connect(tree, handleDelete.OutputSlot!, finalize.InputSlot!);

        helper.ClearHistory();
        return new WorkflowDemoSession(tree, controller,
            [loadSeed, warmCache, branchA, branchB, joinHot, joinCold, aggregate, handleGet, handlePost, handlePut, handleDelete, finalize]);
    }

    private static SlotViewModel CreateInputSlot(SlotChannel channel = SlotChannel.OneSource)
        => new()
        {
            Channel = channel,
        };

    private static SlotViewModel CreateOutputSlot(SlotChannel channel)
        => new()
        {
            Channel = channel,
        };

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
