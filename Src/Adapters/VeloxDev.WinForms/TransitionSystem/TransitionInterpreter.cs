using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    public class TransitionInterpreter() : TransitionInterpreterCore<TransitionEffect>
    {
        /// <inheritdoc />
        /// <remarks>
        /// The wait cannot be a <see cref="System.Windows.Forms.Timer"/>: Windows synthesizes <c>WM_TIMER</c> only
        /// while the message queue holds nothing else, and hands out one such idle moment at a time to whichever
        /// expired timer asks first. A drag answers every mouse message with a synchronous repaint, so the queue
        /// always holds the next one and the clock stops for as long as the drag lasts; the rest of the time every
        /// timer in the process shares the same idle moments. The frame is therefore posted to the target's own
        /// window — or to a live form when the target has none, such as a clock — with <see cref="Control.BeginInvoke(Delegate)"/>
        /// from a thread-pool timer: posted messages arrive
        /// in order without waiting for the queue to empty, and the continuation still resumes on the control's
        /// thread — so the property writes stay direct and <c>Update</c>/<c>LateUpdate</c> stay on that thread,
        /// which is what a pacer is for. Starting on the target's thread is still the condition, so a first
        /// animation started elsewhere keeps the documented fallback, where each write marshals itself.
        /// </remarks>
        protected override FramePacerCore? CreateFramePacer(object target, IThreadAffinity affinity)
            // 非控件目标（如 WorkflowFlowClock）没有自己的窗口，采样仍借宿主的活窗体回到 UI 线程；
            // 没有活窗体时 pacer 拿到 null，按默认的线程池路径放行。
            => affinity.IsCurrent(target) ? new PostedFramePacer(UIThreadInspector.DispatcherFor(target)) : null;

        private sealed class PostedFramePacer : FramePacerCore
        {
            private readonly Control? _target;
            private readonly Action _fire;
            private System.Threading.Timer? _timer;
            private volatile bool _disposed;

            public PostedFramePacer(Control? target)
            {
                _target = target;
                // 缓存的续体：每次投递都现写一个闭包就是每帧一次分配，而采样路径正是不想有它。
                _fire = Fire;
            }

            /// <inheritdoc />
            protected override void Arm(TimeSpan interval)
            {
                // 已释放还要放行挂着的续体，而不是丢掉：停在一次永远等不到的唤醒上，宿主看到的是「不报错、也再没有帧」。
                if (_disposed)
                {
                    Fire();
                    return;
                }

                var timer = _timer ??= CreateTimer();
                timer.Change(interval, Timeout.InfiniteTimeSpan);
            }

            /// <inheritdoc />
            protected override void Disarm()
            {
                if (_disposed) return;
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            private System.Threading.Timer CreateTimer()
                => new(static state => ((PostedFramePacer)state!).OnDue(), this, Timeout.Infinite, Timeout.Infinite);

            // 定时器在工作线程到期：把这一帧投回控件的线程，续体因此在那里恢复。
            // 目标不是控件、或窗口已经消失，就地放行 —— 与默认的线程池等待同形，属性写入本来各自编组。
            private void OnDue()
            {
                if (_disposed) return;

                if (_target is { IsDisposed: false, IsHandleCreated: true } control)
                {
                    try
                    {
                        control.BeginInvoke(_fire);
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        // 句柄在检查与投递之间消失：无句柄抛 InvalidOperationException，已释放的控件抛它的派生类 ObjectDisposedException。
                    }
                }

                _fire();
            }

            /// <inheritdoc />
            public override void Dispose()
            {
                _disposed = true;
                var timer = _timer;
                _timer = null;

                // 基类先停表并放行挂着的续体，之后这块表才轮到被释放。
                base.Dispose();
                timer?.Dispose();
            }
        }
    }
}
