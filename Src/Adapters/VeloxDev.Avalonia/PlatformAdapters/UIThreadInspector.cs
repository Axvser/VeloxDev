using Avalonia.Threading;
using System;
using System.Collections.Generic;
using VeloxDev.Core.Interfaces.TransitionSystem;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.Avalonia.PlatformAdapters
{
    public class UIThreadInspector() : UIThreadInspectorCore<DispatcherPriority>
    {
        public override bool IsAppAlive() => true;

        public override bool IsUIThread() => Dispatcher.UIThread?.CheckAccess() ?? default;

        public override object? ProtectedGetValue(bool isUIThread, object target, ITransitionProperty property)
        {
            if (isUIThread)
            {
                return property.GetValue(target);
            }
            else
            {
                return Dispatcher.UIThread?.Invoke(() => property.GetValue(target));
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
                return Dispatcher.UIThread?.Invoke(interpolate) ?? [];
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
                Dispatcher.UIThread?.InvokeAsync(action, priority);
            }
        }
    }
}
