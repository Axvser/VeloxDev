using System.Runtime.CompilerServices;

namespace VeloxDev.TransitionSystem.Abstractions;

/// <summary>
/// One <see cref="Timer"/> for a whole loop, re-armed per wait, and one cancellation registration for the whole
/// loop rather than one per wait.
/// </summary>
/// <remarks>
/// The point is what is <em>not</em> allocated: <c>await Task.Delay(interval, token)</c> builds a fresh
/// <c>DelayPromise</c> and registers a fresh cancellation callback on every wait, while this reuses one timer and one
/// registration for the whole loop. The sampling loop waits once per animation per frame, so that was the last
/// allocation left on an otherwise allocation-free path — <c>ReusableTimerWaitTests</c> measures the difference.
/// <para>
/// It is the same <see cref="Timer"/> <c>Task.Delay</c> ends up in, so this changes what a wait costs and not when it
/// lands. <b>One loop per instance</b>: a single pending continuation is held.
/// </para>
/// </remarks>
internal sealed class ReusableTimerWait : IDisposable
{
    private readonly Timer _timer;

    private Action? _continuation;
    private CancellationTokenRegistration _registration;
    private bool _tokenBound;
    private bool _disposed;

    internal ReusableTimerWait()
        => _timer = new Timer(static state => ((ReusableTimerWait)state!).Fire(), this, Timeout.Infinite, Timeout.Infinite);

    /// <summary>
    /// Arranges for <paramref name="continuation"/> to run once, no earlier than <paramref name="interval"/> from
    /// now, or as soon as <paramref name="token"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// Must not block the calling thread. Calling from several threads at once is not supported — one loop owns one
    /// instance, and a second pending continuation would replace the first.
    /// </remarks>
    internal void Schedule(Action continuation, TimeSpan interval, CancellationToken token)
    {
        if (continuation is null)
        {
            throw new ArgumentNullException(nameof(continuation));
        }

        // 已经释放或已取消：立刻唤醒。等待侧会看到令牌并抛出，从而正常收尾——比在这里抛更安静，
        // 也不会把一个已经走到尾声的循环变成异常路径。
        if (_disposed || !TryBindToken(token))
        {
            continuation();
            return;
        }

        Volatile.Write(ref _continuation, continuation);

        if (interval <= TimeSpan.Zero)
        {
            Fire();
            return;
        }

        _timer.Change(interval, Timeout.InfiniteTimeSpan);

        // 关掉「令牌已登记、续体还没挂上」时被取消的窗口：否则那一次唤醒会丢，循环要等到 interval 之后才
        // 发现自己被取消了。（丢一次不至于卡死，但一段取消要等 16ms 才生效是没必要的。）
        if (token.IsCancellationRequested)
        {
            Fire();
        }
    }

    /// <summary>Waits for the next instant. Throws <see cref="OperationCanceledException"/> when cancelled first.</summary>
    internal Wait Await(TimeSpan interval, CancellationToken token) => new(this, interval, token);

    /// <remarks>
    /// Implements <see cref="INotifyCompletion"/> and deliberately <b>not</b> <c>ICriticalNotifyCompletion</c>, so
    /// the builder flows the caller's <see cref="ExecutionContext"/> into the continuation. It does not restore a
    /// <see cref="SynchronizationContext"/> — see <c>TransitionInterpreterCore.FrameWait</c>, which measures that.
    /// </remarks>
    internal readonly struct Wait(ReusableTimerWait wait, TimeSpan interval, CancellationToken token) : INotifyCompletion
    {
        public Wait GetAwaiter() => this;

        /// <summary>Always false: scheduling is what completes the wait, so there is nothing to short-circuit.</summary>
        public bool IsCompleted => false;

        /// <summary>Throws when the wait was ended by cancellation rather than by the interval elapsing.</summary>
        public void GetResult() => token.ThrowIfCancellationRequested();

        public void OnCompleted(Action continuation) => wait.Schedule(continuation, interval, token);
    }

    /// <remarks>
    /// Registers the cancellation callback once for the life of this instance. Per wait would be the obvious thing
    /// and is exactly what is being removed: the registration allocates a callback object each time. One loop waits
    /// on one token, so one registration is enough — and once cancelled it stays cancelled, so a bound token never
    /// needs re-checking.
    /// <para>
    /// <c>Register</c> rather than <c>UnsafeRegister</c>: the latter does not exist on netstandard2.0 or
    /// netframework4.6.1, and the ExecutionContext capture it would avoid is paid once per animation instead of once
    /// per frame, which is not what this class is about.
    /// </para>
    /// </remarks>
    private bool TryBindToken(CancellationToken token)
    {
        if (_tokenBound)
        {
            return !token.IsCancellationRequested;
        }

        _tokenBound = true;
        if (!token.CanBeCanceled || token.IsCancellationRequested)
        {
            return !token.IsCancellationRequested;
        }

        _registration = token.Register(static state => ((ReusableTimerWait)state!).Fire(), this);
        return true;
    }

    private void Fire()
    {
        // 先停表，再唤醒：续体可能马上安排下一次等待，那时 Change 会把它重新装上。
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Interlocked.Exchange(ref _continuation, null)?.Invoke();
    }

    /// <remarks>
    /// A parked wait is <b>resumed</b>, not dropped. Dropping it would leave the sampling loop suspended with
    /// nothing left that could ever wake it, and nothing would report it — the animation would simply stop, forever,
    /// with no exception and no frame. Waking it lets the loop run its own tail: it sees a cancelled token or a
    /// disposed target and finishes.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registration.Dispose();
        _timer.Dispose();

        Interlocked.Exchange(ref _continuation, null)?.Invoke();
    }
}
