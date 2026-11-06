using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Cli.Test.Plugin;

/// <summary>Spends a fixed time in its receive body, so a run is still in flight when a pause/stop arrives.</summary>
public sealed class SlowHelper : NodeHelper<SlowNodeViewModel>
{
    public override async Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
    {
        await Task.Delay(Component?.DelayMilliseconds ?? 150, ct).ConfigureAwait(false);
        return context.Data;
    }
}

/// <summary>A two-port node with a controllable receive delay.</summary>
[WorkflowBuilder.Node<SlowHelper>]
[DefaultSize(120, 60)]
public partial class SlowNodeViewModel : ICompileTimeAware
{
    public SlowNodeViewModel() => InitializeWorkflow();

    // The compiler only assigns Order/ChainIndex to nodes that implement this contract; without it the host
    // redirect validator sees Order = -1 and refuses the target as "not an upstream compiled node".
    public ICompileContext? CompileContext { get; private set; }

    public void AttachCompileTimeContext(ICompileContext context) => CompileContext = context;

    /// <summary>Delay applied by every receive.</summary>
    public int DelayMilliseconds { get; set; } = 150;

    [VeloxProperty] public partial PluginSlotViewModel InputSlot { get; set; }

    [VeloxProperty] public partial PluginSlotViewModel OutputSlot { get; set; }
}

/// <summary>Fails on its first receive and succeeds afterwards — the shape a host-level redirect needs.</summary>
public sealed class FlakyHelper : NodeHelper<FlakyNodeViewModel>
{
    private static int attempts;

    /// <summary>Lets a test start from "never failed yet"; the counter lives for the process' lifetime.</summary>
    public static void ResetAttempts() => Interlocked.Exchange(ref attempts, 0);

    public override Task<object?> ReceiveAsync(ITaskContext context, CancellationToken ct)
    {
        if (Interlocked.Increment(ref attempts) == 1)
            throw new InvalidOperationException("the flaky node fails on its first attempt");

        return Task.FromResult(context.Data);
    }
}

/// <summary>A two-port node that reports an error once, then completes.</summary>
[WorkflowBuilder.Node<FlakyHelper>]
[DefaultSize(120, 60)]
public partial class FlakyNodeViewModel : ICompileTimeAware
{
    public FlakyNodeViewModel() => InitializeWorkflow();

    public ICompileContext? CompileContext { get; private set; }

    public void AttachCompileTimeContext(ICompileContext context) => CompileContext = context;

    [VeloxProperty] public partial PluginSlotViewModel InputSlot { get; set; }

    [VeloxProperty] public partial PluginSlotViewModel OutputSlot { get; set; }
}

/// <summary>A slot type the test nodes can own; the plugin does not need any custom slot behaviour.</summary>
[WorkflowBuilder.Slot<SlotHelper>]
public partial class PluginSlotViewModel
{
    public PluginSlotViewModel() => InitializeWorkflow();
}
