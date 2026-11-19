using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli.Test.Plugin;

/// <summary>
/// A node whose business code kills the process. Used to prove that a worker crash is contained: the MCP server
/// that launched it must still answer afterwards.
/// </summary>
public sealed class CrashingHelper : NodeHelper<CrashingNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
    {
        Environment.Exit(42);
        return Task.FromResult<object?>(null);
    }
}

/// <summary>A source node with no ports; compiling from it produces a one-node chain.</summary>
[WorkflowBuilder.Node<CrashingHelper>]
[DefaultSize(120, 60)]
public partial class CrashingNodeViewModel
{
    public CrashingNodeViewModel() => InitializeWorkflow();
}

/// <summary>A node that blocks its receive forever; the server's kill grace is what ends it.</summary>
public sealed class HangingHelper : NodeHelper<HangingNodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
    {
        Thread.Sleep(Timeout.Infinite);
        return Task.FromResult<object?>(null);
    }
}

/// <summary>A source node whose business code never returns.</summary>
[WorkflowBuilder.Node<HangingHelper>]
[DefaultSize(120, 60)]
public partial class HangingNodeViewModel
{
    public HangingNodeViewModel() => InitializeWorkflow();
}
