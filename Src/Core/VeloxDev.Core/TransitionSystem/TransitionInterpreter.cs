using VeloxDev.TimeLine;
using VeloxDev.Timing;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class TransitionInterpreterCore<
    TTransitionEffectCore,
    TPriorityCore> : TransitionInterpreterCore, ITransitionInterpreter<TPriorityCore>
    where TTransitionEffectCore : ITransitionEffect<TPriorityCore>
{
    public virtual Task Execute(
        object target,
        SamplerSet<TPriorityCore> frameSet,
        ITransitionEffect<TPriorityCore> effect,
        CancellationTokenSource cts)
    {
        return ExecuteSamplingLoopAsync(
            target,
            frameSet,
            effect,
            cts,
            easedT => frameSet.Apply(target, easedT, effect.Priority));
    }
}

public abstract class TransitionInterpreterCore<
    TTransitionEffectCore> : TransitionInterpreterCore, ITransitionInterpreter<NonPriority>
    where TTransitionEffectCore : ITransitionEffectCore
{
    /// <summary>
    /// Entry point for a host with no dispatcher priority. Sampling stays on the priority-free path
    /// (<c>frameSet.Apply(target, easedT)</c>), so <see cref="NonPriority"/> costs nothing per frame.
    /// </summary>
    public virtual Task Execute(
        object target,
        SamplerSet<NonPriority> frameSet,
        ITransitionEffect<NonPriority> effect,
        CancellationTokenSource cts)
    {
        return ExecuteSamplingLoopAsync(
            target,
            frameSet,
            effect,
            cts,
            easedT => frameSet.Apply(target, easedT));
    }
}

public abstract class TransitionInterpreterCore : IDisposable
{
    protected CancellationTokenSource? cts = null;
    private ReusableTimerWait? _wait;
    private FramePacerCore? _pacer;
    private bool _pacerResolved;

    /// <summary>The arguments handed to every effect callback of one animation.</summary>
    public virtual TransitionEventArgs Args { get; set; } = new();

    /// <summary>
    /// The host's own frame pacer, or null to wait on the default thread-pool timer.
    /// </summary>
    /// <remarks>
    /// Called at most once per interpreter, on its first frame, so an implementation may capture the current
    /// thread's dispatcher — which is the thread the loop was started on. A host that owns a UI thread returns a
    /// pacer that waits on that thread, so the loop stays there and its effect callbacks run there; see
    /// <see cref="FramePacerCore"/> for why waiting is the only allocation-free way to achieve that.
    /// </remarks>
    protected virtual FramePacerCore? CreateFramePacer() => null;

    /// <summary>
    /// Arranges for <paramref name="continuation"/> to run once, no earlier than <paramref name="interval"/> from
    /// now, or as soon as <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// The default reuses one timer for the whole loop. The timeline is what makes a frame correct, so this only
    /// decides how often the loop looks: waking late is merely a frame drawn further along.
    /// <para>
    /// Must not block the calling thread, and must invoke <paramref name="continuation"/> exactly once — including
    /// when cancelled. An implementation that simply stopped calling back would park the loop for good; nothing else
    /// would wake it.
    /// </para>
    /// </remarks>
    protected virtual void ArmNextFrame(Action continuation, TimeSpan interval, CancellationToken cancellationToken)
    {
        // 已经取消就立刻放行，不等一个间隔：否则一次 Stop 要等到下一帧才生效，而这里不必花任何代价。
        if (cancellationToken.IsCancellationRequested)
        {
            continuation();
            return;
        }

        if (!_pacerResolved)
        {
            _pacerResolved = true;
            _pacer = CreateFramePacer();
        }

        if (_pacer is not null)
        {
            _pacer.Schedule(continuation, interval, cancellationToken);
            return;
        }

        (_wait ??= new ReusableTimerWait()).Schedule(continuation, interval, cancellationToken);
    }

