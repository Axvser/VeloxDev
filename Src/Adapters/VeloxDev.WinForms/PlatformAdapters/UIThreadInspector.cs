using System.Windows.Forms;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : UIThreadInspectorCore
    {
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static volatile bool _isAppAlive = true;

        /// <summary>
        /// 显式在 UI 线程上捕获（可选）。惰性捕获（<see cref="EnsureCaptured"/>）和
        /// 目标对象派生的 <see cref="Control"/> 编组已覆盖绝大多数场景，本方法仅作兜底。
        /// </summary>
        public static void CaptureUIThread()
        {
            if (_uiThreadId != -1) return;

            EnsureCaptured();
            if (_uiThreadId == -1)
                throw new InvalidOperationException("Must be called on WinForms UI thread before Application.Run.");
        }

        /// <summary>
        /// 惰性捕获：首次从 UI 线程触碰本类时自动记录 <see cref="SynchronizationContext"/>
        /// 与线程 id。后台线程（SynchronizationContext 非 WinForms 上下文）调用时无副作用。
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
        /// 目标对象（<see cref="Control"/>）优先：控件自身知道它所属的 UI 线程，
        /// 从任意线程用 <see cref="Control.Invoke(Delegate)"/> / <see cref="Control.BeginInvoke(Delegate)"/>
        /// 编组即可，后台首启动也无需任何显式捕获。
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

        public override List<object?> ProtectedInterpolate(object target, Func<List<object?>> interpolate)
        {
            var control = ControlDispatcher(target);
            if (control != null)
            {
                if (!control.InvokeRequired) return interpolate();
                return (List<object?>)control.Invoke(interpolate);
            }

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
