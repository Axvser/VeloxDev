using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Core.TransitionSystem
{
    public abstract class UIThreadInspectorCore<TPriorityCore> : IUIThreadInspector<TPriorityCore>
    {
        public abstract bool IsUIThread();
        public abstract void ProtectedInvoke(bool isUIThread, Action action, TPriorityCore priority);
        public abstract object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo);
        public abstract List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate);

        public virtual void ProtectedInvoke(bool isUIThread, Action action, object? priority = default)
        {
            if (priority is not TPriorityCore cvt_priority) return;
            ProtectedInvoke(isUIThread, action, cvt_priority);
        }
    }

    public abstract class UIThreadInspectorCore : IUIThreadInspector
    {
        public abstract bool IsUIThread();
        public abstract void ProtectedInvoke(bool isUIThread, Action action);
        public abstract object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo);
        public abstract List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate);

        public virtual void ProtectedInvoke(bool isUIThread, Action action, object? priority = default)
        {
            ProtectedInvoke(isUIThread, action);
        }
    }
}