    /// <summary>
    /// The awaitable the sampling loop waits through. Routed through <see cref="ArmNextFrame"/> so an override is
    /// what actually decides the wait.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="System.Runtime.CompilerServices.INotifyCompletion"/> and deliberately <b>not</b>
    /// <c>ICriticalNotifyCompletion</c>, so the builder flows the caller's <see cref="ExecutionContext"/> into the
    /// continuation instead of suppressing it.
    /// <para>
    /// That does <b>not</b> keep the loop on the UI thread, which is what this remark used to claim. Restoring the
    /// <see cref="SynchronizationContext"/> is <c>Task</c>'s doing — it lives in <c>TaskAwaiter</c>, not in the
    /// builder — so a custom awaiter's continuation resumes on whatever thread completed the wait. Measured
    /// directly: an <c>INotifyCompletion</c>-only awaiter completed from the thread pool resumed on the pool, not on
    /// the context it was awaited under. A loop started on a UI thread therefore drifts to a pool thread after its
    /// first frame, and the effect's <c>Update</c>/<c>LateUpdate</c> callbacks run there; only the property writes
    /// are marshalled, because <see cref="SamplerSet{TPriorityCore}.Apply"/> goes through the inspector.
    /// </para>
    /// </remarks>
    internal readonly struct FrameWait(TransitionInterpreterCore interpreter, TimeSpan interval, CancellationToken token)
        : System.Runtime.CompilerServices.INotifyCompletion
    {
        public FrameWait GetAwaiter() => this;

        /// <summary>Always false: arming is what completes the wait, so there is nothing to short-circuit.</summary>
        public bool IsCompleted => false;

        /// <summary>Throws when the wait was ended by cancellation rather than by the interval elapsing.</summary>
        public void GetResult() => token.ThrowIfCancellationRequested();

        public void OnCompleted(Action continuation) => interpreter.ArmNextFrame(continuation, interval, token);
    }

    /// <summary>
    /// Timeline-driven continuous sampling loop: the normalized time is the distance from the running pass's anchor
    /// into the animation's <see cref="ITimeSource"/>, so <see cref="Task.Delay(TimeSpan)"/> is never a
    /// timing source — its imprecision does not affect correctness. The yield interval is capped at
    /// <c>1000 / FPS</c> ms (<c>FPS</c> is a maximum sample rate, not a frame grid): this bounds the allocation rate
    /// and prevents the loop from flooding the UI render thread when the system timer resolution is fine (e.g.
    /// <c>timeBeginPeriod(1)</c> would otherwise push <c>Task.Delay(1)</c> to ~1000Hz). Each pass samples eased time
    /// in [0,1] and applies via the frame set (which marshals the writes to the UI thread). The final frame of each
    /// pass is the exact endpoint.
    /// </summary>
    protected async Task ExecuteSamplingLoopAsync<TPriorityCore>(
        object target,
        SamplerSet<TPriorityCore> frameSet,
        ITransitionEffectCore effect,
        CancellationTokenSource cts,
        Action<double> apply)
    {
        this.cts = cts;
        frameSet.SetCancellation(cts);
        var run = frameSet.Run;
        var durationMs = effect.Duration.TotalMilliseconds;
        var foreverloop = effect.LoopTime == int.MaxValue;
        try
        {
            effect.InvokeStart(target, Args);
            while (true)
            {
                // The counter is read rather than held, so a seek can move the animation to another pass: the loop
                // has no private notion of which pass it is in. It is also the only thing that can express a pass
                // position when a zero-duration pass consumes no time at all.
                if (!foreverloop && run.Cycle > effect.LoopTime) break;

                if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                await RunPassAsync(target, effect, run, durationMs, cts, apply, forward: true);
                if (effect.IsAutoReverse)
                {
                    if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                    await RunPassAsync(target, effect, run, durationMs, cts, apply, forward: false);
                }
                run.NextCycle();
            }
            effect.InvokeCompleted(target, Args);
        }
        catch
        {
            effect.InvokeCancled(target, Args);
        }
        finally
        {
            effect.InvokeFinally(target, Args);
        }
    }

