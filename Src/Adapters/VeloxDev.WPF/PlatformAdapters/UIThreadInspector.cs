using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WPF.PlatformAdapters
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.CheckAccess() ?? default;

        public override object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
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

        public override List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
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

        public override void ProtectedInvoke(bool isUIThread, Action action, DispatcherPriority priority)
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
