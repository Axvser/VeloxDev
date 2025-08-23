using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;

namespace VeloxDev.WinUI.PlatformAdapters
{
    public class UIThreadInspector : IUIThreadInspector<DispatcherQueuePriority>
    {
        private readonly DispatcherQueue _uiDispatcher;

        public UIThreadInspector()
        {
            _uiDispatcher = DispatcherQueue.GetForCurrentThread()
                            ?? Microsoft.UI.Xaml.Window.Current?.DispatcherQueue
                            ?? throw new InvalidOperationException("UIThreadInspector must be created on UI thread");
        }

        public bool IsUIThread() => _uiDispatcher.HasThreadAccess;

        public object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread || _uiDispatcher.HasThreadAccess)
            {
                return propertyInfo.GetValue(target);
            }
            else
            {
                object? result = null;
                var waitEvent = new ManualResetEventSlim(false);

                // 使用正确的异步执行
                _uiDispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    try
                    {
                        result = propertyInfo.GetValue(target);
                    }
                    finally
                    {
                        waitEvent.Set();
                    }
                });

                waitEvent.Wait();
                return result;
            }
        }

        public List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread || _uiDispatcher.HasThreadAccess)
            {
                return interpolate.Invoke();
            }
            else
            {
                List<object?>? result = null;
                var waitEvent = new ManualResetEventSlim(false);

                _uiDispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    try
                    {
                        result = interpolate.Invoke();
                    }
                    finally
                    {
                        waitEvent.Set();
                    }
                });

                waitEvent.Wait();
                return result ?? [];
            }
        }

        public void ProtectedInvoke(bool isUIThread, Action action, DispatcherQueuePriority priority)
        {
            if (isUIThread || _uiDispatcher.HasThreadAccess)
            {
                action.Invoke();
            }
            else
            {
                // 直接提交到队列，无需等待
                _uiDispatcher.TryEnqueue(priority, () => action.Invoke());
            }
        }
    }
}