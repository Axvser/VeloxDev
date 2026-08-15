using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// Demo 是一张**大画布上的多个示例**，画布原点 (0,0) 在左上角，各示例按行列排布、互不重叠。
/// 每个示例有自己独立的启动节点（Controller）。
///
/// 当前示例（Compiler 机制由用户从零重写中，分支选择节点暂为普通节点，不再负责路由）：
///   A. 节点图能力 —— 基座图：Controller → worker → Bool/Enum 选择器（普通节点）→ 分支 → 汇合 → handler；
///   R. 链内回退 —— 线性链中实现 IRedirectable 的节点依据运行时上下文回退到某前驱编译状态。
///
/// 布点约定：示例自左上 (0,0) 起逐行排布；示例内部连线尽量自左向右 / 自上而下。
/// Method Router 保留在 NetworkRequestMethod 上（Get/Post/Put/Delete），其编译模式默认 Dynamic
/// （编译图保留全部分支，运行期按选中值路由）；可在卡片下拉切换为 Static（只编译当前选中分支）。
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
    /// <summary>主控制器（示例 A：节点图能力），供向后兼容 / 单图宿主使用。</summary>
    public ControllerViewModel Controller { get; }
    /// <summary>所有示例各自的启动节点（Controller）。</summary>
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

        // 建一个示例：创建 Controller（启动节点）并注册进树（输出口在示例各节点 slot 创建后再建）。
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
        // 示例 A：节点图能力 —— Controller → worker → Bool/Enum 选择器（普通节点）→ 分支 + 汇合 + handler
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

        // 注意：controller 已由 NewController 创建过，绝不能再次 CreateNode——重复创建会 Delete+重建，清空其 slot 的 Parent。
        foreach (var n in new IWorkflowNodeViewModel[]
        {
            loadSeed, boolSelector, hot, cold, aggregate, enumSelector,
            handleGet, handlePost, handlePut, handleDelete, finalize,
        })
            helper.CreateNode(n);   // 先注册节点（安装 helper），再创建 slot

        // 用生成器预置的默认 InputSlot/OutputSlot + SetChannelCommand 配置通道（不新建替换，避免幽灵撤销/重做）
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
        // 示例 R：链内回退 —— Controller → Start → Prepare → RedirectGate → Process → Sink。
        // RedirectGate 实现 IRedirectable：前 FailCount 次链内通过回退到 RedirectBackSteps 步前
        // （即 Prepare）重新执行，之后放行。回退目标落在中间可见检查点，重试过程清晰可见。
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
    /// 用节点生成器预置的默认 slot + SetChannelCommand 配置通道。
    /// 不要新建 SlotViewModel 替换默认值——那会触发 setter 的 Remove→DeleteCommand，产生幽灵撤销/重做条目。
    /// SetChannelCommand 是标准命令路径、非撤销（无 undo 条目）。
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
