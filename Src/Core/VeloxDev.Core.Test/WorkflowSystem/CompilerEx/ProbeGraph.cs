using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem.CompilerEx;

/// <summary>CompilerEx 契约测试共享的小工具:接线 / 编译 / 运行 / 图结构读取。</summary>
internal static class ProbeGraph
{
    /// <summary>连接 from 的出槽 → to 的入槽(只维护编译器消费的 Targets/Sources/Parent)。</summary>
    public static void Wire(ProbeNode from, ProbeNode to)
    {
        from.Output.Targets.Add(to.Input);
        to.Input.Sources.Add(from.Output);
    }

    /// <summary>以 start 为根编译可达子图,返回首个 CompiledGraph。</summary>
    public static CompiledGraph Compile(ProbeNode start)
    {
        var graphs = new CompilerViewModel().CompileAsync(start).GetAwaiter().GetResult();
        Assert.IsTrue(graphs.Count > 0, "CompileAsync should produce at least one graph.");
        return graphs[0];
    }

    /// <summary>用运行引擎驱动一个编译图,返回会话(供断言 Status/Attempt/logs)。</summary>
    public static async Task<RuntimeContext> RunAsync(
        CompiledGraph graph, object? seed = null, CancellationToken ct = default)
    {
        var context = new RuntimeContext { Data = seed };
        await new RuntimeEngine().RunAsync(graph, context, ct);
        return context;
    }

    public static ChainSegment? AsChain(CompileSegment? entry) => entry as ChainSegment;
    public static BranchSegment? AsBranch(CompileSegment? entry) => entry as BranchSegment;
    public static ParallelSegment? AsParallel(CompileSegment? entry) => entry as ParallelSegment;
}
