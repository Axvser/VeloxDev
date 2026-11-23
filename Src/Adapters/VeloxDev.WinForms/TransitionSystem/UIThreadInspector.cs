using System.Windows.Forms;
using VeloxDev.Threading;

namespace VeloxDev.TransitionSystem
{
    /// <summary>
    /// The WinForms transition host: it resolves the thread that owns a target and carries work to that thread.
    /// </summary>
    /// <remarks>
    /// A target that is a <see cref="Control"/> — or a visual element with a live surface — answers for itself. A
    /// target that is not a window (a view model, a clock) has no thread of its own, so the host posts through the
    /// first live form instead. The captured <see cref="SynchronizationContext"/> is only the last resort, because
    /// its marshaling window exists only while a message loop is pumping it.
    /// <para>
    /// Nothing here throws. A dead queue, a disposed target and an exited application are all reported as a refused
    /// post (<see langword="false"/>), never as an exception: the write path runs once per frame, where an exception
    /// escaping an asynchronous continuation becomes an unhandled exception on the thread pool.
    /// </para>
    /// </remarks>
    public class UIThreadInspector : TransitionHostBase<NonPriority>
    {
        // 捕获状态由首次在 UI 线程上投递时建立。_uiThreadId 才是「我是不是 UI 线程」的依据：线程池线程可能从
        // ExecutionContext 里流回同一个 SynchronizationContext，引用相等不代表线程相同。
        private static readonly object CaptureGate = new();
        private static SynchronizationContext? _uiSyncContext;
        private static int _uiThreadId = -1;
        private static volatile bool _isAppAlive = true;
        private static bool _exitSubscribed;

        // 非控件目标没有自己的窗口，改投到仍活着的窗体；缓存避免每帧枚举 Application.OpenForms。
        private static Control? _fallbackControl;

        /// <summary>
        /// Optionally captures the UI thread explicitly. Lazy capture (see <see cref="EnsureCaptured"/>) and
        /// target-derived <see cref="Control"/> marshaling already cover most scenarios; this method is only a fallback.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not called on a WinForms UI thread before <c>Application.Run</c>.</exception>
        public static void CaptureUIThread()
        {
            if (Volatile.Read(ref _uiThreadId) != -1) return;

            EnsureCaptured();
            if (Volatile.Read(ref _uiThreadId) == -1)
                throw new InvalidOperationException("Must be called on WinForms UI thread before Application.Run.");
        }

        // 首次从 UI 线程进入时记录上下文与线程 id；同一条 UI 线程换了上下文（新的消息循环）则改绑。
        // 线程池线程上的上下文通常是 ExecutionContext 流回来的旧对象，不能据它改绑。
        private static void EnsureCaptured()
        {
            var current = SynchronizationContext.Current;
            if (current?.GetType().Name != "WindowsFormsSynchronizationContext") return;
            if (Thread.CurrentThread.IsThreadPoolThread) return;

            var threadId = Thread.CurrentThread.ManagedThreadId;
            lock (CaptureGate)
            {
                if (_uiThreadId == threadId)
                {
                    // 同一线程、新的上下文：上一轮消息循环结束之后又开了一轮。
                    if (!ReferenceEquals(_uiSyncContext, current))
                    {
                        _uiSyncContext = current;
                        _isAppAlive = true;
                    }
                    else if (!_isAppAlive && Application.MessageLoop)
                    {
                        // 同一个上下文、新的消息循环：ApplicationExit 之后又跑起来了。
                        _isAppAlive = true;
                    }
                    SubscribeExit();
                    return;
                }

                // 另一条还活着的 UI 线程不是本宿主支持的形态，别改绑；已退出的那条允许被替换。
                if (_uiThreadId != -1 && _isAppAlive) return;

                _uiSyncContext = current;
                Volatile.Write(ref _uiThreadId, threadId);
                _isAppAlive = true;
                SubscribeExit();
            }
        }

        private static void SubscribeExit()
        {
            if (_exitSubscribed) return;
            _exitSubscribed = true;
            Application.ApplicationExit += OnApplicationExit;
        }

        // ApplicationExit 可以重复触发；只把它当作「队列不会再被泵」的信号。
        private static void OnApplicationExit(object? sender, EventArgs e) => _isAppAlive = false;

        // 目标自己的 Control 优先；视觉元素用它的 VisualHost。目标本身是窗口但句柄已经没了，就不改投：
        // 那条 UI 线程已经消失，改投别的线程只会把属性写到错误的地方。
        // 非窗口目标（如 WorkflowFlowClock）没有自己的线程，退到仍活着的窗体。
        private static Control? ControlDispatcher(object target)
        {
            Control? own = target switch
            {
                Control control => control,
                Easel.Visuals.VisualElement { Surface: { } surface } => surface,
                _ => null,
            };

            if (own is not null)
            {
                return own is { IsHandleCreated: true, IsDisposed: false } ? own : null;
            }

            return LiveOpenForm();
        }

