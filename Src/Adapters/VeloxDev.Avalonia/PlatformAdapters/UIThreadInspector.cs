using Avalonia.Threading;
using System;
using System.Collections.Generic;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Dispatcher.UIThread?.CheckAccess() ?? default;

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            if (IsUIThread())
            {
                return property.GetValue(target);
            }
            else
            {
                return Dispatcher.UIThread?.Invoke(() => property.GetValue(target));
            }
        }

        public override List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate)
        {
            if (IsUIThread())
            {
                return interpolate.Invoke();
            }
            else
            {
                return Dispatcher.UIThread?.Invoke(interpolate) ?? [];
            }
        }

        public override void ProtectedInvoke(object target, Action action, DispatcherPriority priority)
        {
            if (IsUIThread())
            {
                action.Invoke();
            }
            else
            {
                Dispatcher.UIThread?.InvokeAsync(action, priority);
            }
        }
    }
}
