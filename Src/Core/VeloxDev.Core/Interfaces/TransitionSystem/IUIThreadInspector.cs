namespace VeloxDev.TransitionSystem
{
    public interface IUIThreadInspector : IUIThreadInspectorCore
    {
        public void ProtectedInvoke(object target, Action action);
    }

    public interface IUIThreadInspector<TPriorityCore> : IUIThreadInspectorCore
    {
        public void ProtectedInvoke(object target, Action action, TPriorityCore priority);
    }

    public interface IUIThreadInspectorCore
    {
        public bool IsAppAlive();
        public bool IsUIThread();
        public abstract void ProtectedInvoke(object target, Action action, object? priority = default);
        public object? ProtectedGetValue(object target, ITransitionProperty property);
        public abstract List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate);
    }
}
