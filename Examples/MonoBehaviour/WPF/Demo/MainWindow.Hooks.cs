// ---------------------------------------------------------------------------------------------------------------------
// MonoBehaviour Part ↓
//
// The demo. One class, one channel, and the five hooks the generator declares.
//
// The subject is a pair of balls integrating under the same gravity through the same Step(), fed by the two pumps
// every channel runs:
//
//   Update      — variable dt, measured off the clock, never compensated. One call per frame, on the channel's
//                 update thread, before LateUpdate.
//   FixedUpdate — a constant dt entered as a step size, and every step a stall costs is owed and repaid. Called on
//                 the channel's fixed-update thread, as many times per wake-up as the debt requires.
//
// Expanding semi-implicit Euler from rest gives  Height − (g/2)·t²  =  −(g/2)·Σdt²  exactly, for any dt sequence.
// So each ball's Drift measures its own pump's Σdt², and its EffectiveDt = Σdt²/Σdt recovers the interval it was
// actually driven at. The two readouts are therefore a measurement of the loops made by the physics — which is the
// whole demo, and why the two balls being indistinguishable at 60 fps is a result rather than a broken demo.
//
// Nothing here talks to the window. Each hook ends by publishing one immutable report and, if the call is worth a
// line, enqueueing one small struct; the window polls. A Dispatcher.Invoke in a hook would be swallowed by the
// engine's per-hook catch — the old version of this file did that every frame — and a failure would look like a
// dead loop with nothing written anywhere to say why.
// ---------------------------------------------------------------------------------------------------------------------

using System.Threading;
using VeloxDev.TimeLine;

namespace Demo;

[MonoBehaviour(DemoChannel.Name)]
public partial class MainWindow
{
    private readonly DemoState _state = new();

    /// <summary>Frame ordinal of the last Update, so LateUpdate can prove it ran after it (update thread only).</summary>
    private int _updateSeenFrame;
    private bool _updateSeen;

    /// <summary>Last step ordinal delivered to FixedUpdate, so a call's batch is a difference of ordinals (fixed thread only).</summary>
    private int _lastFixedIndex;

    /// <summary>Set by a hook that slept; the next call on that thread is the one carrying the consequence.</summary>
    private bool _updateSlept;
    private bool _fixedSlept;

    /// <summary>One per pump, each written only by its own thread — hence two fields and not one.</summary>
    private int _latchedRestartUpdate;
    private int _latchedRestartFixed;

    #region Lifecycle

    partial void Awake()
    {
        Interlocked.Increment(ref _state.AwakeCount);
        _state.AwakeAtUpdateCount = Volatile.Read(ref _state.UpdateCount);
        _state.AwakeFrameOrdinal = MonoBehaviourManager.TotalFrames(DemoChannel.Name);
        _state.Record(HookKind.Awake, _state.AwakeAtUpdateCount, 0, 0, false, HookNote.None);
    }

    partial void Start()
    {
        Interlocked.Increment(ref _state.StartCount);
        _state.StartAtUpdateCount = Volatile.Read(ref _state.UpdateCount);
        _state.Record(HookKind.Start, _state.StartAtUpdateCount, 0, 0, false, HookNote.None);
    }

    #endregion

    #region Frames

    partial void Update(FrameEventArgs e)
    {
        var index = Interlocked.Increment(ref _state.UpdateCount);
        LatchRestartFromUpdate();

        // 卡顿按钮:在钩子里睡。欠的是一帧 —— 采样发生在钩子之前,所以这段停顿不会出现在本帧的 DeltaTime 上,
        // 而是全部记在下一帧的那一个 D 里。两帧都写进日志,因为「尖峰晚一帧到账」正是这里值得看的东西。
        var note = HookNote.None;
        var hitch = Interlocked.Exchange(ref _state.UpdateHitchMs, 0);
        if (hitch > 0)
        {
            Thread.Sleep(hitch);
            note = HookNote.Slept;
            _updateSlept = true;
        }
        else if (_updateSlept)
        {
            _updateSlept = false;
            note = HookNote.AfterSleep;
        }

        var ball = _state.UpdateBall;
        ball.Step(e.DeltaTime.TotalSeconds);
        if (ball.HasLanded) ball.RestartFall();

        // Handled 关掉的是本帧剩下的 Update 和整个 LateUpdate。引擎在钩子之前先看它,所以这里是一次跨线程可见的写。
        // 这一行为什么能直接计数,而不是"大概跳过":LateUpdate 的循环体第一句就是看 Handled,而本通道只有这一个
        // 行为,所以 Update 置上它就等于本帧没有 LateUpdate —— 是确定的,不是推断。
        var handled = Volatile.Read(ref _state.HandledRequested) != 0;
        if (handled)
        {
            e.Handled = true;
            Interlocked.Increment(ref _state.SkippedLateUpdates);
        }

        _updateSeen = true;
        _updateSeenFrame = index;

        _state.PublishUpdate(Report(ball, index, 1, e.DeltaTime.TotalMilliseconds, note));
        RecordIf(HookKind.Update, index, e.DeltaTime.TotalMilliseconds, 1, handled, note);
    }

    partial void LateUpdate(FrameEventArgs e)
    {
        var index = Interlocked.Increment(ref _state.LateUpdateCount);

        // 一个必须一直为 0 的不变量:本帧的 Update 没跑过,LateUpdate 就不可能跑。计数不靠信任,靠这一行。
        if (_updateSeen && _updateSeenFrame == Volatile.Read(ref _state.UpdateCount))
        {
            _updateSeen = false;
        }
        else
        {
            Interlocked.Increment(ref _state.OrderViolations);
        }

        // 这个跟随环唯一的作用是当 Handled 的受害者:它由 LateUpdate 定位,所以 Update 一置 Handled 它就冻住,
        // 而两只球照走。不编「摄像机跟随」的故事 —— 单行为下它在 LateUpdate 里的落点和在 Update 里完全一样,
        // 因为两者之间没有任何东西动过这只球。
        Volatile.Write(ref _state.LateUpdateHeight, _state.UpdateBall.Height);
        Volatile.Write(ref _state.LateUpdateWallMs, Environment.TickCount64);

        RecordIf(HookKind.LateUpdate, index, e.DeltaTime.TotalMilliseconds, 1, false, HookNote.None);
    }

