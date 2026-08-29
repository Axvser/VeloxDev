using System.Windows.Forms;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore
    {
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static volatile bool _isAppAlive = true;

        /// <summary>
        /// Optionally capture on the UI thread. Lazy capture (<see cref="EnsureCaptured"/>) and
        /// target-derived <see cref="Control"/> marshaling already cover most scenarios; this method is only a fallback.
        /// </summary>
        public static void CaptureUIThread()
        {
            if (_uiThreadId != -1) return;

            EnsureCaptured();
            if (_uiThreadId == -1)
                throw new InvalidOperationException("Must be called on WinForms UI thread before Application.Run.");
        }

        /// <summary>
        /// Lazy capture: records the <see cref="SynchronizationContext"/> and thread id the first time this class is
        /// touched from the UI thread. Calling from a background thread (a non-WinForms SynchronizationContext) has no side effects.
        /// </summary>
        private static void EnsureCaptured()
        {
            if (_uiThreadId != -1) return;

            var current = SynchronizationContext.Current;
            if (current?.GetType().Name != "WindowsFormsSynchronizationContext") return;

            _uiSyncContext = current;
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            _isAppAlive = true;

            Application.ApplicationExit += (_, _) => _isAppAlive = false;
        }

        /// <summary>
        /// The target object (<see cref="Control"/>) takes priority: the control itself knows its owning UI thread,
        /// so it can be marshaled from any thread with <see cref="Control.Invoke(Delegate)"/> / <see cref="Control.BeginInvoke(Delegate)"/>
        /// — no explicit capture is needed even for a background first start.
        /// </summary>
        private static Control? ControlDispatcher(object target)
            => target is Control control && control.IsHandleCreated ? control : null;

        public override bool IsAppAlive() => _isAppAlive;

        public override bool IsUIThread()
        {
            EnsureCaptured();
            return Thread.CurrentThread.ManagedThreadId == _uiThreadId;
        }

        public override object? ProtectedGetValue(object target, ITransitionProperty property)
        {
            var control = ControlDispatcher(target);
            if (control != null)
            {
                if (!control.InvokeRequired) return property.GetValue(target);
                return control.Invoke((Func<object?>)(() => property.GetValue(target)));
            }

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
            var control = ControlDispatcher(target);
            if (control != null)
            {
                if (!control.InvokeRequired) { action(); return; }
                control.BeginInvoke(action);
                return;
            }

            if (IsUIThread()) { action(); return; }
            if (_uiSyncContext == null) return;

            _uiSyncContext.Post(_ =>
            {
                try { action(); }
                catch { }
            }, null);
        }
    }
}
