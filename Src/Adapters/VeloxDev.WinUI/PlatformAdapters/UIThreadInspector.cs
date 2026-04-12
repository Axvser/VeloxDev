#nullable enable

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinRT.Interop;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore<DispatcherQueuePriority>
    {
        public static DispatcherQueue? DispatcherQueue { get; set; }
        public static bool IsRunning { get; set; } = false;

        public static void SetWindow(Window window)
        {
            IsRunning = true;
            DispatcherQueue = window.DispatcherQueue;
            var hwnd = WindowNative.GetWindowHandle(window);
            var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.Closing += (sender, args) =>
            {
                IsRunning = false;
            };
        }

        public override bool IsAppAlive() => IsRunning;

        public override bool IsUIThread() => DispatcherQueue?.HasThreadAccess ?? false;

        public override object? ProtectedGetValue(bool isUIThread, object target, ITransitionProperty property)
        {
            if (isUIThread)
                return property.GetValue(target);

            var tcs = new TaskCompletionSource<object?>();
            DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    var value = property.GetValue(target);
                    tcs.SetResult(value);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult();
        }

        public override List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread)
                return interpolate();

            var tcs = new TaskCompletionSource<List<object?>>();
            DispatcherQueue?.TryEnqueue(() =>
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

            var tcs = new TaskCompletionSource<object?>();
            if (DispatcherQueue?.TryEnqueue(priority, () =>
            {
                try
                {
                    action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }) != true)
            {
                throw new InvalidOperationException("Failed to dispatch work to the WinUI UI thread.");
            }

            tcs.Task.GetAwaiter().GetResult();
        }
    }
}