        // Application.OpenForms 在工作线程上不一定可读；读不到就当没有可投递的窗体。
        private static Control? LiveOpenForm()
        {
            var cached = Volatile.Read(ref _fallbackControl);
            if (cached is { IsHandleCreated: true, IsDisposed: false }) return cached;

            try
            {
                var forms = Application.OpenForms;
                for (var i = 0; i < forms.Count; i++)
                {
                    if (forms[i] is { IsHandleCreated: true, IsDisposed: false } form)
                    {
                        Volatile.Write(ref _fallbackControl, form);
                        return form;
                    }
                }
            }
            catch (Exception)
            {
                // 上下文已失效或跨线程访问被拒：没有可用的窗体，交给下面的上下文路径。
            }

            Volatile.Write(ref _fallbackControl, null);
            return null;
        }

        // 供解释器的 pacer 用：非控件目标也要借活窗体回到 UI 线程，否则采样会停在池线程上。
        internal static Control? DispatcherFor(object target) => ControlDispatcher(target);

        /// <inheritdoc />
        public override bool IsAlive => _isAppAlive;

        /// <inheritdoc />
        public override ThreadRef ThreadFor(object target)
        {
            EnsureCaptured();
            return ThreadRef.From(Volatile.Read(ref _uiSyncContext));
        }

        /// <remarks>
        /// Asked of the target first: WinForms exposes no way to name a Control's thread, but the Control answers
        /// whether the caller is on it, which is the same question.
        /// </remarks>
        protected override bool IsCurrentFor(object target, ThreadRef thread)
        {
            if (ControlDispatcher(target) is { } control)
            {
                try
                {
                    return !control.InvokeRequired;
                }
                catch (InvalidOperationException)
                {
                    // 句柄在检查之后消失：退回线程序号判断。
                }
            }

            return base.IsCurrentFor(target, thread);
        }

        /// <inheritdoc />
        protected override bool IsCurrentThread(ThreadRef thread)
            => thread.TryGet<SynchronizationContext>(out var context)
               && ReferenceEquals(SynchronizationContext.Current, context)
               && Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref _uiThreadId);

        /// <inheritdoc />
        protected override bool PostCore(object target, ThreadRef thread, Action action, NonPriority priority)
        {
            // 应用已退出：队列不会再被泵，投进去的动作永远不会跑，等待者会挂死。直接拒收。
            if (!_isAppAlive) return false;

            if (ControlDispatcher(target) is { } control)
            {
                try
                {
                    control.BeginInvoke(action);
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    // 控件在检查与投递之间被释放；已释放的控件抛它的派生类。
                }
                catch (InvalidOperationException)
                {
                    // 句柄在检查与投递之间消失：无句柄的 BeginInvoke 抛的就是它。
                }
                catch (System.ComponentModel.InvalidAsynchronousStateException)
                {
                    // 目标线程已经不再泵消息：WinForms 拒绝把一个不会有消息循环接收的调用排进去。
                }
            }

            // 上下文可能已经失效（编组窗口没有句柄，或消息循环已结束），换捕获到的那一个再试。
            if (thread.TryGet<SynchronizationContext>(out var pinned) && pinned is not null)
            {
                if (PostToContext(pinned, action)) return true;

                // 捕获的那个上下文投不进去了：它不再是可用的 UI 线程，允许下一次从 UI 线程重新捕获。
                InvalidateCapture(pinned);
            }

            var captured = Volatile.Read(ref _uiSyncContext);
            if (captured is not null && !ReferenceEquals(captured, pinned))
            {
                if (PostToContext(captured, action)) return true;
                InvalidateCapture(captured);
            }

            return false;
        }

        // 只在被拒的正是捕获到的那个上下文时清空；别的线程/目标带来的上下文不影响本宿主的捕获。
        private static void InvalidateCapture(SynchronizationContext context)
        {
            lock (CaptureGate)
            {
                if (!ReferenceEquals(_uiSyncContext, context)) return;

                _uiSyncContext = null;
                Volatile.Write(ref _uiThreadId, -1);
            }
        }

        // 上下文拒绝投递必须报 false：说 true 会让等待者永远等一个不会执行的动作。
        // 动作真的跑过之后再抛的异常属于应用本身，照常往外传，不能被当成本底拒绝。
        private static bool PostToContext(SynchronizationContext context, Action action)
        {
            var state = new PostState(action);
            try
            {
                context.Post(static s => ((PostState)s!).Invoke(), state);
                return true;
            }
            catch (ObjectDisposedException) when (!state.Invoked)
            {
                return false;
            }
            catch (InvalidOperationException) when (!state.Invoked)
            {
                return false;
            }
            catch (System.ComponentModel.InvalidAsynchronousStateException) when (!state.Invoked)
            {
                return false;
            }
        }

        // 记录动作是否已经开始：用来把「上下文拒绝投递」和「动作自己抛异常」区分开。
        private sealed class PostState(Action action)
        {
            private volatile bool _invoked;

            public bool Invoked => _invoked;

            public void Invoke()
            {
                _invoked = true;
                action();
            }
        }
    }
}
