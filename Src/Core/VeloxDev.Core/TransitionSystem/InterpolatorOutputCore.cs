using System.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class InterpolatorOutputCore<TUIThreadInspectorCore, TPriorityCore> : InterpolatorOutputBase, IFrameSequence<TPriorityCore>
    where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
{
    private readonly TUIThreadInspectorCore inspector = new();

    // 缓存可复用的帧写入委托：只捕获 this（对象是每个动画一个实例，target 固定），
    // 帧索引存 volatile 字段，避免每帧分配闭包对象。
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
/// 让帧序列携带动画的取消令牌：动画被取消（如重置时 <c>Transition.Exit</c>）后，
/// 已入队到 UI 线程的旧帧会在 <see cref="InterpolatorOutputBase.SetValues"/> 处跳过写入，
/// 避免它们覆盖重置结果（此前是"时好时坏"的根因：点重置时正在运行的动画的残留帧覆盖了重置值）。
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
        // 动画已取消 → 跳过写入，避免已入队的旧帧覆盖重置结果
        if (_cts?.IsCancellationRequested == true) return;

        foreach (var kvp in Frames)
        {
            if (!CanSetValue()) return;
            kvp.Key.SetValue(target, kvp.Value[frameIndex]);
        }
    }
}
