using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading;

namespace VeloxDev.TransitionSystem.Abstractions;

public abstract class TransitionCore
{
    /// <summary>
    /// Creates a snapshot and marks it as the root of its segment chain.
    /// </summary>
    public static TSnapshot Create<TSnapshot>() where TSnapshot : StateSnapshotCore, new()
    {
        var snapshot = new TSnapshot();
        snapshot.AsRoot();
        return snapshot;
    }

    /// <summary>
    /// Cancels every animation running on <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// Cancellation is a signal: the animation stops at its next await, so it may still be releasing its scheduler
    /// when this returns. That is fine for a following mutually-exclusive <c>Execute</c> — it queues on the
    /// scheduler's own gate behind the cancelled animation and therefore lands one frame later at worst.
    /// </remarks>
    public static void Exit<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
    {
        var gate = TransitionSchedulerCore.GetTargetLock(target);
        List<TransitionRun> drained = [];
        gate.Wait();
        try
        {
            // Only the bookkeeping runs under the target lock: taking the tokens out of the active set is what has
            // to be atomic against an animation entering. Cancelling them is left until after the release — Cancel
            // runs the registered callbacks synchronously on this thread, so doing it here would block the
            // dispatcher on a UI-thread Exit and deadlock on a callback that re-enters Exit for the same target.
            foreach (var scheduler in CollectSchedulers(target, IncludeMutual, IncludeNoMutual))
            {
                drained.AddRange(((TransitionSchedulerCore)scheduler).DrainActive());
            }
        }
        finally
        {
            gate.Release();
        }

        TransitionSchedulerCore.CancelDrained(drained);
    }

    /// <summary>
    /// Freezes the timeline of every animation running on <paramref name="target"/>, without ending any of them.
    /// </summary>
    /// <remarks>
    /// The time spent paused is excluded from the animation rather than merely skipped: the clock stops accruing, so
    /// a pause of any length leaves the remaining duration unchanged. A paused animation also costs no timer
    /// wake-ups — its sampling loop is parked on a signal.
    /// </remarks>
    public static void Pause<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
        => ApplyToRuns(target, IncludeMutual, IncludeNoMutual, static run => run.Timeline.Pause());

    /// <summary>
    /// Lets a paused animation on <paramref name="target"/> run again, at the rate it was last set to.
    /// </summary>
    public static void Resume<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
        => ApplyToRuns(target, IncludeMutual, IncludeNoMutual, static run => run.Timeline.Resume());

