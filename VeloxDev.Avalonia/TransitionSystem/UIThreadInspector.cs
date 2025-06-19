using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Reflection;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.Avalonia.TransitionSystem
{
    public class UIThreadInspector() : IUIThreadInspector<DispatcherPriority>
    {
        public bool IsUIThread() => Dispatcher.UIThread.CheckAccess();

        public object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
            {
                return propertyInfo.GetValue(target);
            }
            else
            {
                return Dispatcher.UIThread.Invoke(() => propertyInfo.GetValue(target));
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
                return Dispatcher.UIThread.Invoke(interpolate);
            }
        }

        public void ProtectedInvoke(bool isUIThread, Action action, DispatcherPriority priority)
        {
            if (isUIThread)
            {
                action.Invoke();
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(action, priority);
            }
        }
    }
}
