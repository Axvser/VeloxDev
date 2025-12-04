using System.Reflection;
using VeloxDev.Core.TransitionSystem;

namespace VeloxDev.WinForms.PlatformAdapters
{
    public class UIThreadInspector : UIThreadInspectorCore
    {
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static bool _isAppRunning = false;
        
        public static void CaptureUIThread()
        {
            if (_uiThreadId != -1)
                throw new InvalidOperationException("UI context already captured.");

            var current = SynchronizationContext.Current;
            if (current?.GetType().Name != "WindowsFormsSynchronizationContext")
                throw new InvalidOperationException("Must be called on WinForms UI thread before Application.Run.");

            _uiSyncContext = current;
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            _isAppRunning = true;

            Application.ApplicationExit += (_, _) => _isAppRunning = false;
        }

        public override bool IsAppAlive() => _isAppRunning;

        public override bool IsUIThread() => Thread.CurrentThread.ManagedThreadId == _uiThreadId;

        public override object? ProtectedGetValue(bool isUIThread, object target, PropertyInfo propertyInfo)
        {
            if (isUIThread) return propertyInfo?.GetValue(target);

            var tcs = new TaskCompletionSource<object?>();
            _uiSyncContext?.Post(_ =>
            {
                try { tcs.SetResult(propertyInfo?.GetValue(target)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task.GetAwaiter().GetResult();
        }

        public override List<object?> ProtectedInterpolate(bool isUIThread, Func<List<object?>> interpolate)
        {
            if (isUIThread) return interpolate();

            var tcs = new TaskCompletionSource<List<object?>>();
            _uiSyncContext?.Post(_ =>
            {
                try { tcs.SetResult(interpolate()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task.GetAwaiter().GetResult() ?? [];
        }

        public override void ProtectedInvoke(bool isUIThread, Action action)
        {
            if (isUIThread)
            {
                action.Invoke();
                return;
            }

            _uiSyncContext?.Post(_ =>
            {
                try { action.Invoke(); }
                catch { }
            }, null);
        }
    }
}