    partial void FixedUpdate(FrameEventArgs e)
    {
        var index = Interlocked.Increment(ref _state.FixedUpdateCount);
        LatchRestartFromFixed();

        // 这一批推了几步 = 步序号的差。不能用 TotalTime 的差:改步长会重置累计但保留已交付计数,
        // 于是 TotalTime 会跳变,而步序号不会。
        var batch = index - _lastFixedIndex;
        _lastFixedIndex = index;

        // 在这条线程上睡,欠的是步而不是帧:醒来后下一次 Advance 会把攒下的步一次推完(受 MaxStepsPerCall
        // 限速),所以屏幕上不动、日志里是一串 batch > 1,而不是一个巨大的 DeltaTime。
        var note = HookNote.None;
        var hitch = Interlocked.Exchange(ref _state.FixedHitchMs, 0);
        if (hitch > 0)
        {
            Thread.Sleep(hitch);
            note = HookNote.Slept;
            _fixedSlept = true;
        }
        else if (_fixedSlept)
        {
            _fixedSlept = false;
            note = HookNote.AfterSleep;
        }

        var ball = _state.FixedBall;
        ball.Step(e.DeltaTime.TotalSeconds);
        if (ball.HasLanded) ball.RestartFall();

        _state.PublishFixed(Report(ball, index, batch, e.DeltaTime.TotalMilliseconds, note));
        RecordIf(HookKind.FixedUpdate, index, e.DeltaTime.TotalMilliseconds, batch, false, note);
    }

    #endregion

    #region Plumbing

    /// <summary>
    /// Snapshots one ball, its pump, and how much time that pump has not handed over yet.
    /// </summary>
    /// <remarks>
    /// How many steps the fixed sampler owes, and how much of a step it has already banked, are private to the
    /// channel — the public face of its clock is position, rate and epoch, and nothing else. So each pump measures
    /// its own shortfall instead: the bus's position now, minus the time it has handed to its ball so far. What is
    /// left is the part it has not driven.
    /// <para>
    /// It has to be taken <em>here</em>, inside the hook, rather than by subtracting the two published drive clocks
    /// in the window: those two are only as fresh as their own last call, and at a low frame rate the update side's
    /// is most of a second stale — a difference that swamps the quantity being looked at. Measured at each pump's
    /// own instant, the stale part cancels exactly and what is left is the remainder plus the debt.
    /// </para>
    /// <para>
    /// The absolute value also carries a constant — how long the channel's clock existed before <c>Start</c>
    /// re-anchored it — so only the difference between the two pumps is shown.
    /// </para>
    /// </remarks>
    private BallReport Report(BallBody ball, int index, int batch, double dtMilliseconds, HookNote note)
    {
        var bus = MonoBehaviourManager.Bus(DemoChannel.Name);
        var unspentMs = bus is null
            ? 0
            : (bus.Position - TimeSpan.FromSeconds(ball.DriveTime)).TotalMilliseconds;

        return new BallReport(ball.Height,
                              ball.FallTime,
                              ball.DriveTime,
                              ball.EffectiveDt,
                              ball.Drift,
                              dtMilliseconds,
                              unspentMs,
                              index,
                              batch,
                              ball.Falls,
                              Thread.CurrentThread.Name ?? "—",
                              note,
                              Environment.TickCount64);
    }

    /// <summary>
    /// Records a hook call, or counts it as omitted.
    /// </summary>
    /// <remarks>
    /// With the log holding every frame the panel is a wall of text — at 30 fps and a 16 ms step a channel runs
    /// about 120 hooks a second. So the default is the notable calls only, and every call left out is counted and
    /// displayed: a filtered log that does not say it is filtered reads exactly like a loop that stopped.
    /// </remarks>
    private void RecordIf(HookKind kind, int index, double dtMilliseconds, int batch, bool handled, HookNote note)
    {
        var everyHook = Volatile.Read(ref _state.RecordEveryHook) != 0;
        if (everyHook || note != HookNote.None || kind is HookKind.Awake or HookKind.Start)
            _state.Record(kind, index, dtMilliseconds, batch, handled, note);
        else
            Interlocked.Increment(ref _state.OmittedCount);
    }

    /// <summary>
    /// Puts the update ball back on the line if the window asked for it.
    /// </summary>
    /// <remarks>
    /// Two methods rather than one, and a field each: a shared latch would let whichever pump got there first
    /// swallow the request before the other saw it. This way each thread touches only its own ball and only its
    /// own latch, and the two landings may be a frame apart — which is exactly why <c>DriveTime</c> survives a
    /// restart while <c>FallTime</c> does not.
    /// </remarks>
    private void LatchRestartFromUpdate()
    {
        var generation = Volatile.Read(ref _state.RestartGeneration);
        if (generation == _latchedRestartUpdate) return;
        _latchedRestartUpdate = generation;
        _state.UpdateBall.RestartFall();
    }

    private void LatchRestartFromFixed()
    {
        var generation = Volatile.Read(ref _state.RestartGeneration);
        if (generation == _latchedRestartFixed) return;
        _latchedRestartFixed = generation;
        _state.FixedBall.RestartFall();
    }

    #endregion
}
