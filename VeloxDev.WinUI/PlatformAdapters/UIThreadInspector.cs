#nullable enable

using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using VeloxDev.Core.TransitionSystem;
using WinRT.Interop;

namespace VeloxDev.WinUI.PlatformAdapters
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

        public override object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread)
                return propertyInfo?.GetValue(target);

            var tcs = new TaskCompletionSource<object?>();
            DispatcherQueue?.TryEnqueue(() =>
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

            DispatcherQueue?.TryEnqueue(priority, () =>
            {
                action();
            });
        }
    }
}
