using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.Compilation;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the compile-time identity notification (ICompileTimeNotifier): the moment a workflow
/// is compiled from the controller, every downstream node already knows its execution order —
/// before any node actually runs. UI can show the badge immediately on click, not after execution.
/// </summary>
[TestClass]
public class DemoCompileTimeOrderPreview
{
    [TestMethod]
    public void CompileFromController_AllNodesKnowOrderBeforeExecution()
    {
        var session = WorkflowDemoSession.Create();
        var compiler = new WorkflowCompiler();

        // 编译但不执行 —— 模拟「点击运行的一瞬」
        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        var items = results[0].Items;

        // 每个编译项对应的节点，此时应已从 OnCompiled 拿到编译身份。
        // 正常编译项：Order+1；被略过项（未选中分支独占）：Order=-1，LastExecutionOrder=0。
        var seen = new HashSet<int>();
        foreach (var item in items)
        {
            bool skipped = item.IsSkipped;
            int expected = skipped ? 0 : item.Order + 1;
            switch (item.Node)
            {
                case NodeViewModel n:
                    Assert.AreEqual(expected, n.LastExecutionOrder,
                        $"compile-time order for '{n.Title}'");
                    Assert.AreEqual(skipped, n.IsCompileSkipped,
                        $"skip flag for '{n.Title}' matches compile-time pruning");
                    Assert.AreEqual(!skipped, n.HasExecutionOrder,
                        $"badge visibility for '{n.Title}'");
                    break;
                case EnumSelectorNodeViewModel e:
                    Assert.AreEqual(expected, e.LastExecutionOrder,
                        $"compile-time order for enum router '{e.Title}'");
                    Assert.AreEqual(skipped, e.IsCompileSkipped,
                        $"skip flag for enum router '{e.Title}'");
                    break;
                case BoolSelectorNodeViewModel b:
                    Assert.AreEqual(expected, b.LastExecutionOrder,
                        $"compile-time order for bool router '{b.Title}'");
                    Assert.AreEqual(skipped, b.IsCompileSkipped,
                        $"skip flag for bool router '{b.Title}'");
                    break;
            }

            if (!skipped)
                Assert.IsTrue(seen.Add(expected), $"order {expected} appears exactly once");
        }

        // 保留项（非跳过）的顺序是 1..M 的连续序列（紧凑重建，无空洞）
        Assert.AreEqual(seen.Count, seen.Max(), "retained orders are a compact 1..M sequence");
        Assert.AreEqual(1, seen.Min(), "first retained compiled item gets order 1");
    }

    [TestMethod]
    public void CompileFromController_SkippedBranchNodesKnowTheyWereSkipped()
    {
        var session = WorkflowDemoSession.Create();
        var compiler = new WorkflowCompiler();

        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        var items = results[0].Items;

        // 自洽验证：对每个路由器的每个独占分支，IsSkipped 必须与「分支 key != 编译时选中 key」一致。
        // 不硬编码 demo 的当前选中分支（demo 会把 enum selector 切到 VoltageRange），
        // 只验证「被略过的节点知道自己被略过、且不拥有流程顺序」这一核心语义。
        foreach (var item in items)
        {
            if (item.RouteTable is null || item.Node is not ICompileTimeRouter router)
                continue;
            if (item.BranchExclusiveItems is null || item.BranchExclusiveItems.Count == 0)
                continue;

            var chosenKey = router.GetCurrentRouteKey();
            foreach (var kv in item.BranchExclusiveItems)
            {
                bool shouldSkip = !Equals(kv.Key, chosenKey);
                foreach (var skipId in kv.Value)
                {
                    var targetItem = items.FirstOrDefault(i => i.Id == skipId);
                    if (targetItem is null) continue;

                    Assert.AreEqual(shouldSkip, targetItem.IsSkipped,
                        $"item '{targetItem.Node.GetType().Name}' skip flag matches '{kv.Key}' vs chosen '{chosenKey}'");

                    if (shouldSkip)
                    {
                        Assert.AreEqual(-1, targetItem.Order, "skipped item Order = -1");
                        Assert.AreEqual(-1, targetItem.CompositeId.OrderId, "skipped item OrderId = -1");
                        Assert.AreNotEqual(Guid.Empty, targetItem.CompositeId.Uid,
                            "skipped item keeps its UID (knows its own identity)");
                    }
                }
            }
        }

        // 至少有一个节点被编译期略过（boolSelector.Condition=true → False 分支必被略过）
        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        Assert.IsTrue(cold.IsCompileSkipped,
            "Cold Path (unchosen False branch) knows it was compile-skipped");
        Assert.AreEqual(0, cold.LastExecutionOrder, "Cold Path owns no flow order");

        // 选中分支（bool True → Hot Path）不被略过，持有流程顺序
        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        Assert.IsFalse(hot.IsCompileSkipped, "Hot Path (chosen True branch) is not skipped");
        Assert.IsTrue(hot.LastExecutionOrder > 0, "Hot Path holds a flow order");
    }
}

