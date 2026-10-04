// ---------------------------------------------------------------------------------------------------------------------
// Tickable 部分 ↓
//
// 演示：一个类、一个通道、生成器声明的五个钩子。
//
// 主体是一对球，用同一个 Step()、同样的重力积分，由每条通道都有的两条泵驱动：
//   Update      —— 可变 dt，按挂钟量，不做补偿；每帧一次，在通道的 update 线程上，先于 LateUpdate。
//   FixedUpdate —— 固定 dt 作为步长，卡顿欠下的步会被补还；在 fixed-update 线程上，每次唤醒按欠账调用多次。
//
// 从静止展开半隐式欧拉得 Height − (g/2)·t² = −(g/2)·Σdt²，对任意 dt 序列都精确。
// 于是每只球的 Drift 就是它那条泵的 Σdt²，EffectiveDt = Σdt²/Σdt 还原它实际被驱动的间隔 ——
// 这就是整个演示，也是两球在 60fps 下无法区分是结论、而不是演示坏了的原因。
//
// 这里不直接和窗口说话：每个钩子最后发布一份不可变报告，值得记一行就再入一个小结构体，由窗口轮询。
// 钩子里的 Dispatcher.Invoke 会被引擎的 per-hook catch 吞掉，失败看起来就像一个不写任何东西的死循环。
// ---------------------------------------------------------------------------------------------------------------------

using System.Threading;
using VeloxDev.TimeLine;

namespace Demo;

[Tickable(DemoChannel.Name)]
public partial class MainWindow
{
    private readonly DemoState _state = new();

    // 上一次 Update 的帧序号，LateUpdate 用它证明自己跑在其后（仅 update 线程）。
    private int _updateSeenFrame;
    private bool _updateSeen;

    // 交付给 FixedUpdate 的最后步序号，一次调用的 batch 就是序号之差（仅 fixed 线程）。
    private int _lastFixedIndex;

    // 由睡过的钩子置上；该线程的下一次调用就是承担后果的那一次。
    private bool _updateSlept;
    private bool _fixedSlept;

    // 每条泵一个，只由各自的线程写，所以是两个字段而不是一个。
    private int _latchedRestartUpdate;
    private int _latchedRestartFixed;

    #region Lifecycle

    partial void Awake()
    {
        Interlocked.Increment(ref _state.AwakeCount);
        _state.AwakeAtUpdateCount = Volatile.Read(ref _state.UpdateCount);
        _state.AwakeFrameOrdinal = TickManager.TotalFrames(DemoChannel.Name);
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

    // 快照一只球、它的泵，以及那条泵还没交付的时间。
    // 固定采样器欠多少步、已经攒下多少，是通道的私有状态（它对外只有位置、速率与 epoch），所以每条泵自己量缺口：
    // 总线此刻的位置减去它已交给球的时间，剩下的就是还没驱动的部分。
    // 必须在钩子内部量，而不是在窗口里把两个已发布的驱动时钟相减：那两个只和各自最后一次调用一样新，
    // 低帧率下 update 侧能旧掉近一秒，差值会淹没被观察的量。在各自瞬间测量，陈旧部分恰好抵消，剩下余数加欠账。
    // 绝对值还带一个常数（通道时钟在 Start 重新锚定前存在了多久），所以只显示两条泵之差。
    private BallReport Report(BallBody ball, int index, int batch, double dtMilliseconds, HookNote note)
    {
        var bus = TickManager.Bus(DemoChannel.Name);
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

    // 记录一次钩子调用，或把它计为省略。
    // 若日志保存每一帧，面板就是一面字墙——30fps、16ms 步长下每秒约 120 次钩子。
    // 所以默认只记值得注意的调用，省略的每一次都计数并显示：一个不说自己被过滤了的日志，读起来和一个停下来的循环一模一样。
    private void RecordIf(HookKind kind, int index, double dtMilliseconds, int batch, bool handled, HookNote note)
    {
        var everyHook = Volatile.Read(ref _state.RecordEveryHook) != 0;
        if (everyHook || note != HookNote.None || kind is HookKind.Awake or HookKind.Start)
            _state.Record(kind, index, dtMilliseconds, batch, handled, note);
        else
            Interlocked.Increment(ref _state.OmittedCount);
    }

    // 窗口要求时把 update 球放回线上。
    // 两个方法而不是一个、各一个字段：共享闩锁会让先到的泵吞掉请求。
    // 这样每个线程只碰自己的球和自己的闩锁，两个落地可能差一帧——正是 DriveTime 能跨重启保住、FallTime 不能的原因。
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
