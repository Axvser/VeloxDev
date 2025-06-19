using System.Reflection;

namespace VeloxDev.Core.Interfaces.TransitionSystem
{
    public interface IUIThreadInspector : IUIThreadInspectorCore
    {
        public void ProtectedInvoke(bool isUIThread, Action action);
    }

    public interface IUIThreadInspector<TPriorityCore> : IUIThreadInspectorCore
    {
        public void ProtectedInvoke(bool isUIThread, Action action, TPriorityCore priority);
    }

    public interface IUIThreadInspectorCore
    {
        public bool IsUIThread();
        public object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo);
        public abstract List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate);
    }
}
