namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class InterpolatorOutputCore<TUIThreadInspectorCore, TPriorityCore> : InterpolatorOutputBase, IFrameSequence<TPriorityCore>
    where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
{
    private readonly TUIThreadInspectorCore inspector = new();
    public override bool CanSetValue() => inspector.IsAppAlive();
    public override void Update(object target, int frameIndex, object? priority = default)
    {
        if (priority is not TPriorityCore cvt_priority) throw new InvalidDataException($"The value of \"priority\" is not [ {typeof(TPriorityCore).FullName} ] !");
        Update(target, frameIndex, cvt_priority);
    }
    public virtual void Update(object target, int frameIndex, TPriorityCore priority)
    {
        inspector.ProtectedInvoke(target, () => { SetValues(target, frameIndex); }, priority);
    }
}

public abstract class InterpolatorOutputCore<TUIThreadInspectorCore> : InterpolatorOutputBase, IFrameSequence
    where TUIThreadInspectorCore : IUIThreadInspectorCore, new()
{
    private readonly TUIThreadInspectorCore inspector = new();
    public override bool CanSetValue() => inspector.IsAppAlive();
    public override void Update(object target, int frameIndex, object? priority = default)
    {
        Update(target, frameIndex);
    }
    public virtual void Update(object target, int frameIndex)
    {
        inspector.ProtectedInvoke(target, () => { SetValues(target, frameIndex); });
    }
}

public abstract class InterpolatorOutputBase : IFrameSequenceCore
{
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
        foreach (var kvp in Frames)
        {
            if (!CanSetValue()) return;
            kvp.Key.SetValue(target, kvp.Value[frameIndex]);
        }
    }
}
