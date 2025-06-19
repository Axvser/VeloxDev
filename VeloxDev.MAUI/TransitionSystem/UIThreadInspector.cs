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
                object? result = null;
                _ = Dispatcher.GetForCurrentThread()?.Dispatch(() => result = propertyInfo.GetValue(target));
                return result;
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
                List<object?> result = [];
                _ = Dispatcher.GetForCurrentThread()?.Dispatch(() => result = interpolate.Invoke());
                return result;
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
                _ = Dispatcher.GetForCurrentThread()?.Dispatch(action);
            }
        }
    }
}