    /// <summary>
    /// Changes how fast <paramref name="target"/>'s animations run, without moving their position. Zero pauses.
    /// </summary>
    /// <remarks>
    /// The rate multiplies elapsed time, so it cannot be changed mid-pass without the position jumping — the
    /// timeline rebases first, which is why a change during playback is seamless. A rate given while paused is
    /// remembered and takes effect on <see cref="Resume{T}"/>.
    /// <para>
    /// Time only ever moves forwards: there is no reverse playback, and a negative rate is rejected rather than
    /// clamped. To go back to a point, <see cref="Seek{T}(T, TimeSpan, bool, bool)"/> there.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is negative.</exception>
    public static void SetRate<T>(T target, double rate, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
        => ApplyToRuns(target, IncludeMutual, IncludeNoMutual, run => run.Timeline.SetRate(rate));

    /// <summary>
    /// Moves <paramref name="target"/>'s animations to <paramref name="position"/> within the pass that is running,
    /// keeping the rate.
    /// </summary>
    /// <remarks>
    /// A position past the end of the pass simply finishes it, and a negative one is pinned to its start. Seeking
    /// while paused draws the new position without resuming.
    /// </remarks>
    public static void Seek<T>(T target, TimeSpan position, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
        => ApplyToRuns(target, IncludeMutual, IncludeNoMutual, run => SeekRun(run, position));

    /// <summary>
    /// Moves <paramref name="target"/>'s animations to <paramref name="position"/> within the pass numbered
    /// <paramref name="cycle"/>, keeping the rate.
    /// </summary>
    /// <remarks>
    /// The pass counter is what an absolute timeline needs to name a pass at all, since passes are not always
    /// separable by time — a zero-duration one consumes none. Jumping past the last pass the effect allows finishes
    /// the animation, exactly as running off the end would.
    /// </remarks>
    public static void Seek<T>(T target, int cycle, TimeSpan position, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
        => ApplyToRuns(target, IncludeMutual, IncludeNoMutual, run =>
        {
            run.Cycle = cycle;
            SeekRun(run, position);
        });

    /// <summary>
    /// Which pass the animation on <paramref name="target"/> is playing, or zero when nothing is running.
    /// </summary>
    public static int Cycle<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
    {
        var runs = TransitionSchedulerCore.CollectRuns(target, IncludeMutual, IncludeNoMutual);
        return runs.Count == 0 ? 0 : (int)runs[0].Cycle;
    }

    /// <summary>
    /// True when every animation running on <paramref name="target"/> is paused, and there is at least one.
    /// </summary>
    public static bool IsPaused<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
    {
        var runs = TransitionSchedulerCore.CollectRuns(target, IncludeMutual, IncludeNoMutual);
        if (runs.Count == 0) return false;

        foreach (var run in runs)
        {
            if (!run.Timeline.IsPaused) return false;
        }
        return true;
    }

    /// <summary>
    /// How far into its current pass the animation on <paramref name="target"/> is, or <see cref="TimeSpan.Zero"/>
    /// when nothing is running.
    /// </summary>
    public static TimeSpan Position<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
    {
        var runs = TransitionSchedulerCore.CollectRuns(target, IncludeMutual, IncludeNoMutual);
        return runs.Count == 0 ? TimeSpan.Zero : PositionOf(runs[0]);
    }

    /// <summary>
    /// The rate the animation on <paramref name="target"/> is set to, or zero when nothing is running. Never negative.
    /// Remembered across a pause, so this is what playback resumes at rather than what it is doing right now.
    /// </summary>
    public static double Rate<T>(T target, bool IncludeMutual = true, bool IncludeNoMutual = false)
        where T : class
    {
        var runs = TransitionSchedulerCore.CollectRuns(target, IncludeMutual, IncludeNoMutual);
        return runs.Count == 0 ? 0d : runs[0].Timeline.Rate;
    }

    private static void SeekRun(TransitionRun run, TimeSpan position)
        => run.PassAnchor = run.Timeline.Now - TransitionTime.MsToTicks(position.TotalMilliseconds);

    private static TimeSpan PositionOf(TransitionRun run)
    {
        var elapsedMs = TransitionTime.TicksToMs(run.Timeline.Now - run.PassAnchor);
        return elapsedMs > 0d ? TimeSpan.FromMilliseconds(elapsedMs) : TimeSpan.Zero;
    }

    /// <summary>
    /// Hands one control operation to every run on the target.
    /// </summary>
    /// <remarks>
    /// Deliberately without the target lock that <see cref="Exit{T}"/> takes. That lock exists to serialize leaving
    /// against entering — removing a registration atomically with respect to an animation starting. This neither
    /// adds nor removes one, so there is nothing to serialize, and a run that starts in the middle of the sweep is
    /// exactly as controllable as one that started before it.
    /// </remarks>
    private static void ApplyToRuns(object target, bool includeMutual, bool includeNoMutual, Action<TransitionRun> operation)
    {
        foreach (var run in TransitionSchedulerCore.CollectRuns(target, includeMutual, includeNoMutual))
        {
            operation(run);
        }
    }

    /// <summary>
    /// Rejects declared paths that can never animate: a path whose leaf holds a reference type with neither a
    /// custom interpolator nor a registered sampler.
    /// </summary>
    /// <remarks>
    /// A value type is exempt — one can still be assembled member by member, and the assembler reports "cannot" by
    /// returning null, which stays a skip. This is unrelated to <see cref="TransitionProperty.UnreadablePath"/>,
    /// where a path is valid but does not match the current target's runtime type: that stays a per-frame skip.
    /// </remarks>
    internal static void RejectUnsampleablePaths(IFrameState state)
    {
        foreach (var property in state.Values.Keys)
        {
            if (state.TryGetInterpolator(property, out var custom) && custom is not null) continue;
            if (InterpolatorCore.TryGetInterpolator(property.PropertyType, out var registered) && registered is not null) continue;
            if (property.PropertyType.IsValueType) continue;

            throw new TransitionPathUnsampleableException(property);
        }
    }

    private static List<ITransitionSchedulerCore> CollectSchedulers(object target, bool includeMutual, bool includeNoMutual)
    {
        List<ITransitionSchedulerCore> schedulers = [];
        if (includeMutual && TransitionSchedulerCore.TryGetMutualScheduler(target, out var mutualScheduler))
        {
            schedulers.Add(mutualScheduler!);
        }
        if (includeNoMutual && TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var nomutualSchedulers))
        {
            schedulers.AddRange(nomutualSchedulers);
        }
        return schedulers;
    }

    internal static void AddNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
    {
        // GetValue installs the per-target set atomically, and the set itself is concurrent: an animation
        // registers itself from whichever thread started it, and several can be running at the same time.
        // Emptying the entry is left to the weak table — removing it here would race with a concurrent add
        // and silently drop that animation's scheduler, leaving it uncancellable.
        var schedulers = TransitionSchedulerCore.NoMutualSchedulers.GetValue(
            target,
            static _ => new ConcurrentDictionary<ITransitionSchedulerCore, byte>());

        foreach (var scheduler in schedulerCores)
        {
            schedulers.TryAdd(scheduler, 0);
        }
    }
    internal static void RemoveNoMutual(object target, IEnumerable<ITransitionSchedulerCore> schedulerCores)
    {
        if (TransitionSchedulerCore.NoMutualSchedulers.TryGetValue(target, out var schedulers))
        {
            foreach (var scheduler in schedulerCores)
            {
                schedulers.TryRemove(scheduler, out _);
            }
        }
    }
}

public class TransitionCore<
    T,
    TStateCore,
    TEffectCore,
    TInterpolatorCore,
    TUIThreadInspectorCore,
    TTransitionInterpreterCore,
    TPriorityCore> : StateSnapshotCore<T>
    where T : class
    where TStateCore : IFrameState, new()
    where TEffectCore : ITransitionEffect<TPriorityCore>, new()
    where TInterpolatorCore : InterpolatorCore, new()
    where TUIThreadInspectorCore : IUIThreadInspector<TPriorityCore>, new()
    where TTransitionInterpreterCore : class, ITransitionInterpreter<TPriorityCore>, new()
{
    protected TStateCore state = new();
    protected TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? root;
    protected TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? next = null;
    protected TEffectCore effect = new();
    protected TInterpolatorCore interpolator = new();

    public TStateCore GetState() => state;

    /// <summary>
    /// Starts a batch of snapshots on the same target. Non-mutual by default: they run concurrently
    /// and do not cancel each other — deliberately the opposite of the single-shot <c>Execute</c>.
    /// </summary>
    public static void Execute(
        T target,
        IEnumerable<TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>> values,
        bool CanMutualTask = false)
    {
        foreach (var snapshot in values)
        {
            snapshot.CoreValidate();
            snapshot.CoreExecute(target, CanMutualTask);
        }
    }

    internal override void AsRoot()
    {
        root = this;
    }

    internal override void CoreValidate()
    {
        // Walks the chain the way CoreExecute does, so every segment's paths are checked and not only the root's.
        for (var node = root ?? this; node is not null; node = node.next)
        {
            TransitionCore.RejectUnsampleablePaths(node.state);
        }
    }

    internal override IFrameState CoreRecordState()
    {
        return state.Clone();
    }

    internal override async void CoreExecute(object target, bool CanMutualTask = true, TransitionTimeline? timeline = null)
    {
        if (target is not T)
            throw new InvalidDataException($"The target is not a {typeof(T).Name} !");

        root ??= this;

        // Starting an animation is bookkeeping, done under the target's control-plane lock: an Exit racing with it
        // either cancels this animation or happens entirely before it — never in between, where it would leave the
        // animation running but untracked, and therefore unstoppable.
        CancellationTokenSource cts;
        TransitionRun run;
        TransitionSchedulerCore coreScheduler;
        ITransitionSchedulerCore scheduler;
        List<TransitionRun> superseded = [];
        var gate = TransitionSchedulerCore.GetTargetLock(target);
        await gate.WaitAsync();
        try
        {
            scheduler = TransitionSchedulerCore<TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>.FindOrCreate(target, CanMutualTask);
            if (CanMutualTask)
            {
                // A mutually-exclusive animation supersedes whatever is running on the target. Drained here, under
                // the same lock as the registration below — leaving stays atomic against a concurrent entering —
                // but cancelled after the release (see CancelDrained), and before this animation's own token is
                // tracked, so it cannot cancel itself.
                superseded.AddRange(((TransitionSchedulerCore)scheduler).DrainActive());
            }

            // The run carries the token source and the timeline together, and is what makes this animation reachable
            // by a later Exit or a control call. A timeline handed in is shared with whatever else was given the
            // same one; the default is this animation's own.
            run = new TransitionRun(timeline ?? new TransitionTimeline());
            cts = run.Cts;

            // Registered for the whole animation, not just the segment currently executing: between segments the
            // animation sits idle in an Await gap, and an Exit() during that gap has to find and cancel it too —
            // and, for the same reason, a Pause() has to find it there.
            coreScheduler = (TransitionSchedulerCore)scheduler;
            coreScheduler.Track(run);

            if (!CanMutualTask)
            {
                TransitionCore.AddNoMutual(target, [scheduler]);
            }
        }
        finally
        {
            gate.Release();
        }

        TransitionSchedulerCore.CancelDrained(superseded);

        Queue<InterpolatorCore> interpolators = [];
        Queue<TimeSpan> spans = [];
        Queue<ITransitionEffectCore> effects = [];
        Queue<IFrameState> states = [];
        int Count = 0;

        TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore>? currentNode = root;
        do
        {
            interpolators.Enqueue(currentNode.interpolator);
            spans.Enqueue(currentNode.delay);
            var newEffect = currentNode.effect.Clone();
            effects.Enqueue(newEffect);
            states.Enqueue(currentNode.state);
            Count++;
            currentNode = currentNode.next;
        }
        while (currentNode is not null);

        try
        {
            while (!cts.IsCancellationRequested && Count > 0)
            {
                try
                {
                    await DelayWhilePausedAsync(run.Timeline, spans.Dequeue(), cts.Token);
                }
                catch (OperationCanceledException) { return; }
                await scheduler.Execute(interpolators.Dequeue(), states.Dequeue(), effects.Dequeue(), cts);
                Count--;
            }
        }
        finally
        {
            // Unregister only when the whole animation ends (completed or cancelled). Doing it per segment — off
            // each segment's effect Finally — drops the scheduler while the animation is still alive in an Await
            // gap, so Exit() can no longer find it and the remaining segments run anyway.
            coreScheduler.Untrack(run);
            if (!CanMutualTask)
            {
                TransitionCore.RemoveNoMutual(target, [coreScheduler]);
            }
        }
    }

    /// <summary>
    /// Waits out the delay between two segments without letting a pause consume it.
    /// </summary>
    /// <remarks>
    /// The remaining time is recomputed against the real clock after every wake, so the animation does not lose the
    /// part of its delay that passed while it was paused. The floor on the subtraction keeps a timer that returns a
    /// hair early from leaving the loop repeating the same sub-millisecond remainder.
    /// </remarks>
    private static async Task DelayWhilePausedAsync(TransitionTimeline timeline, TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;

        // 同一个等待对象贯穿整个段间等待。这里不是热路径（一个动画的段数很少），复用只是为了不让同一个子系统里
        // 留两种等待方式。
        using var wait = new ReusableTimerWait();

        var remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            // Parked for as long as the animation is paused: a paused animation must not spend its delay either.
            // Every wake re-checks, so a nudge — which only exists to redraw a seeked frame — costs one pass and
            // then parks again.
            while (timeline.IsPaused && !ct.IsCancellationRequested)
            {
                var gate = timeline.PauseGate;
                if (gate is null) break;
                await gate.Task.ConfigureAwait(false);
            }

            var before = TransitionTime.Now;
            await wait.Await(remaining, ct);
            var elapsedMs = Math.Max(TransitionTime.TicksToMs(TransitionTime.Now - before), 0.5d);
            remaining -= TimeSpan.FromMilliseconds(elapsedMs);
        }
    }

    internal override T1 CoreThen<T1>()
    {
        var newNode = new T1();
        if (newNode is not TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> converted)
        {
            throw new InvalidOperationException($"The current TransitionCore is not of type {typeof(T1).Name}.");
        }
        converted.root = root;
        next = converted;
        return newNode;
    }
    internal override T1 CoreAwaitThen<T1>(TimeSpan timeSpan)
    {
        var newNode = new T1();
        if (newNode is not TransitionCore<T, TStateCore, TEffectCore, TInterpolatorCore, TUIThreadInspectorCore, TTransitionInterpreterCore, TPriorityCore> converted)
        {
            throw new InvalidOperationException($"The current TransitionCore is not of type {typeof(T1).Name}.");
        }
        converted.root = root;
        converted.delay = timeSpan;
        next = converted;
        return newNode;
    }
    internal override T1 CoreInterpolator<T1, TTarget1, TValue>(Expression<Func<TTarget1, TValue>> propertyLambda, ISampler interpolator)
    {
        if (this is not T1 result)
        {
            throw new InvalidOperationException($"The current TransitionCore is not of type {typeof(T1).Name}.");
        }
        state.SetInterpolator(propertyLambda, interpolator);
        return result;
    }
    protected override T1 CoreEffect<T1, T2>(T2 effect)
    {
        if (this is not T1 result)
        {
            throw new InvalidOperationException($"The current TransitionCore is not of type {typeof(T1).Name}.");
        }
        if (effect is not TEffectCore convertedEffect)
        {
            throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
        }
        this.effect = convertedEffect;
        return result;
    }
    protected override T1 CoreEffect<T1, T2>(Action<T2> effectSetter)
    {
        if (this is not T1 result)
        {
            throw new InvalidOperationException($"The current TransitionCore is not of type {typeof(T1).Name}.");
        }
        var newEffect = new T2();
        effectSetter.Invoke(newEffect);
        if (newEffect is not TEffectCore convertedEffect)
        {
            throw new InvalidOperationException($"The effect setter did not return an effect of type {typeof(TEffectCore).Name}.");
        }
        effect = convertedEffect;
        return result;
    }
}
