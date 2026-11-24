using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI;
using VeloxDev.AI.Pipelines;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The two guarantees every tool call carries: it respects the tree's gate (a read waits for a write; reads do
/// not wait for each other), and it leaves the tree marked dirty so the host's frame driver recomputes the canvas.
/// </summary>
[TestClass]
public class ToolGateTests
{
    /// <summary>Records the dirty marks the toolkit makes after a call.</summary>
    private sealed class CountingHelper : TreeHelper<TreeDefaultViewModel>
    {
        public int DirtyMarks { get; private set; }

        public override void MarkDirty()
        {
            DirtyMarks++;
            base.MarkDirty();
        }
    }

    private static (WorkflowAgentScope Scope, CountingHelper Helper) NewScope()
    {
        var tree = new TreeDefaultViewModel();
        var helper = new CountingHelper();
        tree.SetHelper(helper);
        return (new WorkflowAgentScope(tree), helper);
    }

    private static object? Invoke(AITool tool)
        => ((AIFunction)tool)
            .InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    [TestMethod]
    public async Task Query_WaitsWhileAWriteHoldsTheGate()
    {
        var (scope, _) = NewScope();
        var lease = await scope.TreeGate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None);

        var call = Task.Run(() => WorkflowToolInvoker.Invoke(scope, "ListNodes"));
        try
        {
            await Task.Delay(150);
            Assert.IsFalse(
                call.IsCompleted,
                "a query enumerates the graph, so it must wait while an edit holds the tree exclusively");
        }
        finally
        {
            lease.Dispose();
        }

        Assert.IsTrue(call.Wait(TimeSpan.FromSeconds(15)));
    }

    [TestMethod]
    public async Task Queries_DoNotWaitForEachOther()
    {
        var (scope, _) = NewScope();
        var first = await scope.TreeGate.EnterAsync(ToolAccess.Shared, CancellationToken.None);

        var call = Task.Run(() => WorkflowToolInvoker.Invoke(scope, "ListNodes"));
        try
        {
            Assert.IsTrue(
                call.Wait(TimeSpan.FromSeconds(15)),
                "a second reader enters while the first is still inside the gate");
        }
        finally
        {
            first.Dispose();
        }
    }

    [TestMethod]
    public void SuccessfulQuery_MarksTheTreeDirty()
    {
        var (scope, helper) = NewScope();

        WorkflowToolInvoker.Invoke(scope, "ListNodes");

        Assert.AreEqual(1, helper.DirtyMarks, "dirty marking is unconditional, so a query marks the tree too");
    }

    [TestMethod]
    public void FailedCall_MarksTheTreeDirty()
    {
        var (scope, helper) = NewScope();
        Func<string> body = () => throw new InvalidOperationException("boom");
        scope.WithTools(null, AIFunctionFactory.Create(body, "Boom"));

        var result = WorkflowToolInvoker.Invoke(scope, "Boom");

        StringAssert.Contains(result, "error");
        Assert.AreEqual(1, helper.DirtyMarks, "a write that threw halfway may have left the graph changed");
    }

    [TestMethod]
    public async Task UngatedTool_IsTheOnlyCallThatRunsWhileTheGateIsHeld()
    {
        var (scope, _) = NewScope();
        var tools = new ToolPipeline { ToolGate = () => scope.TreeGate };
        Func<string> body = () => "ok";
        var probe = AIFunctionFactory.Create(body, "GateProbe");
        var gated = new TrackedAIFunction(probe, tools);
        var ungated = new TrackedAIFunction(probe, tools, gated: false);

        var lease = await scope.TreeGate.EnterAsync(ToolAccess.Exclusive, CancellationToken.None);
        try
        {
            var blocked = Task.Run(() => Invoke(gated));
            await Task.Delay(150);
            Assert.IsFalse(blocked.IsCompleted, "a gated call takes the tree gate");

            Assert.AreEqual(
                "ok",
                Invoke(ungated)?.ToString(),
                "the sub-agent wait tool must stay runnable while the children it waits for need the gate");
        }
        finally
        {
            lease.Dispose();
        }
    }

    [TestMethod]
    public async Task LegacyMutationGate_StillSerializesEverything()
    {
        using var legacy = new SemaphoreSlim(1, 1);
        var tools = new ToolPipeline { MutationGate = () => legacy };
        Func<string> body = () => "ok";
        var gated = new TrackedAIFunction(AIFunctionFactory.Create(body, "LegacyProbe"), tools);

        await legacy.WaitAsync();
        try
        {
            var blocked = Task.Run(() => Invoke(gated));
            await Task.Delay(150);
            Assert.IsFalse(blocked.IsCompleted, "a host that only supplies the old semaphore keeps full exclusion");
        }
        finally
        {
            legacy.Release();
        }
    }
}
