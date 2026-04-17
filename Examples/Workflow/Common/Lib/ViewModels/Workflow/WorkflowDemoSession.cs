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
        tree.Layout.OriginSize = new Size(2400, 1200);

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
            Anchor = new Anchor(60, 360, 0),
            SeedPayload = "demo-request-chain",
        };

        var loadSeed = CreateNode("Load Seed", 900, 280, 120);
        var warmCache = CreateNode("Warm Cache", 1400, 280, 470);
        var branchA = CreateNode("Branch A", 2200, 640, 20);
        var branchB = CreateNode("Branch B", 1200, 640, 250);
        var branchC = CreateNode("Branch C", 1800, 640, 480);
        var joinA = CreateNode("Join A", 1000, 1000, 140);
        var joinB = CreateNode("Join B", 1600, 1000, 390);
        var aggregate = CreateNode("Aggregate", 2600, 1360, 210);
        var finalize = CreateNode("Finalize", 700, 1720, 210);

        var boolSelector = new BoolSelectorNodeViewModel
        {
            Title = "Bool Gate",
            Condition = true,
            Size = new Size(260, 200),
            Anchor = new Anchor(640, 720, 0),
        };

        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Method Router",
            Size = new Size(280, 380),
            Anchor = new Anchor(1360, 500, 0),
        };
        enumSelector.EnumType = typeof(NetworkRequestMethod);
        enumSelector.SelectedValue = NetworkRequestMethod.Get;

        foreach (var node in new IWorkflowNodeViewModel[]
        {
            controller,
            loadSeed,
            warmCache,
            branchA,
            branchB,
            branchC,
            joinA,
            joinB,
            aggregate,
            finalize,
            boolSelector,
            enumSelector,
        })
        {
            helper.CreateNode(node);
        }

        controller.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        loadSeed.InputSlot = CreateInputSlot();
        loadSeed.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        warmCache.InputSlot = CreateInputSlot();
        warmCache.OutputSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        branchA.InputSlot = CreateInputSlot();
        branchA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        branchB.InputSlot = CreateInputSlot();
        branchB.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        branchC.InputSlot = CreateInputSlot();
        branchC.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        joinA.InputSlot = CreateInputSlot();
        joinA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        joinB.InputSlot = CreateInputSlot();
        joinB.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        aggregate.InputSlot = CreateInputSlot(SlotChannel.MultipleSources);
        aggregate.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        finalize.InputSlot = CreateInputSlot();

        boolSelector.InputSlot = CreateInputSlot();
        boolSelector.TrueSlot = CreateOutputSlot(SlotChannel.MultipleTargets);
        boolSelector.FalseSlot = CreateOutputSlot(SlotChannel.MultipleTargets);

        enumSelector.InputSlot = CreateInputSlot();

        Connect(tree, controller.OutputSlot!, loadSeed.InputSlot!);
        Connect(tree, controller.OutputSlot!, warmCache.InputSlot!);
        Connect(tree, loadSeed.OutputSlot!, branchA.InputSlot!);
        Connect(tree, loadSeed.OutputSlot!, branchB.InputSlot!);
        Connect(tree, loadSeed.OutputSlot!, branchC.InputSlot!);
        Connect(tree, warmCache.OutputSlot!, joinA.InputSlot!);
        Connect(tree, warmCache.OutputSlot!, joinB.InputSlot!);
        Connect(tree, branchA.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, branchB.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, branchC.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, joinA.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, joinB.OutputSlot!, aggregate.InputSlot!);
        Connect(tree, aggregate.OutputSlot!, finalize.InputSlot!);

        // Bool selector: warmCache → boolSelector, true → joinA, false → joinB
        Connect(tree, warmCache.OutputSlot!, boolSelector.InputSlot!);
        Connect(tree, boolSelector.TrueSlot!, joinA.InputSlot!);
        Connect(tree, boolSelector.FalseSlot!, joinB.InputSlot!);

        // Enum selector: aggregate → enumSelector, Get output → finalize
        Connect(tree, aggregate.OutputSlot!, enumSelector.InputSlot!);
        var getSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Get);
        if (getSlot is not null)
            Connect(tree, getSlot, finalize.InputSlot!);

        helper.ClearHistory();
        return new WorkflowDemoSession(tree, controller, [loadSeed, warmCache, branchA, branchB, branchC, joinA, joinB, aggregate, finalize]);
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
