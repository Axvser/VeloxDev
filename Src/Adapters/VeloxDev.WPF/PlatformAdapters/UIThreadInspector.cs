using System.Windows;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Application.Current?.Dispatcher?.CheckAccess() ?? default;

        public override object? ProtectedGetValue(bool isUIThread, object target, ITransitionProperty property)
        {
            if (isUIThread)
            {
                return property.GetValue(target);
            }
            else
            {
                return Application.Current?.Dispatcher?.Invoke(() => property.GetValue(target));
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
