using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;

namespace VeloxDev.Core.Extension.Test.Examples;

/// <summary>
/// The data path behind the compiled-structure side panel: compiling through the controller's own command pushes the
/// flat outline into the tree, and a panel bound to <c>Tree.CompiledStructure</c> reads it from there. The XAML side
/// is compile-checked by the platform's compiled bindings; this is the half a build cannot see.
/// </summary>
[TestClass]
public class CompiledStructurePanelTests
{
    private static async Task CompileAsync(WorkflowDemoSession session)
        => await session.Controller.CompileCommand.ExecuteAsync(null);

    [TestMethod]
    public async Task CompilingThroughTheController_FillsTheTreesCompiledStructure()
    {
        var session = WorkflowDemoSession.Create();
        Assert.IsEmpty(session.Tree.CompiledStructure, "nothing is compiled yet");

        await CompileAsync(session);

        var rows = session.Tree.CompiledStructure;
        var graph = session.Controller.Compiler.Graphs.FirstOrDefault();
        Assert.IsNotNull(graph, "compiling must leave a graph behind");
        Assert.HasCount(CompiledOutline.Of(graph).Count, rows,
            "the panel shows exactly the outline of the compiled graph, row for row");
        Assert.AreEqual("Execute", rows[0].Kind, "the demo's chain starts with the controller");
        Assert.IsTrue(rows.Any(r => r.Kind == "Parallel"), "the demo's fan-out must be in there");
        Assert.IsTrue(rows.Any(r => r.Kind is "Option" or "Terminal"), "and so must the router's route keys");
        Assert.IsTrue(rows.Any(r => r.Depth > 0), "a branch's options are nested — the panel indents by Depth");
    }

    [TestMethod]
    public async Task CompilingTwice_ReplacesTheRows_RatherThanAppending()
    {
        var session = WorkflowDemoSession.Create();
        await CompileAsync(session);
        var once = session.Tree.CompiledStructure.Count;
        Assert.IsTrue(once > 0, "precondition: the first compile produced rows");

        await CompileAsync(session);

        Assert.HasCount(once, session.Tree.CompiledStructure,
            "a re-compile replaces the list; appending would double every row");
    }
}
