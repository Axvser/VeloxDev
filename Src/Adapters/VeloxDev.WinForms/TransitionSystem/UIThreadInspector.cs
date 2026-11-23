using System.Windows.Forms;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class UIThreadInspector : TransitionHostBase<NonPriority>
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

            System.Windows.Forms.Application.ApplicationExit += (_, _) => _isAppAlive = false;
        }

        // 目标优先用自身的 Control：它知道自己的 UI 线程，BeginInvoke 不必先抓 SynchronizationContext。
        // 视觉元素不是窗口，投递交给它所属的 VisualHost；动画目标仍是元素本身，互斥动画按元素区分。
        private static Control? ControlDispatcher(object target)
            => target switch
            {
                Control control when control.IsHandleCreated => control,
                VeloxDev.WinForms.Xaml.Visuals.VisualElement { Surface: { IsHandleCreated: true } surface } => surface,
                _ => null,
            };

        /// <inheritdoc />
        public override bool IsAlive => _isAppAlive;

        /// <inheritdoc />
        public override ThreadRef ThreadFor(object target)
        {
            EnsureCaptured();
            return ThreadRef.From(_uiSyncContext);
        }

        /// <summary>
        /// Asked of the target first: WinForms exposes no way to name a Control's thread, but the Control answers
        /// whether the caller is on it, which is the same question.
        /// </summary>
        protected override bool IsCurrentFor(object target, ThreadRef thread)
            => ControlDispatcher(target) is { } control
                ? !control.InvokeRequired
                : base.IsCurrentFor(target, thread);

        /// <inheritdoc />
        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<SynchronizationContext>(out var context)
               && ReferenceEquals(SynchronizationContext.Current, context);

        /// <inheritdoc />
        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
        {
            if (ControlDispatcher(target) is { } control)
            {
                control.BeginInvoke(action);
                return true;
            }

            if (!thread.TryGet<SynchronizationContext>(out var context)) return false;

            context.Post(_ => action(), null);
            return true;
        }
    }
}
