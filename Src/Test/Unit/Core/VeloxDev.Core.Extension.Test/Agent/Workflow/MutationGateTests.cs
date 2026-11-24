using Microsoft.Extensions.AI;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Extension.Test.Agent.Workflow;

/// <summary>
/// Two scopes over one tree must not edit it at the same time. The gate belongs to the tree, not to the
/// wrapper, so the guarantee holds across scopes — the case a per-scope lock cannot express.
/// </summary>
[TestClass]
public class MutationGateTests
{
    private sealed class Probe
    {
        private int _current;
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int Calls => Volatile.Read(ref _calls);
        public int MaxConcurrent { get; private set; }

        public Task<string> RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            var now = Interlocked.Increment(ref _current);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);

            try
            {
                Entered.Set();
                Release.Wait(cancellationToken);
                return Task.FromResult("""{"status":"ok"}""");
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private static string Invoke(WorkflowAgentScope scope)
    {
        var tool = scope.ProvideTools().OfType<AIFunction>().Single(t => t.Name == "GateProbe");
        var result = tool.InvokeAsync(new AIFunctionArguments(), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        return result?.ToString() ?? string.Empty;
    }

    [TestMethod]
    public async Task TwoScopesOverOneTree_TakeTurns()
    {
        var tree = new TreeDefaultViewModel();
        var probe = new Probe();

        // Two separate scopes, two separate toolkits — only the tree is shared.
        var first = new WorkflowAgentScope(tree)
            .WithTools("probe", [AIFunctionFactory.Create(probe.RunAsync, "GateProbe")]);
        var second = new WorkflowAgentScope(tree)
            .WithTools("probe", [AIFunctionFactory.Create(probe.RunAsync, "GateProbe")]);

        var running = Task.Run(() => Invoke(first));
        Assert.IsTrue(probe.Entered.Wait(TimeSpan.FromSeconds(5)), "the first call never reached the tool body");

        var waiting = Task.Run(() => Invoke(second));
        await Task.Delay(150);

        Assert.AreEqual(1, probe.Calls,
            "the second scope's edit must still be waiting at the tree's gate");

        probe.Release.Set();
        await Task.WhenAll(running, waiting);

        Assert.AreEqual(2, probe.Calls);
        Assert.AreEqual(1, probe.MaxConcurrent, "one writer at a time, across scopes");
    }
}