    private async Task RunPassAsync(
        object target,
        ITransitionEffectCore effect,
        TransitionRun run,
        double durationMs,
        CancellationTokenSource cts,
        Action<double> apply,
        bool forward)
    {
        var timeline = run.Timeline;

        // The pass starts at the timeline's present. This is an anchor rather than a reset, which is what lets
        // several animations share one timeline: starting a pass here cannot move any other animation, and a seek
        // is simply a different anchor.
        run.PassAnchor = timeline.Ticks;

        while (true)
        {
            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();

            if (!timeline.IsAdvancing)
            {
                // Draw the frozen position first, then park. Drawing first is what makes a seek while paused visible
                // without resuming: the wait is replaced by a nudge, so the loop wakes, draws the new position and
                // parks again. A plain pause therefore costs one frame and then no timer wake-ups at all.
                // IsAdvancing rather than IsPaused: a rate of zero freezes the timeline without pausing it, and this
                // loop has to park for that too.
                EmitFrame(target, effect, durationMs, forward, ToMilliseconds(timeline, timeline.Ticks - run.PassAnchor), apply);

                // 不是 ConfigureAwait(false)：这里等的是一个 Task，所以把它投回捕获的上下文是有意义的。
                // 注意这修不了整条循环的线程归属——见 FrameWait 的说明，自定义 awaiter 的续体不恢复
                // SynchronizationContext，循环本来就已经不在启动它的上下文上了。这一处只保证暂停等待本身
                // 不再额外把循环推向池线程。
                await timeline.WaitWhileStalledAsync(cts.Token);
                continue;
            }

            // A pass ends in exactly one place: at its far end. The timeline only ever moves forwards, so there is no
            // second bound to check.
            var elapsedTicks = timeline.Ticks - run.PassAnchor;
            if (EmitFrame(target, effect, durationMs, forward, ToMilliseconds(timeline, elapsedTicks), apply)) return;

            // Read per frame rather than once up front, so a rate cap can be tightened on a running animation.
            // FPS is a *maximum* sample rate, not a frame grid: an animation slows down when it is lowered and
            // never runs faster than it, whatever the system timer resolution happens to be.
            var sampleIntervalMs = 1000.0 / Math.Max(1, effect.FPS);
            await new FrameWait(this, TimeSpan.FromMilliseconds(sampleIntervalMs), cts.Token); // yield; the timeline is the timing authority
        }
    }

    /// <summary>
    /// Converts a tick distance into the milliseconds the pass position is expressed in, using the source's own
    /// unit rather than a framework clock's.
    /// </summary>
    private static double ToMilliseconds(ITimeSource source, long ticks)
        => TimeConversion.TicksToMilliseconds(ticks, source.TicksPerSecond);

    /// <summary>
    /// Draws one frame at <paramref name="elapsedMs"/> into the pass. Returns true when the pass has reached its
    /// far end.
    /// </summary>
    private bool EmitFrame(
        object target,
        ITransitionEffectCore effect,
        double durationMs,
        bool forward,
        double elapsedMs,
        Action<double> apply)
    {
        if (elapsedMs < 0d) elapsedMs = 0d;

        var rawT = durationMs <= 0d ? 1d : elapsedMs / durationMs;

        double easedT;
        if (rawT >= 1d)
        {
            // 每程末帧精确 = end（正向）/ start（反向）—— 不依赖 Ease(1) 是否精确为 1
            easedT = forward ? 1d : 0d;
        }
        else
        {
            var easeIn = forward ? rawT : 1d - rawT;
            // Deliberately unclamped: Back and Elastic are defined by leaving [0,1], and clamping here flattened
            // them. The eased value is handed to the samplers as-is; each sampler decides whether it can
            // extrapolate (numeric ones can) or has to pin to its endpoint.
            easedT = effect.Ease.Ease(easeIn);
        }

        effect.InvokeUpdate(target, Args);
        apply(easedT);
        effect.InvokeLateUpdate(target, Args);

        return rawT >= 1d;
    }

    public virtual void Exit()
    {
        Dispose();
    }

    public virtual void Dispose()
    {
        // 先取消：唤醒循环是它停下来的方式。pacer 后释放——即使顺序反过来也不会卡住（已释放的 pacer 会立刻唤醒
        // 续体），但先取消能让循环走的是正常的取消路径，而不是异常路径。
        var oldCts = Interlocked.Exchange(ref cts, null);
        if (oldCts != null && !oldCts.IsCancellationRequested)
        {
            oldCts.Cancel();
        }

        _pacer?.Dispose();
        _pacer = null;
        _wait?.Dispose();
        _wait = null;

        GC.SuppressFinalize(this);
    }
}
