using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>Shared helpers for the CompilerEx contract tests: wiring / compile / run / graph inspection.</summary>
internal static class ProbeGraph
{
    /// <summary>Connects from's output slot to to's input slot (maintains only the Targets/Sources/Parent the compiler consumes).</summary>
    public static void Wire(ProbeNode from, ProbeNode to)
    {
        from.Output.Targets.Add(to.Input);
        to.Input.Sources.Add(from.Output);
    }

    /// <summary>Compiles the reachable sub-graph rooted at start and returns the first CompiledGraph.</summary>
    public static CompiledGraph Compile(ProbeNode start)
    {
        var graphs = new CompilerViewModel().CompileAsync(start, CompileRole.Root).GetAwaiter().GetResult();
        Assert.IsTrue(graphs.Count > 0, "CompileAsync should produce at least one graph.");
        return graphs[0];
    }

    /// <summary>Drives a compiled graph with the runtime engine and returns the session (for Status/Attempt/logs asserts). Optionally tracks a target node.</summary>
    public static async Task<RuntimeContext> RunAsync(
        CompiledGraph graph, object? seed = null, CancellationToken ct = default, ProbeNode? target = null)
    {
        var context = new RuntimeContext { Data = seed, Target = target };
        await new RuntimeEngine().RunAsync(graph, context, ct);
        return context;
    }

    public static ChainSegment? AsChain(CompileSegment? entry) => entry as ChainSegment;
    public static BranchSegment? AsBranch(CompileSegment? entry) => entry as BranchSegment;
    public static ParallelSegment? AsParallel(CompileSegment? entry) => entry as ParallelSegment;
}
