using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// 验证「终端分支」语义：路由表登记了某分支但该分支无下游节点（如 Method Router 的 Patch），
/// 运行时选中该分支应**直接结束**运行，而不是透传到汇合点（Finalize）继续执行。
/// </summary>
[TestClass]
public class TerminalBranchTests
{
    [TestMethod]
    public async Task TerminalBranch_PatchNoTarget_StopsRunInsteadOfPassingThrough()
    {
        var session = WorkflowDemoSession.Create();
        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        var graph = compiler.Graphs[0];
        var router = session.Tree.Nodes.OfType<EnumSelectorNodeViewModel>()
            .Single(n => n.Title == "Method Router");

        // 让运行期路由选到 Patch——该分支没有连接任何下游节点。
        router.SelectedValue = NetworkRequestMethod.Patch;

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(graph, context, CancellationToken.None);

        var finalize = session.Tree.Nodes.OfType<NodeViewModel>().Single(n => n.Title == "Finalize");
        Assert.AreEqual("Idle", finalize.LastStatus,
            "terminal Patch branch must END the run — Finalize must NOT execute (no pass-through)");
    }

    [TestMethod]
    public async Task StaticCompile_LocksRouteKey_IgnoresPostCompileChange()
    {
        var session = WorkflowDemoSession.Create();
        var boolSelector = session.Tree.Nodes.OfType<BoolSelectorNodeViewModel>().Single();
        boolSelector.CompileMode = RouterCompileMode.Static;
        boolSelector.Condition = true;   // 编译瞬间：True → Hot 分支

        var compiler = new CompilerViewModel();
        await compiler.CompileAsync(session.Controller);

        // 编译后再改选中值：运行期仍应走编译期锁定的 Hot（true），而不是新值 false。
        boolSelector.Condition = false;

        var context = new RuntimeContext();
        await new CompilerEngine().RunAsync(compiler.Graphs[0], context, CancellationToken.None);

        var hot = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Hot Path");
        var cold = session.Tree.Nodes.OfType<NodeViewModel>().First(n => n.Title == "Cold Path");
        Assert.AreEqual("Completed", hot.LastStatus,
            "Static route follows the compile-time value (Hot/true), not the post-compile change");
        Assert.AreEqual("Idle", cold.LastStatus,
            "Cold branch was pruned at compile time; must NOT execute");
    }
}
