namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class UIThreadInspectorCore<TPriorityCore> : UIThreadInspectorBase, IUIThreadInspector<TPriorityCore>
{
    public abstract void ProtectedInvoke(object target, Action action, TPriorityCore priority);

    public override void ProtectedInvoke(object target, Action action, object? priority = default)
    {
        if (priority is not TPriorityCore cvt_priority) return;
        ProtectedInvoke(target, action, cvt_priority);
    }
}

public abstract class UIThreadInspectorCore : UIThreadInspectorBase, IUIThreadInspector
{
    public abstract void ProtectedInvoke(object target, Action action);

    public override void ProtectedInvoke(object target, Action action, object? priority = default)
    {
        ProtectedInvoke(target, action);
    }
}

public abstract class UIThreadInspectorBase : IUIThreadInspectorCore
{
    public abstract bool IsAppAlive();
    public abstract bool IsUIThread();
    public abstract object? ProtectedGetValue(object target, ITransitionProperty property);
    public abstract void ProtectedInvoke(object target, Action action, object? priority = default);
}
