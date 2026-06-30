using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

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
        tree.Layout.OriginSize = new Size(3400, 3200);

        var helper = tree.GetHelper();
        var nodeSize = new Size(260, 130);
        var compNodeSize = new Size(260, 130);
        var controllerSize = new Size(220, 170);
        var compCtrlSize = new Size(200, 130);

        NodeViewModel CreateNode(string title, int delayMilliseconds, double left, double top, Size? size = null)
            => new()
            {
                Title = title,
                DelayMilliseconds = delayMilliseconds,
                Size = size ?? nodeSize,
                Anchor = new Anchor(left, top, 0),
            };

        ControllerViewModel CreateController(string label, double left, double top,
            CompileMode mode, CompileDirection dir, CompileScope scope, Size? size = null)
            => new()
            {
                Size = size ?? compCtrlSize,
                Anchor = new Anchor(left, top, 0),
                SeedPayload = label,
                CompileMode = mode,
                CompileDirection = dir,
                CompileScope = scope,
            };

        // ═══════════════════════════════════════════════════════════════════
        // Top Section — 编译运行示例链
        // ═══════════════════════════════════════════════════════════════════

        // ── Chain 1: BFS + Forward + FromNode ──
        var bfsCtrl = CreateController("BFS+Fwd+FromNode", 60, 40,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode);
        var bfsA = CreateNode("BFS Step 1", 600, 300, 40, compNodeSize);
        var bfsB = CreateNode("BFS Step 2", 400, 540, 40, compNodeSize);

        // ── Chain 2: DFS + Forward + FromNode ──
        var dfsCtrl = CreateController("DFS+Fwd+FromNode", 60, 220,
            CompileMode.DFS, CompileDirection.Forward, CompileScope.FromNode);
        var dfsA = CreateNode("DFS Step 1", 800, 300, 220, compNodeSize);
        var dfsB = CreateNode("DFS Step 2", 500, 540, 220, compNodeSize);

        // ── Chain 3: BFS + Forward + Omni ──
        // 两条独立链，Omni 自动发现两个入口节点
        var omniCtrl = CreateController("BFS+Fwd+Omni", 60, 440,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.Omni);
        var omniA = CreateNode("Omni Step 1", 600, 300, 440, compNodeSize);
        var omniB = CreateNode("Omni Step 2", 400, 540, 440, compNodeSize);
        // 独立侧链（与主链无连接），Omni 自动将其入口纳入编译
        var omniSideSeed = CreateNode("Side Seed", 300, 860, 440);
        var omniSideA = CreateNode("Side Step", 500, 1120, 440, compNodeSize);

        // ── Chain 4: BFS + Reverse + FromNode ──
        var revCtrl = CreateController("BFS+Rev+FromNode", 60, 620,
            CompileMode.BFS, CompileDirection.Reverse, CompileScope.FromNode);
        var revA = CreateNode("Rev Step 1", 600, 300, 620, compNodeSize);
        var revB = CreateNode("Rev Step 2", 400, 540, 620, compNodeSize);

        // ── Chain 5: BFS + Forward + FromNode + CycleHandling.Trim ──
        var trimCtrl = CreateController("BFS+Trim(Cycle)", 60, 800,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode);
        trimCtrl.CycleHandling = CycleHandling.Trim;
        var trimA = CreateNode("Trim Step 1", 600, 300, 800, compNodeSize);
        var trimB = CreateNode("Trim Step 2", 400, 540, 800, compNodeSize);

        // ═══════════════════════════════════════════════════════════════════
        // Bottom Section — 原 Demo 路径（整体下移 600px）
        // ═══════════════════════════════════════════════════════════════════

        var demoY = 1100; // 原起始 y 整体偏移

        var controller = new ControllerViewModel
        {
            Size = controllerSize,
            Anchor = new Anchor(60, 460 + demoY, 0),
            SeedPayload = "demo-request-chain",
        };

        // Row 1: initial fan-out
        var loadSeed = CreateNode("Load Seed", 900, 340, 120 + demoY);
        var warmCache = CreateNode("Warm Cache", 1400, 340, 620 + demoY);

        // Row 2: Bool selector routes warmCache output
        var boolSelector = new BoolSelectorNodeViewModel
        {
            Title = "Cache Valid?",
            Condition = true,
            Size = new Size(260, 200),
            Anchor = new Anchor(680, 620 + demoY, 0),
        };

        // Row 2: loadSeed fan-out
        var branchA = CreateNode("Branch A", 2200, 700, 20 + demoY);
        var branchB = CreateNode("Branch B", 1200, 700, 280 + demoY);

        // Row 3: bool selector targets
        var joinHot = CreateNode("Hot Path", 800, 1020, 520 + demoY);
        var joinCold = CreateNode("Cold Path", 2000, 1020, 780 + demoY);

        // Row 4: aggregate all branches
        var aggregate = CreateNode("Aggregate", 2600, 1380, 360 + demoY);

        // Row 5: Enum selector routes aggregate output by HTTP method
        var enumSelector = new EnumSelectorNodeViewModel
        {
            Title = "Method Router",
            Size = new Size(280, 380),
            Anchor = new Anchor(1740, 200 + demoY, 0),
        };
        enumSelector.SelectedValue = NetworkRequestMethod.Get;

        // Row 6: enum selector targets
        var handleGet = CreateNode("GET Handler", 600, 2100, 20 + demoY);
        var handlePost = CreateNode("POST Handler", 900, 2100, 300 + demoY);
        var handlePut = CreateNode("PUT Handler", 700, 2100, 580 + demoY);
        var handleDelete = CreateNode("DELETE Handler", 500, 2100, 860 + demoY);

        // Row 7: final merge
        var finalize = CreateNode("Finalize", 700, 2480, 360 + demoY);

        // ── Collect all nodes ──

        var allNodes = new IWorkflowNodeViewModel[]
        {
            // 编译示例链
            bfsCtrl, bfsA, bfsB,
            dfsCtrl, dfsA, dfsB,
            omniCtrl, omniA, omniB, omniSideSeed, omniSideA,
            revCtrl, revA, revB,
            trimCtrl, trimA, trimB,
            // 原 Demo 路径
            controller,
            loadSeed, warmCache, boolSelector, branchA, branchB,
            joinHot, joinCold, aggregate, enumSelector,
            handleGet, handlePost, handlePut, handleDelete, finalize,
        };

        foreach (var node in allNodes)
            helper.CreateNode(node);

        // ── Helpers ──

        SlotViewModel CreateInputSlot(SlotChannel channel = SlotChannel.OneSource)
            => new() { Channel = channel };

        SlotViewModel CreateOutputSlot(SlotChannel channel)
            => new() { Channel = channel };

        void ConnectSlots(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
        {
            tree.GetHelper().SendConnection(sender);
            tree.GetHelper().ReceiveConnection(receiver);
        }

        // ── Compiled demo chain connections ──

        // Chain 1: BFS
        bfsCtrl.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        bfsA.InputSlot = CreateInputSlot(); bfsA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        bfsB.InputSlot = CreateInputSlot();
        ConnectSlots(bfsCtrl.OutputSlot, bfsA.InputSlot);
        ConnectSlots(bfsA.OutputSlot, bfsB.InputSlot);

        // Chain 2: DFS
        dfsCtrl.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        dfsA.InputSlot = CreateInputSlot(); dfsA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        dfsB.InputSlot = CreateInputSlot();
        ConnectSlots(dfsCtrl.OutputSlot, dfsA.InputSlot);
        ConnectSlots(dfsA.OutputSlot, dfsB.InputSlot);

        // Chain 3: Omni — 主链 Controller → OmniA → OmniB，独立侧链 SideSeed → SideStep
        omniCtrl.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        omniA.InputSlot = CreateInputSlot(); omniA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        omniB.InputSlot = CreateInputSlot();
        ConnectSlots(omniCtrl.OutputSlot, omniA.InputSlot);
        ConnectSlots(omniA.OutputSlot, omniB.InputSlot);
        omniSideSeed.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        omniSideA.InputSlot = CreateInputSlot();
        ConnectSlots(omniSideSeed.OutputSlot, omniSideA.InputSlot);

        // Chain 4: Reverse
        revCtrl.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        revA.InputSlot = CreateInputSlot(); revA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        revB.InputSlot = CreateInputSlot();
        ConnectSlots(revCtrl.OutputSlot, revA.InputSlot);
        ConnectSlots(revA.OutputSlot, revB.InputSlot);

        // Chain 5: Trim
        trimCtrl.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        trimA.InputSlot = CreateInputSlot(); trimA.OutputSlot = CreateOutputSlot(SlotChannel.OneTarget);
        trimB.InputSlot = CreateInputSlot();
        ConnectSlots(trimCtrl.OutputSlot, trimA.InputSlot);
        ConnectSlots(trimA.OutputSlot, trimB.InputSlot);

        // ── Original demo connections ──

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

        // Controller fans out to two seed paths
        ConnectSlots(controller.OutputSlot!, loadSeed.InputSlot!);
        ConnectSlots(controller.OutputSlot!, warmCache.InputSlot!);

        // loadSeed fans out to branchA / branchB
        ConnectSlots(loadSeed.OutputSlot!, branchA.InputSlot!);
        ConnectSlots(loadSeed.OutputSlot!, branchB.InputSlot!);

        // warmCache → Bool selector
        ConnectSlots(warmCache.OutputSlot!, boolSelector.InputSlot!);
        ConnectSlots(boolSelector.TrueSlot!, joinHot.InputSlot!);
        ConnectSlots(boolSelector.FalseSlot!, joinCold.InputSlot!);

        // All branches merge into aggregate
        ConnectSlots(branchA.OutputSlot!, aggregate.InputSlot!);
        ConnectSlots(branchB.OutputSlot!, aggregate.InputSlot!);
        ConnectSlots(joinHot.OutputSlot!, aggregate.InputSlot!);
        ConnectSlots(joinCold.OutputSlot!, aggregate.InputSlot!);

        // Aggregate → Enum selector
        ConnectSlots(aggregate.OutputSlot!, enumSelector.InputSlot!);

        // Enum outputs to handlers
        var getSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Get);
        if (getSlot is not null) ConnectSlots(getSlot, handleGet.InputSlot!);
        var postSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Post);
        if (postSlot is not null) ConnectSlots(postSlot, handlePost.InputSlot!);
        var putSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Put);
        if (putSlot is not null) ConnectSlots(putSlot, handlePut.InputSlot!);
        var deleteSlot = enumSelector.GetSlotForValue(NetworkRequestMethod.Delete);
        if (deleteSlot is not null) ConnectSlots(deleteSlot, handleDelete.InputSlot!);

        // All handlers merge into finalize
        ConnectSlots(handleGet.OutputSlot!, finalize.InputSlot!);
        ConnectSlots(handlePost.OutputSlot!, finalize.InputSlot!);
        ConnectSlots(handlePut.OutputSlot!, finalize.InputSlot!);
        ConnectSlots(handleDelete.OutputSlot!, finalize.InputSlot!);

        CSharpObjectDemo.AddPipeline(tree);
        enumSelector.OutputSlots.SetSelector(typeof(VoltageRange));

        return new WorkflowDemoSession(tree, controller,
            [loadSeed, warmCache, branchA, branchB, joinHot, joinCold, aggregate, handleGet, handlePost, handlePut, handleDelete, finalize]);
    }

    /// <summary>
    /// Creates a session from an already-deserialized <see cref="TreeViewModel"/>.
    /// </summary>
    public static WorkflowDemoSession FromTree(TreeViewModel tree)
    {
        var controller = tree.Nodes.OfType<ControllerViewModel>().FirstOrDefault() ?? new ControllerViewModel();
        var nodes = tree.Nodes.OfType<NodeViewModel>();
        return new WorkflowDemoSession(tree, controller, nodes);
    }
}
