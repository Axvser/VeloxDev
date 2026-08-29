using System.Diagnostics;
using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class TransitionInterpreterCore<
    TTransitionEffectCore,
    TPriorityCore> : TransitionInterpreterCore, ITransitionInterpreter<TPriorityCore>
    where TTransitionEffectCore : ITransitionEffect<TPriorityCore>
{
    public override Task Execute(
        object target,
        SamplerSet frameSet,
        ITransitionEffectCore effect,
        CancellationTokenSource cts)
    {
        if (effect is not ITransitionEffect<TPriorityCore> cvt_effect) return Task.CompletedTask;
        return Execute(target, frameSet, cvt_effect, cts);
    }

    public virtual Task Execute(
        object target,
        SamplerSet frameSet,
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
    TTransitionEffectCore> : TransitionInterpreterCore, ITransitionInterpreter
    where TTransitionEffectCore : ITransitionEffectCore
{
    public override Task Execute(
        object target,
        SamplerSet frameSet,
        ITransitionEffectCore effect,
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

public abstract class TransitionInterpreterCore : ITransitionInterpreterCore, IDisposable
{
    protected CancellationTokenSource? cts = null;
    public virtual TransitionEventArgs Args { get; set; } = new();

    public abstract Task Execute(object target, SamplerSet frameSet, ITransitionEffectCore effect, CancellationTokenSource cts);

    /// <summary>
    /// Stopwatch-driven continuous sampling loop: the normalized time is derived from elapsed wall-clock time each
    /// iteration, so <see cref="Task.Delay(TimeSpan)"/> is never a timing source — its imprecision does not affect
    /// correctness. The yield interval is capped at <c>1000 / FPS</c> ms (<c>FPS</c> is a maximum sample rate, not a
    /// frame grid): this bounds the allocation rate and prevents the loop from flooding the UI render thread when the
    /// system timer resolution is fine (e.g. <c>timeBeginPeriod(1)</c> would otherwise push <c>Task.Delay(1)</c> to
    /// ~1000Hz). Each pass samples eased time in [0,1] and applies via the frame set (which marshals the writes to
    /// the UI thread). The final frame of each pass is the exact endpoint.
    /// </summary>
    protected async Task ExecuteSamplingLoopAsync(
        object target,
        SamplerSet frameSet,
        ITransitionEffectCore effect,
        CancellationTokenSource cts,
        Action<double> apply)
    {
        this.cts = cts;
        frameSet.SetCancellation(cts);
        var durationMs = effect.Duration.TotalMilliseconds;
        var foreverloop = effect.LoopTime == int.MaxValue;
        var sampleIntervalMs = 1000.0 / Math.Max(1, effect.FPS); // FPS = maximum sample rate cap
        try
        {
            effect.InvokeStart(target, Args);
            var stopwatch = Stopwatch.StartNew();
            var cycle = 0;
            while (foreverloop || cycle <= effect.LoopTime)
            {
                if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                await RunPassAsync(target, effect, stopwatch, durationMs, sampleIntervalMs, cts, apply, forward: true);
                if (effect.IsAutoReverse)
                {
                    if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                    await RunPassAsync(target, effect, stopwatch, durationMs, sampleIntervalMs, cts, apply, forward: false);
                }
                cycle++;
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
        Stopwatch stopwatch,
        double durationMs,
        double sampleIntervalMs,
        CancellationTokenSource cts,
        Action<double> apply,
        bool forward)
    {
        var passStartMs = stopwatch.Elapsed.TotalMilliseconds;
        while (true)
        {
            if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();

            var rawT = durationMs <= 0 ? 1 : (stopwatch.Elapsed.TotalMilliseconds - passStartMs) / durationMs;
            if (rawT < 0) rawT = 0;

            double easedT;
            if (rawT >= 1)
            {
                // 每程末帧精确 = end（正向）/ start（反向）—— 不依赖 Ease(1) 是否精确为 1
                easedT = forward ? 1 : 0;
            }
            else
            {
                var easeIn = forward ? rawT : 1 - rawT;
                easedT = effect.Ease.Ease(easeIn);
                if (easedT < 0) easedT = 0;
                else if (easedT > 1) easedT = 1; // 保留 GetEaseIndex 的 Back/Elastic 越界钳制
            }

            effect.InvokeUpdate(target, Args);
            apply(easedT);
            effect.InvokeLateUpdate(target, Args);

            if (rawT >= 1) return;
            await Task.Delay(TimeSpan.FromMilliseconds(sampleIntervalMs), cts.Token); // yield; Stopwatch is the timing authority
        }
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