/// <summary>
/// Verifies the default demo (WorkflowDemoSession.Create(), which switches the enum selector
/// to VoltageRange via line 194) can still wake up downstream handler nodes by routing — the
/// SlotEnumerator preserves the wiring topology across a type switch and defaults the current
/// value to a valid member, so routing keeps working. Also verifies per-credential selection
/// memory across switching.
/// </summary>
[TestClass]
public class DemoEnumSelectorSwitchRepro
{
    [TestMethod]
    public async Task DefaultDemo_RoutesToDownstreamAfterTypeSwitch()
    {
        var session = WorkflowDemoSession.Create();   // default: line 194 → VoltageRange
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // Line 194 switched the selector type after wiring; SlotEnumerator preserves the wiring
        // topology (new branches re-routed onto the old branches' downstream by position).
        int connectedSlots = node.OutputSlots.Items.Count(s => s.Slot.Targets.Count > 0);
        Console.WriteLine($"enum selector type={node.EnumType?.Name} connectedSlots={connectedSlots}");

        var compiler = new WorkflowCompiler();
        var context = NetworkFlowContext.Create("demo");
        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        await results[0].ExecuteAsync(context, CancellationToken.None);
        int executedHandlers = context.ExecutionTrail.Count(t => t.Contains("Handler"));

        Console.WriteLine($"connectedSlots={connectedSlots} executedHandlers={executedHandlers}");

        // The default demo CAN wake up a downstream handler: SlotEnumerator re-routed the
        // VoltageRange branches onto the old downstream and defaults the current value to a
        // valid member, so routing picks a branch.
        Assert.IsGreaterThan(0, connectedSlots,
            "type switch re-routes the new branches onto the old downstream");
        Assert.IsGreaterThan(0, executedHandlers,
            "the default loaded demo wakes up a downstream handler by routing");
    }

