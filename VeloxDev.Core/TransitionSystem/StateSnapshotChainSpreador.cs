using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem;

public class StateSnapshotChainSpreador<
    T,
    TPriorityCore>(StateSnapshotCore root, T target, ITransitionScheduler<TPriorityCore> scheduler, bool canMutualTask) : StateSnapshotChainSpreadorCore<T>(root, target, canMutualTask)
    where T : class
{
    public ITransitionScheduler<TPriorityCore> Scheduler { get; } = scheduler;
}

public class StateSnapshotChainSpreador<T>(StateSnapshotCore root, T target, ITransitionScheduler scheduler, bool canMutualTask) : StateSnapshotChainSpreadorCore<T>(root, target, canMutualTask)
    where T : class
{
    public ITransitionScheduler Scheduler { get; } = scheduler;
}

public abstract class StateSnapshotChainSpreadorCore<T>(StateSnapshotCore root, T target, bool canMutualTask) : StateSnapshotChainSpreadorCore(root, canMutualTask)
{
    internal CancellationTokenSource? cts = new();
    internal readonly SemaphoreSlim slim = new(1, 1);

    public T Target { get; } = target;

    public override async Task CloseAsync()
    {
        await slim.WaitAsync().ConfigureAwait(false);
        Interlocked.Exchange(ref cts, null)?.Cancel();
        Interlocked.Exchange(ref Root.spreador, null);
        slim.Release();
    }
}

public abstract class StateSnapshotChainSpreadorCore(StateSnapshotCore root, bool canMutualTask)
{
    public StateSnapshotCore Root { get; } = root;
    public bool CanMutualTask { get; } = canMutualTask;

    public abstract Task CloseAsync();
}
