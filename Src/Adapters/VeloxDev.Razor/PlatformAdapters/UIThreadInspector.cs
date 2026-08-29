namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore
    {
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static volatile bool _isAppRunning = true;

        /// <summary>
        /// Optionally capture on the Blazor circuit thread. If not called, capture also happens automatically on first UI-thread access
        /// (see <see cref="EnsureCaptured"/>); it is only needed when an animation starts from a background thread for the first time,
        /// e.g. calling <c>Execute</c> from a non-UI thread via <c>Task.Run</c>.
        /// </summary>
        public static void CaptureUIThread()
        {
            if (_uiThreadId != -1) return;
            EnsureCaptured();
        }

        public static void NotifyShutdown() => _isAppRunning = false;

        /// <summary>
        /// Lazy capture: records the context and thread id the first time this class is touched on a thread with a SynchronizationContext
        /// (such as the circuit thread). Calling from a background thread (no SynchronizationContext) has no side effects.
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