    [TestMethod]
    public void Repro_DemoCredentialValuesPreserved()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        // Now on VoltageRange (B), current=Zero (first member).
        // Select on B, switch to A (NetworkRequestMethod), select, switch back — each credential
        // must remember its own value independently.
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "A remembers Get (from demo setup)");
        node.SelectedValue = "Post";
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "B remembers High");
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Post", node.SelectedValue, "A remembers Post");
    }

    [TestMethod]
    public void Repro_UndoRedo_DictRestoration()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");

        // Voltage=High, HTTP=Post — then undo/redo through each credential.
        node.SelectedValue = "High";
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual("Get", node.SelectedValue, "HTTP restores Get from dict");
        node.SelectedValue = "Post";
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage restores High from dict");

        // Undo → HTTP: must immediately restore Post from the dict.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("Post", node.SelectedValue, "undo → HTTP immediately restores Post");
        // Undo → Voltage: High.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual("High", node.SelectedValue, "undo → Voltage immediately restores High");
        // Redo → HTTP: Post.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("Post", node.SelectedValue, "redo → HTTP immediately restores Post");
        // Redo → Voltage: High.
        session.Tree.GetHelper().Redo();
        Assert.AreEqual("High", node.SelectedValue, "redo → Voltage immediately restores High");
    }

    [TestMethod]
    public void Repro_MethodRouter_UndoSelectSwitch()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();
        var vr = typeof(NetworkRequestMethod).Assembly.GetType("Demo.ViewModels.VoltageRange");
        Assert.IsNotNull(vr, "VoltageRange should exist in Lib");

        // The demo keeps its bootstrap on the undo stack (逐条引导步骤), but the interactive
        // switches we perform below land on top of it — so a single Undo pops exactly our own
        // switch, deterministically. To exercise the undo path, perform switches of our own, then
        // undo one.
        // The demo starts on VoltageRange; select a Voltage value first so "retain previous
        // selection" is meaningful.
        node.SelectedValue = "High";

        // Switch to HTTP → HTTP must show GET (the value the demo's initial setup selected).
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType);
        Assert.AreEqual("Get", node.SelectedValue, "HTTP shows GET selected");

        // Select an HTTP mode, then switch back to Voltage — Voltage must retain High.
        node.SelectedValue = "Post";
        Assert.AreEqual("Post", node.SelectedValue);
        node.OutputSlots.SetSelector(vr!);
        Assert.AreEqual("High", node.SelectedValue, "Voltage retains its previous selection");

        // Undo the Voltage switch → back to HTTP; HTTP must retain Post.
        session.Tree.GetHelper().Undo();
        Assert.AreEqual(typeof(NetworkRequestMethod), node.EnumType, "undo returns to HTTP");
        Assert.AreEqual("Post", node.SelectedValue, "undo to HTTP shows POST selected");
    }

    [TestMethod]
    public async Task DefaultDemo_AfterUndoSelectSwitch_RoutesToHandler()
    {
        var session = WorkflowDemoSession.Create();
        var node = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>().Single();

        // The demo keeps its bootstrap on the undo stack, so an interactive switch performed on
        // top is the deterministic top entry. Exercise the switch/undo path here: switch the
        // selector, then undo it — the remembered state (type + routing connections) must
        // restore, and routing must still wake up a downstream handler.
        node.OutputSlots.SetSelector(typeof(NetworkRequestMethod));
        session.Tree.GetHelper().Undo();   // → back to the pre-switch type (VoltageRange) + connections
        int connectedSlots = node.OutputSlots.Items.Count(s => s.Slot.Targets.Count > 0);

        var compiler = new WorkflowCompiler();
        var context = NetworkFlowContext.Create("demo");
        var results = compiler.Compile(session.Controller,
            CompileMode.BFS, CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        await results[0].ExecuteAsync(context, CancellationToken.None);
        int executedHandlers = context.ExecutionTrail.Count(t => t.Contains("Handler"));

        Console.WriteLine($"after undo select-switch: type={node.EnumType?.Name} " +
                          $"connectedSlots={connectedSlots} executedHandlers={executedHandlers}");
        Assert.IsGreaterThan(0, connectedSlots, "undoing a selector switch restores the routing connections");
        Assert.IsGreaterThan(0, executedHandlers,
            "with connections restored, routing wakes up a downstream handler");
    }

    [TestMethod]
    public async Task MethodRouter_MultiTargetBranch_GetRouteTableKeepsAllTargets()
    {
        // 用户场景：一个枚举分支指向多个目标（如 C→3 且 C→4）。
        // EnumSelectorNodeViewModel.GetRouteTable 必须保留该分支的全部目标，
        // 否则 1:1 覆盖会让丢失的目标不再属于任何分支独占集，执行时被错误计入。
        var tree = new TreeViewModel();
        var helper = tree.GetHelper();

        var router = new EnumSelectorNodeViewModel { Title = "Method Router" };
        helper.CreateNode(router);

        // 每个枚举分支一个下游节点（必须注册进 tree，编译的邻接表基于 tree.Nodes）
        var getTarget = CreateStep(helper, "GET Handler");
        var postTarget = CreateStep(helper, "POST Handler");
        var putTargetA = CreateStep(helper, "PUT Handler A");
        var putTargetB = CreateStep(helper, "PUT Handler B"); // PUT 分支指向两个目标（1:N fan-out）

        // 连接各分支：Get→GET, Post→POST, Put→PUT-A 且 Put→PUT-B
        Connect(helper, router, NetworkRequestMethod.Get, getTarget);
        Connect(helper, router, NetworkRequestMethod.Post, postTarget);
        Connect(helper, router, NetworkRequestMethod.Put, putTargetA);
        Connect(helper, router, NetworkRequestMethod.Put, putTargetB);

        // 编译
        var compiler = new WorkflowCompiler();
        var results = compiler.Compile(router, CompileMode.BFS,
            CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        var routerItem = results[0].Items.First(i => i.Node == router);

        // 路由表必须完整保留 PUT 分支的两个目标
        var putTargets = routerItem.RouteTable![NetworkRequestMethod.Put];
        Assert.AreEqual(2, putTargets.Count,
            "route table keeps BOTH targets of the PUT branch (1:N fan-out)");
        CollectionAssert.Contains(putTargets.Select(n => n).ToList(), putTargetA);
        CollectionAssert.Contains(putTargets.Select(n => n).ToList(), putTargetB);

        // 执行：选 GET → 只执行 GET Handler；PUT 分支的两个目标都必须被跳过。
        // RunCount 走 UI 线程计数器，测试环境下不可靠，改用 ExecutionTrail（WorkAsync 同步记录）。
        var context = NetworkFlowContext.Create("multi-target-repro");
        router.SelectedValue = NetworkRequestMethod.Get;
        await results[0].ExecuteAsync(context, CancellationToken.None);

        Assert.IsTrue(context.ExecutionTrail.Any(t => t.Contains("GET Handler")),
            "GET Handler executed (chosen)");
        Assert.IsFalse(context.ExecutionTrail.Any(t => t.Contains("POST Handler")),
            "POST Handler skipped (unchosen)");
        Assert.IsFalse(context.ExecutionTrail.Any(t => t.Contains("PUT Handler A")),
            "PUT Handler A skipped (Put not chosen)");
        Assert.IsFalse(context.ExecutionTrail.Any(t => t.Contains("PUT Handler B")),
            "PUT Handler B skipped (Put not chosen) — must NOT leak into the GET path");

        // 执行：选 PUT → 两个 PUT Handler 都必须执行
        var context2 = NetworkFlowContext.Create("multi-target-repro");
        router.SelectedValue = NetworkRequestMethod.Put;
        var results2 = compiler.Compile(router, CompileMode.BFS,
            CompileDirection.Forward, CompileScope.FromNode, CycleHandling.Trim);
        await results2[0].ExecuteAsync(context2, CancellationToken.None);
        Assert.IsTrue(context2.ExecutionTrail.Any(t => t.Contains("PUT Handler A")),
            "PUT Handler A executed (Put chosen)");
        Assert.IsTrue(context2.ExecutionTrail.Any(t => t.Contains("PUT Handler B")),
            "PUT Handler B executed (Put chosen)");
    }

    private static NodeViewModel CreateStep(IWorkflowTreeViewModelHelper helper, string title)
    {
        var node = new NodeViewModel { Title = title, Size = new Size(300, 260) };
        helper.CreateNode(node);
        return node;
    }

    private static void Connect(IWorkflowTreeViewModelHelper helper, EnumSelectorNodeViewModel router,
        NetworkRequestMethod method, NodeViewModel target)
    {
        var slot = router.GetSlotForValue(method)
            ?? throw new InvalidOperationException($"no output slot for {method}");
        var targetInput = target.InputSlot
            ?? throw new InvalidOperationException($"no input slot on {target.Title}");
        helper.SendConnection(slot);
        helper.ReceiveConnection(targetInput);
    }
}
