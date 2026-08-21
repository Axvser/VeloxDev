using System.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class InterpolatorOutputCore<TUIThreadInspectorCore, TPriorityCore> : InterpolatorOutputBase, IFrameSequence<TPriorityCore>
    where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
{
    private readonly TUIThreadInspectorCore inspector = new();

    // Cache a reusable frame-write delegate: it only captures this (one instance per animation, target fixed),
    // and the frame index is stored in a volatile field, avoiding a closure allocation per frame.
    private Action? _cachedUpdate;
    private object? _cachedTarget;
    private volatile int _cachedFrameIndex;

    public override bool CanSetValue() => inspector.IsAppAlive();
    public override void Update(object target, int frameIndex, object? priority = default)
    {
        if (priority is not TPriorityCore cvt_priority) throw new InvalidDataException($"The value of \"priority\" is not [ {typeof(TPriorityCore).FullName} ] !");
        Update(target, frameIndex, cvt_priority);
    }
    public virtual void Update(object target, int frameIndex, TPriorityCore priority)
    {
        if (!ReferenceEquals(_cachedTarget, target))
        {
            _cachedTarget = target;
            _cachedUpdate = () => SetValues(_cachedTarget!, _cachedFrameIndex);
        }
        _cachedFrameIndex = frameIndex;
        inspector.ProtectedInvoke(target, _cachedUpdate!, priority);
    }
}

public abstract class InterpolatorOutputCore<TUIThreadInspectorCore> : InterpolatorOutputBase, IFrameSequence
    where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
{
    private readonly TUIThreadInspectorCore inspector = new();

    private Action? _cachedUpdate;
    private object? _cachedTarget;
    private volatile int _cachedFrameIndex;

    public override bool CanSetValue() => inspector.IsAppAlive();
    public override void Update(object target, int frameIndex, object? priority = default)
    {
        Update(target, frameIndex);
    }
    public virtual void Update(object target, int frameIndex)
    {
        if (!ReferenceEquals(_cachedTarget, target))
        {
            _cachedTarget = target;
            _cachedUpdate = () => SetValues(_cachedTarget!, _cachedFrameIndex);
        }
        _cachedFrameIndex = frameIndex;
        inspector.ProtectedInvoke(target, _cachedUpdate!);
    }
}

/// <summary>
/// Lets a frame sequence carry the animation's cancellation token: after the animation is cancelled (e.g.
/// <c>Transition.Exit</c> on reset), stale frames already queued to the UI thread skip writing at
/// <see cref="InterpolatorOutputBase.SetValues"/>, preventing them from overwriting the reset result (previously
/// the root cause of the intermittent bug: residual frames from a running animation overwrote the reset value
/// when reset was clicked).
/// </summary>
internal interface ICancellableFrameSequence
{
    void SetCancellation(CancellationTokenSource cts);
}

public abstract class InterpolatorOutputBase : IFrameSequenceCore, ICancellableFrameSequence
{
    private volatile CancellationTokenSource? _cts;

    void ICancellableFrameSequence.SetCancellation(CancellationTokenSource cts) => _cts = cts;

    public abstract bool CanSetValue();
    public virtual Dictionary<ITransitionProperty, List<object?>> Frames { get; protected set; } = [];
    public virtual int Count { get; protected set; } = 0;
    public abstract void Update(object target, int frameIndex, object? priority = default);
    public virtual void AddPropertyInterpolations(ITransitionProperty propertyInfo, List<object?> objects)
    {
        if (Frames.TryGetValue(propertyInfo, out _))
        {
            Frames[propertyInfo] = objects;
        }
        else
        {
            Frames.Add(propertyInfo, objects);
        }
    }
    public virtual void SetCount(int count)
    {
        Count = count;
    }
    public virtual void SetValues(object target, int frameIndex)
    {
        // Animation cancelled → skip the write to avoid stale queued frames overwriting the reset result
        if (_cts?.IsCancellationRequested == true) return;

        foreach (var kvp in Frames)
        {
            if (!CanSetValue()) return;
            kvp.Key.SetValue(target, kvp.Value[frameIndex]);
        }
    }
}
