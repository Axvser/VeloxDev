using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
{
    public class UIThreadInspector() : IUIThreadInspector<DispatcherPriority>
    {
        public bool IsUIThread() => Application.Current?.Dispatcher?.CheckAccess() ?? default;
        public object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
            {
                return propertyInfo.GetValue(target);
            }
            else
            {
                return Application.Current?.Dispatcher?.Invoke(() => propertyInfo.GetValue(target));
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
                return Application.Current?.Dispatcher?.Invoke(interpolate) ?? [];
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
                Application.Current?.Dispatcher?.InvokeAsync(action, priority);
            }
        }
    }
}
