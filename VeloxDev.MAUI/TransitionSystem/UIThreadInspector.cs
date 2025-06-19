using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.MAUI.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector
    {
        public bool IsUIThread()
        {
            return Dispatcher.GetForCurrentThread()?.IsDispatchRequired == false;
        }

        public object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
            {
                return propertyInfo.GetValue(target);
            }
            else
            {
                return Dispatcher.GetForCurrentThread()?.Dispatch(() => propertyInfo.GetValue(target));
            }
        }

        public List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread)
            {
                return interpolate.Invoke();
            }
            else
            {
                return [Dispatcher.GetForCurrentThread()?.Dispatch(() => interpolate.Invoke())];
            }
        }

        public void ProtectedInvoke(bool isUIThread, Action action)
        {
            if (isUIThread)
            {
                action.Invoke();
            }
            else
            {
                Dispatcher.GetForCurrentThread()?.Dispatch(action);
            }
        }
    }
}
