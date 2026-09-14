using Microsoft.Extensions.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow.Functions;

/// <summary>
/// The toolkit promises that a registered tool call runs on the host's synchronization context — the
/// whole call, not just its entry. Workflow components are UI-bound, so a tool that drifts back onto a
/// thread-pool thread after its first await would touch them from the wrong thread.
/// </summary>
[TestClass]
public class ToolThreadAffinityTests
{
    /// <summary>
    /// A context that owns one dedicated thread and installs itself as current there, so a bare
    /// <c>await</c> inside a posted callback resumes on that same thread — the way a real dispatcher
    /// behaves, and the way a thread-pool context does not.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public int ThreadId { get; private set; }

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                    callback(state);
            })
            { IsBackground = true, Name = "single-thread-context" };
            thread.Start();
            SpinWait.SpinUntil(() => ThreadId != 0, TimeSpan.FromSeconds(5));
        }

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == ThreadId) { d(state); return; }

            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            }, null);
            done.Wait(TimeSpan.FromSeconds(10));
            if (failure is not null) throw failure;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    /// <summary>
    /// A node helper that reports when the node's own business code runs, so a test can tell which
    /// thread the execution engine drove it on.
    /// </summary>
    private sealed class ThreadRecordingHelper(Func<Task> onReceive) : NodeHelper<NodeDefaultViewModel>
    {
        public override async Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
        {
            await onReceive();
            return null;
        }
    }

    private static string Invoke(AITool tool, params (string Name, object? Value)[] args)
    {
        var callArgs = new AIFunctionArguments();
        foreach (var (name, value) in args)
            if (value is not null)
                callArgs[name] = value;

        var result = ((AIFunction)tool)
            .InvokeAsync(callArgs, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public void RegisteredTool_StaysOnTheContextAcrossAnAwait()
    {
        using var context = new SingleThreadContext();
        int atEntry = 0, afterAwait = 0, atReturn = 0;

        var probe = AIFunctionFactory.Create(async (CancellationToken ct) =>
        {
            atEntry = Environment.CurrentManagedThreadId;
            await Task.Yield();                  // bare await: resumes on the captured context
            afterAwait = Environment.CurrentManagedThreadId;
            await Task.Delay(1, ct);
            atReturn = Environment.CurrentManagedThreadId;
            return "ok";
        }, "ProbeTool");

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel())
            .WithSynchronizationContext(context)
            .WithTools(null, probe);

        var tool = scope.ProvideTools().Single(t => t.Name == "ProbeTool");
        Assert.AreEqual("ok", Invoke(tool));

        Assert.AreEqual(context.ThreadId, atEntry, "the wrapper must marshal the call onto the registered context");
        Assert.AreEqual(context.ThreadId, afterAwait, "an await must not let the call escape the context");
        Assert.AreEqual(context.ThreadId, atReturn, "nor must a second await");
        Assert.AreNotEqual(0, atEntry);
    }

    [TestMethod]
    public void RegisteredTool_WithoutAContext_RunsWhereverItIsCalled()
    {
        // The contract is conditional on the host registering a context; without one nothing is marshalled
        // and the test above would be asserting a property the scope never promised.
        var callerThread = 0;
        var probe = AIFunctionFactory.Create((CancellationToken ct) =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            return "ok";
        }, "ProbeTool");

        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithTools(null, probe);
        var tool = scope.ProvideTools().Single(t => t.Name == "ProbeTool");

        Assert.AreEqual("ok", Invoke(tool));
        Assert.AreEqual(Environment.CurrentManagedThreadId, callerThread);
    }

    [TestMethod]
    public void BuiltInTools_AreRegisteredWithTheWrapper()
    {
        // Every built-in tool must go through the tracking wrapper; one registered raw would silently
        // lose marshalling, call accounting and the tool-call callback together.
        var scope = new WorkflowAgentScope(new TreeDefaultViewModel()).WithMaxToolCalls(5);
        var tools = scope.ProvideTools();

        Assert.IsNotEmpty(tools);
        foreach (var tool in tools)
            Assert.AreEqual("TrackedAIFunction", tool.GetType().Name, $"'{tool.Name}' is not wrapped");
    }

    [TestMethod]
    public async Task BuiltInTool_WithAGenuineSuspension_StaysOnTheContext()
    {
        // ExecuteNodes iterates: it awaits node 0, then reads and dispatches node 1. The second pass
        // therefore happens *after* a real suspension, and a ConfigureAwait(false) in that loop would
        // move the second node's business code onto a thread-pool thread.
        //
        // The node code has to actually suspend for this to mean anything: an await that completes
        // synchronously never yields, and the whole call stays on one thread by accident.
        using var context = new SingleThreadContext();
        var receiveThreads = new ConcurrentQueue<int>();

        var tree = new TreeDefaultViewModel();
        for (int i = 0; i < 2; i++)
        {
            var node = new NodeDefaultViewModel();
            tree.GetHelper().CreateNode(node);
            node.SetHelper(new ThreadRecordingHelper(async () =>
            {
                await Task.Yield();                       // force a real suspension
                receiveThreads.Enqueue(Environment.CurrentManagedThreadId);
            }));
        }

        var scope = new WorkflowAgentScope(tree)
            .WithSynchronizationContext(context)
            .WithAllowNodeExecution(true);

        var tool = scope.ProvideTools().Single(t => t.Name == "ExecuteNodes");
        var json = JObject.Parse(Invoke(tool, ("nodeIndicesJson", "[0,1]")));

        Assert.AreEqual("ok", json["status"]?.Value<string>(), json.ToString());
        Assert.AreEqual(2, json["completed"]?.Value<int>(), "both nodes must have been driven");
        Assert.HasCount(2, receiveThreads);
        foreach (var thread in receiveThreads)
            Assert.AreEqual(context.ThreadId, thread,
                "node code must stay on the host's context across the tool's own await");
    }

    [TestMethod]
    public void CompiledRun_DrivesNodeCodeOnTheRegisteredContext()
    {
        // Smoke coverage for the chain-run path under a host context: it must run to completion there.
        // It does NOT prove the ConfigureAwait removal on its own — a single-node compile completes
        // synchronously, so this path never suspends to begin with. The suspension case above is what
        // distinguishes the two behaviours.
        using var context = new SingleThreadContext();
        int? receiveThread = null;

        var tree = new TreeDefaultViewModel();
        var node = new NodeDefaultViewModel();
        tree.GetHelper().CreateNode(node);
        node.SetHelper(new ThreadRecordingHelper(() => { receiveThread = Environment.CurrentManagedThreadId; return Task.CompletedTask; }));

        var scope = new WorkflowAgentScope(tree)
            .WithSynchronizationContext(context)
            .WithAllowNodeExecution(true);

        var tool = scope.ProvideTools().Single(t => t.Name == "RunCompiledWorkflow");
        var json = JObject.Parse(Invoke(tool, ("startNodeIndex", 0)));

        Assert.AreEqual("ok", json["status"]?.Value<string>(), json.ToString());
        Assert.IsNotNull(receiveThread, "the node's business code must have been driven");
        Assert.AreEqual(context.ThreadId, receiveThread,
            "driving a compiled chain must not move node code off the host's context");
    }
}
