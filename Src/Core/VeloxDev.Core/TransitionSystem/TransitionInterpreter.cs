using VeloxDev.TimeLine;

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

    /// <summary>The arguments handed to every effect callback of one animation.</summary>
    public virtual TransitionEventArgs Args { get; set; } = new();

    /// <summary>
    /// Timeline-driven continuous sampling loop: the normalized time is the distance from the running pass's anchor
    /// into the animation's <see cref="TransitionTimeline"/>, so <see cref="Task.Delay(TimeSpan)"/> is never a
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
        run.PassAnchor = timeline.Now;

        while (true)
        {
            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();

            var gate = timeline.PauseGate;
            if (gate is not null)
            {
                // Draw the frozen position first, then park. Drawing first is what makes a seek while paused visible
                // without resuming: the gate is replaced by a nudge, so the loop wakes, draws the new position and
                // parks again. A plain pause therefore costs one frame and then no timer wake-ups at all.
                EmitFrame(target, effect, durationMs, forward, timeline.Now - run.PassAnchor, apply);
                await gate.Task.ConfigureAwait(false);
                continue;
            }

            // A pass ends in exactly one place: at its far end. The timeline only ever moves forwards, so there is no
            // second bound to check.
            var elapsedTicks = timeline.Now - run.PassAnchor;
            if (EmitFrame(target, effect, durationMs, forward, elapsedTicks, apply)) return;

            // Read per frame rather than once up front, so a rate cap can be tightened on a running animation.
            // FPS is a *maximum* sample rate, not a frame grid: an animation slows down when it is lowered and
            // never runs faster than it, whatever the system timer resolution happens to be.
            var sampleIntervalMs = 1000.0 / Math.Max(1, effect.FPS);
            await Task.Delay(TimeSpan.FromMilliseconds(sampleIntervalMs), cts.Token); // yield; the timeline is the timing authority
        }
    }

    /// <summary>
    /// Draws one frame at <paramref name="elapsedTicks"/> into the pass. Returns true when the pass has reached its
    /// far end.
    /// </summary>
    private bool EmitFrame(
        object target,
        ITransitionEffectCore effect,
        double durationMs,
        bool forward,
        long elapsedTicks,
        Action<double> apply)
    {
        var elapsedMs = TransitionTime.TicksToMs(elapsedTicks);
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
        var oldCts = Interlocked.Exchange(ref cts, null);
        if (oldCts != null && !oldCts.IsCancellationRequested)
        {
            oldCts.Cancel();
        }
        GC.SuppressFinalize(this);
    }
}
