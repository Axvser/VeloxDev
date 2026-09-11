namespace VeloxDev.TransitionSystem
{
    public interface IUIThreadInspector<TPriorityCore> : IUIThreadInspectorCore
    {
        public void ProtectedInvoke(object target, Action action, TPriorityCore priority);
    }

    public interface IUIThreadInspectorCore
    {
        public bool IsAppAlive();
        public bool IsUIThread();
        public object? ProtectedGetValue(object target, ITransitionProperty property);
    }
}
