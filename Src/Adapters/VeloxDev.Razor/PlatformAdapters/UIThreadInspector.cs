using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : TransitionHostBase<NonPriority>
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

        public override bool IsAlive => _isAppRunning;

        public override ThreadRef ThreadFor(object target)
        {
            EnsureCaptured();
            return ThreadRef.From(_uiSyncContext);
        }

        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<SynchronizationContext>(out var context)
               && ReferenceEquals(SynchronizationContext.Current, context);

        protected override bool PostCore(object target, Action action, NonPriority priority)
        {
            if (_uiSyncContext is null) return false;

            // Post 没有失败信号，只能按"已接受"记；真正的丢弃由帧侧的取消标记兜住。
            _uiSyncContext.Post(_ => action(), null);
            return true;
        }
    }
}
