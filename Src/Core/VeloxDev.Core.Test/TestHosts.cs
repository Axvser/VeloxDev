using VeloxDev.Threading;
using VeloxDev.TransitionSystem.Abstractions;

namespace VeloxDev.Core.Test;

/// <summary>A host that answers every question on the calling thread.</summary>
internal class ImmediateHost : TransitionHostBase<NonPriority>
{
    public override ThreadRef ThreadFor(object target) => ThreadRef.None;

    protected override bool IsCurrentThread(ThreadRef thread) => true;

    protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
    {
        action();
        return true;
    }
}

/// <summary>
/// A host that is never on the target's thread, so every write really travels the post channel — the path a test
/// asserting on marshalling, or on the priority argument, has to exercise.
/// </summary>
internal class InlinePostHost<TPriority> : TransitionHostBase<TPriority>
{
    public override ThreadRef ThreadFor(object target) => ThreadRef.None;

    protected override bool IsCurrentThread(ThreadRef thread) => false;

    protected override TPriority InternalPriority => default!;

    protected override bool PostCore(object target, ThreadRef thread, Action action, TPriority priority)
    {
        action();
        return true;
    }
}

/// <summary>
/// Models the fire-and-forget adapters from the background-thread side: a write is queued and lands when
/// <see cref="Pump"/> stands in for the UI thread draining its queue, while a read is still answered inline — the
/// split the real adapters have.
/// </summary>
internal sealed class DeferredHost : TransitionHostBase<NonPriority>
{
    private readonly List<Action> _pending = [];

    public override ThreadRef ThreadFor(object target) => ThreadRef.None;

    protected override bool IsCurrentThread(ThreadRef thread) => false;

    protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
    {
        _pending.Add(action);
        return true;
    }

    public override T Run<T>(object target, Func<T> body) => body();

    public void Pump()
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var action in pending)
        {
            action();
        }
    }
}
