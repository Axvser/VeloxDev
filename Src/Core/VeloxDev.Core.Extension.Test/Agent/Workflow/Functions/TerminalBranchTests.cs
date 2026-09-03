using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// Verifies the "terminal branch" semantics: when the route table registers a branch that has no
/// downstream nodes (e.g. the Method Router's Patch), selecting that branch at runtime should
/// **end** the run directly instead of passing through to the join point (Finalize).
/// </summary>
[TestClass]
public class TerminalBranchTests
{
    [TestMethod]
    public async Task TerminalBranch_PatchNoTarget_StopsRunInsteadOfPassingThrough()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        var graph = compiler.Graphs[0];
        var router = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>()
            .Single(n => n.Title == "Method Router");

        // Make the runtime route select Patch — that branch has no downstream nodes connected.
        router.SelectedValue = NetworkRequestMethod.Patch;

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(graph, context, CancellationToken.None);

        var finalize = session.Tree.Nodes.OfType<NodeViewModel>().Single(n => n.Title == "Finalize");
        Assert.AreEqual("Idle", finalize.LastStatus,
            "terminal Patch branch must END the run — Finalize must NOT execute (no pass-through)");
    }

    [TestMethod]
    public async Task StaticCompile_LocksRouteKey_IgnoresPostCompileChange()
    {
        var session = WorkflowDemoSession.CreateLegacy();
        var boolSelector = session.Tree.Nodes.OfType<BoolSelectorNodeViewModel>().Single();
        boolSelector.CompileMode = RouterCompileMode.Static;
        boolSelector.Condition = true;   // at compile time: True → Hot branch

        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        // Change the selection after compile: at runtime it must still follow the compile-time-locked Hot (true), not the new value false.
        boolSelector.Condition = false;

        var context = new RuntimeContext();
        await new RuntimeEngine().RunAsync(compiler.Graphs[0], context, CancellationToken.None);

        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        Assert.AreEqual("Completed", hot.LastStatus,
            "Static route follows the compile-time value (Hot/true), not the post-compile change");
        Assert.AreEqual("Idle", cold.LastStatus,
            "Cold branch was pruned at compile time; must NOT execute");
    }
}
