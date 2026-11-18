using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.Serialization;

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

    /// <summary>
    /// The run manager's first half: a manual compile is a task row before it is a run, the row says where it came
    /// from, and the output page already shows the plan it compiled.
    /// </summary>
    [TestMethod]
    public async Task CompilingThroughTheController_AlsoFillsTheRunManager()
    {
        var session = WorkflowDemoSession.Create();
        Assert.IsEmpty(session.Tree.Runs.Tasks, "nothing has been compiled or run yet");

        await CompileAsync(session);

        var task = session.Tree.Runs.Tasks.FirstOrDefault()!;
        Assert.IsNotNull(task, "the compile must leave a task row");
        Assert.AreEqual(WorkflowRunSource.Manual, task.Source);
        Assert.AreEqual(WorkflowRunState.Compiled, task.State);
        Assert.AreSame(task, session.Tree.Runs.Selected, "the newest task becomes the output page's subject");
        Assert.IsNotEmpty(task.Lines, "the row carries the compiled outline");
        Assert.IsNotEmpty(session.Tree.ExecutionLog, "and the output page mirrors it");
        Assert.IsTrue(task.CanStart, "a compiled row can be run");
    }

    /// <summary>
    /// The Agent's compile tools have no runtime session, so the catalog reads their result's own outline. This is
    /// the other half of "every compiled task shows up, whoever compiled it".
    /// </summary>
    [TestMethod]
    public void AnAgentCompile_BecomesARowFromItsOwnOutline()
    {
        var session = WorkflowDemoSession.Create();
        const string result = """
            {"status":"ok","role":"Root","graphCount":1,
             "entries":[
               {"depth":0,"type":"Execute","nodes":["ControllerViewModel"]},
               {"depth":0,"type":"Branch","router":"TimerNodeViewModel","isDynamic":false},
               {"depth":0,"type":"Parallel","branches":3}],
             "nodeOrders":[{"i":1,"order":0,"chainIndex":0,"offset":0,"isStopped":false}]}
            """;

        session.Tree.Runs.NoteAgentToolCall("CompileWorkflow", result);

        var task = session.Tree.Runs.Tasks.FirstOrDefault()!;
        Assert.IsNotNull(task, "the compile result must leave a row");
        Assert.AreEqual(WorkflowRunSource.Agent, task.Source);
        Assert.AreEqual(WorkflowRunState.Compiled, task.State);
        StringAssert.Contains(task.ProgressText, "3 段",
            "the row counts the flat entries the tool returned");
        Assert.IsTrue(task.Lines.Any(line => line.Contains("Parallel")), "the outline's segment kinds survive");
    }

    /// <summary>The inspector's selection is view state, not workflow state: saving a tree must not write it.</summary>
    [TestMethod]
    public void TheSelectedElement_IsNotPartOfTheSavedTree()
    {
        var session = WorkflowDemoSession.Create();
        session.Tree.SelectedElement = new WorkflowElementEntry(
            session.Tree.Nodes[0], "selection", WorkflowElementKind.Node);

        var json = session.Tree.Serialize();

        Assert.IsFalse(json.Contains("selectedElement", StringComparison.OrdinalIgnoreCase),
            "the selection would drag the element, and through it the whole tree, into the file");
    }
}
