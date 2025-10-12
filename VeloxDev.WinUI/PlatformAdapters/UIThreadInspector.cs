#nullable enable

using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class UIThreadInspector : UIThreadInspectorCore<DispatcherQueuePriority>
    {
        private DispatcherQueue? _dispatcher;

        private DispatcherQueue Dispatcher
        {
            get
            {
                if (_dispatcher == null)
                    _dispatcher = DispatcherQueue.GetForCurrentThread();

                return _dispatcher
                    ?? throw new InvalidOperationException("Cannot find a valid DispatcherQueue for UI thread.");
            }
        }

        public override bool IsUIThread() =>
            DispatcherQueue.GetForCurrentThread()?.HasThreadAccess == true;

        public override object ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
                return propertyInfo?.GetValue(target) ?? throw new InvalidOperationException("Failed to Get Value While Reflecting");

            var tcs = new TaskCompletionSource<object?>();
            Dispatcher.TryEnqueue(() =>
            {
                try
                {
                    var value = propertyInfo.GetValue(target);
                    tcs.SetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Failed to Get Value While Reflecting");
        }

        public override List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread)
                return interpolate();

            var tcs = new TaskCompletionSource<List<object?>>();
            Dispatcher.TryEnqueue(() =>
            {
                try
                {
                    tcs.SetResult(interpolate());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult() ?? [];
        }

        public override void ProtectedInvoke(bool isUIThread, Action action, DispatcherQueuePriority priority)
        {
            if (isUIThread)
            {
                action();
                return;
            }

            if (!Dispatcher.TryEnqueue(priority, () =>
            {
                action();
            }))
                throw new InvalidOperationException("Failed to enqueue action to UI thread dispatcher.");
        }
    }
}
