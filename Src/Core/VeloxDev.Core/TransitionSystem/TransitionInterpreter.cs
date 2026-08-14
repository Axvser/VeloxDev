using System.Diagnostics;
using VeloxDev.TimeLine;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class TransitionInterpreterCore<
    TOutputCore,
    TTransitionEffectCore,
    TPriorityCore> : TransitionInterpreterCore, ITransitionInterpreter<TPriorityCore>
    where TTransitionEffectCore : ITransitionEffect<TPriorityCore>
    where TOutputCore : IFrameSequence<TPriorityCore>
{
    public override async Task Execute(
        object target,
        IFrameSequenceCore frameSequence,
        ITransitionEffectCore effect,
        CancellationTokenSource cts)
    {
        if (frameSequence is not IFrameSequence<TPriorityCore> cvt_frameSequence) return;
        if (effect is not ITransitionEffect<TPriorityCore> cvt_effect) return;
        await Execute(
            target,
            cvt_frameSequence,
            cvt_effect,
            cts);
    }

    public virtual async Task Execute(
        object target,
        IFrameSequence<TPriorityCore> frameSequence,
        ITransitionEffect<TPriorityCore> effect,
        CancellationTokenSource cts)
    {
        this.cts = cts;
        var indexs = GetEaseIndex(effect, frameSequence.Count);
        var frameMs = effect.Duration.TotalMilliseconds / frameSequence.Count;
        var foreverloop = effect.LoopTime == int.MaxValue;
        try
        {
            effect.InvokeStart(target, Args);
            var stopwatch = Stopwatch.StartNew();
            double frameCounter = 0;
            for (int loop = 0;
                loop <= effect.LoopTime || foreverloop;
                loop += foreverloop ? 0 : 1)
            {
                for (int index = 0; index < frameSequence.Count; index++)
                {
                    if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                    effect.InvokeUpdate(target, Args);
                    frameSequence.Update(
                        target,
                        indexs[index],
                        effect.Priority);
                    effect.InvokeLateUpdate(target, Args);
                    await WaitForFrameAsync(stopwatch, frameCounter++, frameMs, cts);
                }

                if (effect.IsAutoReverse)
                {
                    for (int index = frameSequence.Count - 1; index >= 0; index--)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index],
                            effect.Priority);
                        effect.InvokeLateUpdate(target, Args);
                        await WaitForFrameAsync(stopwatch, frameCounter++, frameMs, cts);
                    }
                }
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

    private static List<int> GetEaseIndex(
        ITransitionEffect<TPriorityCore> effect,
        int steps)
    {
        List<int> result = [];
        var endIndex = steps - 1d;
        for (int i = 0; i < steps; i++)
        {
            var ease = effect.Ease.Ease(i / endIndex);
            var index = (int)(steps * ease);
            if (index < 0) result.Add(0);
            else if (index >= steps) result.Add(steps - 1);
            else result.Add(index);
        }
        return result;
    }
}

public abstract class TransitionInterpreterCore<
    TOutputCore,
    TTransitionEffectCore> : TransitionInterpreterCore, ITransitionInterpreter
    where TTransitionEffectCore : ITransitionEffectCore
    where TOutputCore : IFrameSequence
{
    public override async Task Execute(
        object target,
        IFrameSequenceCore frameSequence,
        ITransitionEffectCore effect,
        CancellationTokenSource cts)
    {
        if (frameSequence is not IFrameSequence cvt_frameSequence) return;
        await Execute(
            target,
            cvt_frameSequence,
            effect,
            cts);
    }

    public virtual async Task Execute(
        object target,
        IFrameSequence frameSequence,
        ITransitionEffectCore effect,
        CancellationTokenSource cts)
    {
        this.cts = cts;
        var indexs = GetEaseIndex(effect, frameSequence.Count);
        var frameMs = effect.Duration.TotalMilliseconds / frameSequence.Count;
        var foreverloop = effect.LoopTime == int.MaxValue;
        try
        {
            effect.InvokeStart(target, Args);
            var stopwatch = Stopwatch.StartNew();
            double frameCounter = 0;
            for (int loop = 0;
                loop <= effect.LoopTime || foreverloop;
                loop += foreverloop ? 0 : 1)
            {
                for (int index = 0; index < frameSequence.Count; index++)
                {
                    if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                    effect.InvokeUpdate(target, Args);
                    frameSequence.Update(
                        target,
                        indexs[index]);
                    effect.InvokeLateUpdate(target, Args);
                    await WaitForFrameAsync(stopwatch, frameCounter++, frameMs, cts);
                }

                if (effect.IsAutoReverse)
                {
                    for (int index = frameSequence.Count - 1; index >= 0; index--)
                    {
                        if (cts.IsCancellationRequested || Args.Handled) throw new OperationCanceledException();
                        effect.InvokeUpdate(target, Args);
                        frameSequence.Update(
                            target,
                            indexs[index]);
                        effect.InvokeLateUpdate(target, Args);
                        await WaitForFrameAsync(stopwatch, frameCounter++, frameMs, cts);
                    }
                }
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

    private static List<int> GetEaseIndex(
        ITransitionEffectCore effect,
        int steps)
    {
        List<int> result = [];
        var endIndex = steps - 1d;
        for (int i = 0; i < steps; i++)
        {
            var ease = effect.Ease.Ease(i / endIndex);
            var index = (int)(steps * ease);
            if (index < 0) result.Add(0);
            else if (index >= steps) result.Add(steps - 1);
            else result.Add(index);
        }
        return result;
    }
}

public abstract class TransitionInterpreterCore : ITransitionInterpreterCore, IDisposable
{
    protected CancellationTokenSource? cts = null;
    public virtual TransitionEventArgs Args { get; set; } = new();

    public abstract Task Execute(object target, IFrameSequenceCore frameSequence, ITransitionEffectCore effect, CancellationTokenSource cts);

    public virtual void Exit()
    {
        Dispose();
    }

    /// <summary>
    /// 用 Stopwatch 校准的帧间隔等待：补偿 <see cref="Task.Delay(TimeSpan)"/> 的定时器抖动与
    /// 漂移，避免动画随时间累积滞后、帧间隔忽长忽短造成的卡顿。
    /// 落后于目标进度时立即返回，让动画追上计划时长。动画最终按时长精确结束。
    /// </summary>
    protected static async Task WaitForFrameAsync(Stopwatch stopwatch, double frameIndex, double frameMs, CancellationTokenSource cts)
    {
        if (frameMs <= 0) return;

        var targetMs = (frameIndex + 1) * frameMs;
        var remainingMs = targetMs - stopwatch.Elapsed.TotalMilliseconds;
        if (remainingMs <= 0) return;

        await Task.Delay(TimeSpan.FromMilliseconds(remainingMs), cts.Token);
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
