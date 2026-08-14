namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore
    {
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static volatile bool _isAppRunning = true;

        /// <summary>
        /// 显式在 Blazor circuit 线程上捕获（可选）。不调用时也会在首次 UI 线程访问时自动捕获
        /// （见 <see cref="EnsureCaptured"/>）；仅当动画从后台线程首次启动时才需要，
        /// 例如经 <c>Task.Run</c> 在非 UI 线程调用 <c>Execute</c>。
        /// </summary>
        public static void CaptureUIThread()
        {
            if (_uiThreadId != -1) return;
            EnsureCaptured();
        }

        public static void NotifyShutdown() => _isAppRunning = false;

        /// <summary>
        /// 惰性捕获：首次在带 SynchronizationContext 的线程（如 circuit 线程）触碰本类时
        /// 自动记录上下文与线程 id。后台线程（无 SynchronizationContext）调用时无副作用。
        /// </summary>
        private static void EnsureCaptured()
        {
            if (_uiThreadId != -1) return;

            var current = SynchronizationContext.Current;
            if (current == null) return;

            _uiSyncContext = current;
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            _isAppRunning = true;
        }

        public override bool IsAppAlive() => _isAppRunning;

        public override bool IsUIThread()
        {
            EnsureCaptured();
            return Thread.CurrentThread.ManagedThreadId == _uiThreadId;
        }

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            if (IsUIThread()) return property.GetValue(target);
            if (_uiSyncContext == null) return default;

            var tcs = new TaskCompletionSource<object?>();
            _uiSyncContext.Post(_ =>
            {
                try { tcs.SetResult(property.GetValue(target)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task.GetAwaiter().GetResult();
        }

        public override List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate)
        {
            if (IsUIThread()) return interpolate();
            if (_uiSyncContext == null) return [];

            var tcs = new TaskCompletionSource<List<object?>>();
            _uiSyncContext.Post(_ =>
            {
                try { tcs.SetResult(interpolate()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task.GetAwaiter().GetResult() ?? [];
        }

        public override void ProtectedInvoke(object target, Action action)
        {
            if (IsUIThread())
            {
                action.Invoke();
                return;
            }
            if (_uiSyncContext == null) return;

            var tcs = new TaskCompletionSource<object?>();
            _uiSyncContext.Post(_ =>
            {
                try
                {
                    action.Invoke();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);
            tcs.Task.GetAwaiter().GetResult();
        }
    }
}
