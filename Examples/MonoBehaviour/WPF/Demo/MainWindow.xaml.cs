using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
using VeloxDev.TimeLine;

namespace Demo;

/// <summary>
/// The window half of the demo.
/// </summary>
/// <remarks>
/// Nothing here knows what a frame is. It polls, formats and draws; every number on screen was produced by a hook
/// in <c>MainWindow.Hooks.cs</c> and arrived as a published report.
/// <para>
/// The polling is not a compromise, it is the only shape that works: the engine runs each hook on a channel thread
/// and swallows anything thrown out of one, so a <c>Dispatcher.Invoke</c> from inside a hook would be the one
/// failure mode with no symptom — the loop would simply stop drawing and nothing would be logged.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private const int LogCapacity = 2000;
    private const int LogTail = 80;

    /// <summary>Half the ball, in pixels — what a height has to clear to sit on the ground line.</summary>
    private const double BallRadius = 9;

    private const double FollowerRadius = 15;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Queue<HookEntry> _log = new();

    private int _polls;
    private bool _registered;
    private int _restartGeneration;

    /// <summary>Wall clock and both ordinals at the opening of the current delivery window.</summary>
    private long _windowMs;
    private int _windowUpdate;
    private int _windowFixed;

    /// <summary>Fixed steps per Update frame, and frames per second, both as measured — not as configured.</summary>
    private double _stepsPerFrame;
    private double _framesPerSecond;

    public MainWindow()
    {
        InitializeComponent();

        _poll.Tick += (_, _) => Poll();

        Loaded += (_, _) =>
        {
            // 先注册,再配帧率,最后启动。注册只入队一个动作,由更新循环在它的第一帧体里取走并触发 Awake/Start,
            // 所以顺序不影响「生命周期先于第一帧」这条结论 —— 参数里的 -1 也让注册永远不会覆写下面这个帧率。
            InitializeMonoBehaviour();
            _registered = true;
            MonoBehaviourManager.SetTargetFPS(30, DemoChannel.Name);
            MonoBehaviourManager.Start(DemoChannel.Name);
            _poll.Start();
        };

        Closing += (_, _) =>
        {
            _poll.Stop();
            if (_registered) CloseMonoBehaviour();
            // 不 await:窗口关掉之后进程就结束了,两条泵都是后台线程。等一个 Join 只会让关闭看起来卡了一下。
            _ = MonoBehaviourManager.StopAsync(DemoChannel.Name);
        };
    }

    #region Controls

    private void Fps60(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTargetFPS(60, DemoChannel.Name);

    private void Fps30(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTargetFPS(30, DemoChannel.Name);

    private void Fps10(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTargetFPS(10, DemoChannel.Name);

    private void Fps2(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTargetFPS(2, DemoChannel.Name);

    private void Step16(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetFixedUpdateInterval(16, DemoChannel.Name);

    private void Step33(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetFixedUpdateInterval(33, DemoChannel.Name);

    private void Step50(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetFixedUpdateInterval(50, DemoChannel.Name);

    private void Rate0(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTimeScale(0f, DemoChannel.Name);

    private void Rate1(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTimeScale(1f, DemoChannel.Name);

    private void Rate2(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTimeScale(2f, DemoChannel.Name);

    private void Rate4(object sender, RoutedEventArgs e) => MonoBehaviourManager.SetTimeScale(4f, DemoChannel.Name);

    private void HitchUpdate(object sender, RoutedEventArgs e) => Interlocked.Exchange(ref _state.UpdateHitchMs, 300);

    private void HitchFixed(object sender, RoutedEventArgs e) => Interlocked.Exchange(ref _state.FixedHitchMs, 200);

    private void HitchForgive(object sender, RoutedEventArgs e) => Interlocked.Exchange(ref _state.FixedHitchMs, 1500);

    private void StartChannel(object sender, RoutedEventArgs e) => MonoBehaviourManager.Start(DemoChannel.Name);

    private void StopChannel(object sender, RoutedEventArgs e) => _ = MonoBehaviourManager.StopAsync(DemoChannel.Name);

    private void PauseChannel(object sender, RoutedEventArgs e) => MonoBehaviourManager.Pause(DemoChannel.Name);

    private void ResumeChannel(object sender, RoutedEventArgs e) => MonoBehaviourManager.Resume(DemoChannel.Name);

    /// <summary>
    /// Takes the behaviour off the channel. The next registration re-runs Awake and Start on it.
    /// </summary>
    /// <remarks>
    /// This pair is the sharpest contrast in the lifecycle and the one nothing else in the demo can show:
    /// registration drives Awake and Start again on a fresh wrapper for the same object, while stopping and
    /// starting the channel does not (the counters stay put). The buttons are enabled one at a time so the
    /// sequence is the one that demonstrates it — registering twice without unregistering would fire Awake twice
    /// and say nothing about the lifecycle.
    /// </remarks>
    private void Unregister(object sender, RoutedEventArgs e)
    {
        CloseMonoBehaviour();
        _registered = false;
        BtnUnregister.IsEnabled = false;
        BtnRegister.IsEnabled = true;
    }

    private void RegisterAgain(object sender, RoutedEventArgs e)
    {
        MonoBehaviourManager.RegisterBehaviour(this, DemoChannel.Name);
        _registered = true;
        BtnUnregister.IsEnabled = true;
        BtnRegister.IsEnabled = false;
    }

    /// <summary>
    /// Asks both pumps to put their own ball back on the line.
    /// </summary>
    /// <remarks>
    /// Two clicks of this are never needed in normal running — landing already restarts a fall. What it is for is
    /// re-synchronising the pair after a slow frame rate has moved them a long way apart, so a fresh comparison
    /// can be watched from a known starting point.
    /// </remarks>
    private void RestartBalls(object sender, RoutedEventArgs e) => _restartGeneration = Interlocked.Increment(ref _state.RestartGeneration);

    private void ClearLog(object sender, RoutedEventArgs e)
    {
        while (_state.Log.TryDequeue(out _)) { }
        _log.Clear();
        Log.Text = string.Empty;
    }

    private void EveryHookChanged(object sender, RoutedEventArgs e)
        => Volatile.Write(ref _state.RecordEveryHook, ChkEveryHook.IsChecked == true ? 1 : 0);

    private void HandledChanged(object sender, RoutedEventArgs e)
        => Volatile.Write(ref _state.HandledRequested, ChkHandled.IsChecked == true ? 1 : 0);

    #endregion

    #region Polling

    private void Poll()
    {
        _polls++;
        var update = _state.UpdateReport;
        var fixedReport = _state.FixedReport;

        DrainLog();
        MeasureDelivery();
        DrawStage(update, fixedReport);
        DrawReadouts(update, fixedReport);
        DrawLog();
        DrawPayload(update, fixedReport);
    }

    /// <summary>Moves whatever the hooks queued into the bounded view model, once per poll.</summary>
    private void DrainLog()
    {
        while (_state.Log.TryDequeue(out var entry))
        {
            _log.Enqueue(entry);
            if (_log.Count > LogCapacity) _log.Dequeue();
        }
    }

    /// <summary>
    /// Measures how many fixed steps each Update frame was given, over a wall-clock second.
    /// </summary>
    /// <remarks>
    /// This is the one claim counters alone cannot make: that the fixed pump runs on its own cadence rather than the
    /// frame rate's. Two ordinals and a wall clock give <c>Δstep / Δframe</c> — the delivery this channel actually
    /// produced, with neither the target frame rate nor the step size taken on trust. It is the number to watch while
    /// the frame-rate buttons are pressed: it barely moves when they do.
    /// <para>
    /// Only updated when both ordinals advanced inside the window. A paused channel, or a frame rate low enough to
    /// starve the window, would otherwise divide by zero — and a stale value is honest where an infinity is not.
    /// </para>
    /// </remarks>
    private void MeasureDelivery()
    {
        var now = Environment.TickCount64;
        var update = Volatile.Read(ref _state.UpdateCount);
        var fixedCount = Volatile.Read(ref _state.FixedUpdateCount);

        if (_windowMs == 0)
        {
            _windowMs = now;
            _windowUpdate = update;
            _windowFixed = fixedCount;
            return;
        }

        var elapsed = now - _windowMs;
        if (elapsed < 1000) return;

        var frames = update - _windowUpdate;
        if (frames > 0)
        {
            _stepsPerFrame = (fixedCount - _windowFixed) / (double)frames;
            _framesPerSecond = frames * 1000.0 / elapsed;
        }

        _windowMs = now;
        _windowUpdate = update;
        _windowFixed = fixedCount;
    }

    private void DrawStage(BallReport update, BallReport fixedReport)
    {
        Place(BallUpdate, 110, update.Height, BallRadius);
        Place(BallFixed, 210, fixedReport.Height, BallRadius);

        // 跟随环由 LateUpdate 定位,所以勾上 Handled 它就冻在原地,而两只球继续走。这是它在单行为演示里的全部作用。
        Place(Follower, 110, Volatile.Read(ref _state.LateUpdateHeight), FollowerRadius);

        var centreUpdate = CentreY(update.Height);
        var centreFixed = CentreY(fixedReport.Height);
        var half = Math.Abs(centreUpdate - centreFixed) / 2 * DemoChannel.Mirror;
        var mid = (centreUpdate + centreFixed) / 2;
        half = Math.Min(half, DemoChannel.StageHeight / 2);
        MirrorBar.Y1 = mid - half;
        MirrorBar.Y2 = mid + half;
    }

    /// <summary>The centre of a ball resting at <paramref name="height"/> metres, in stage pixels.</summary>
    private static double CentreY(double height) => DemoChannel.GroundY - BallRadius - height * DemoChannel.PixelsPerMetre;

    private static void Place(System.Windows.Shapes.Shape shape, double centreX, double height, double radius)
    {
        Canvas.SetLeft(shape, centreX - radius);
        Canvas.SetTop(shape, CentreY(height) - radius);
    }

    private void DrawReadouts(BallReport update, BallReport fixedReport)
    {
        var awake = Volatile.Read(ref _state.AwakeCount);
        var start = Volatile.Read(ref _state.StartCount);
        var updateCount = Volatile.Read(ref _state.UpdateCount);
        var lateCount = Volatile.Read(ref _state.LateUpdateCount);
        var fixedCount = Volatile.Read(ref _state.FixedUpdateCount);

        // Awake/Start 写在 Update 计数上:那一刻计数还是 0,这就是「生命周期跑在第一帧体之前」的证据本身,
        // 而不是一句注释。它们由主线程队列驱动,队列在采样之前被排空。
        Lifecycle.Text =
            $"生命周期  Awake ×{awake} (触发时 Update 次数 {Volatile.Read(ref _state.AwakeAtUpdateCount)}," +
            $" 引擎帧号 {Volatile.Read(ref _state.AwakeFrameOrdinal)})" +
            $"  ·  Start ×{start} (Update 次数 {Volatile.Read(ref _state.StartAtUpdateCount)})" +
            $"  ·  Update ×{updateCount}  ·  LateUpdate ×{lateCount}  ·  FixedUpdate ×{fixedCount}";

        var lateAge = Volatile.Read(ref _state.LateUpdateWallMs) == 0
            ? double.NaN
            : (Environment.TickCount64 - Volatile.Read(ref _state.LateUpdateWallMs)) / 1000.0;

        Ordering.Text =
            $"次序      首帧 Awake → Start → Update → LateUpdate" +
            $"  ·  LateUpdate 早于 Update 的帧 {Volatile.Read(ref _state.OrderViolations)} (必须为 0)" +
            $"  ·  Handled 跳过 LateUpdate 的帧 {Volatile.Read(ref _state.SkippedLateUpdates)}" +
            $"  ·  最后一句 LateUpdate 距今 {lateAge:F1} s";

        UpdatePump.Text =
            $"Update    帧 #{update.Index,6}  D {update.DtMilliseconds,8:F2} ms" +
            $"  Σdt²/Σdt {update.EffectiveDt * 1000,8:F2} ms" +
            $"  偏差 {update.Drift,10:F4} m  第 {update.Falls} 次下落" +
            $"  ·  实测 {_framesPerSecond,5:F1} Hz  {update.Pump}";

        FixedPump.Text =
            $"Fixed     步 #{fixedReport.Index,6}  本批 {fixedReport.Batch,2} 步" +
            $"  h {fixedReport.DtMilliseconds,8:F2} ms" +
            $"  Σdt²/Σdt {fixedReport.EffectiveDt * 1000,8:F2} ms" +
            $"  偏差 {fixedReport.Drift,10:F4} m  第 {fixedReport.Falls} 次下落" +
            $"  ·  每帧 {_stepsPerFrame,5:F2} 步  {fixedReport.Pump}";

        // 固定泵手里"已经过去、却还没凑成一步"的时间。欠多少步是通道的私有状态,读不到 —— 所以每条泵在自己的
        // 钩子里量「总线位置 − 已交给球的时间」:这个差里除了余数+欠账,只剩下一个常数(采样器的锚点),而两条泵
        // 的采样器是在 Start 里被一起重新锚定的,常数相同,相减即消。绝对量因此不显示,只显示差。
        var unspent = fixedReport.UnspentMs - update.UnspentMs;
        var step = fixedReport.DtMilliseconds;
        var ratio = fixedReport.EffectiveDt > 0 ? update.EffectiveDt / fixedReport.EffectiveDt : 0;
        var measured = step > 0 ? update.DtMilliseconds / step : 0;
        UnspentLine.Text =
            $"未交付    固定泵没凑成步的时间 {unspent,8:F2} ms" +
            $"  ({(step > 0 ? unspent / step : 0),5:F2} 步 · 稳态应在 [0, h) 内)" +
            $"  ·  有效步长比 {ratio:F3}  (实测 D/h {measured:F3})";

        var bus = MonoBehaviourManager.Bus(DemoChannel.Name);
        ChannelLine.Text =
            $"通道      {DemoChannel.Name}" +
            $"  IsAdvancing {bus?.IsAdvancing}  IsPaused {bus?.IsPaused}  Epoch {bus?.Epoch}" +
            $"  Rate {bus?.Rate ?? 0:F2}×" +
            $"  TargetFPS {MonoBehaviourManager.TargetFPS(DemoChannel.Name)}" +
            $"  CurrentFPS {MonoBehaviourManager.CurrentFPS(DemoChannel.Name)}" +
            $"  SystemStatus {MonoBehaviourManager.SystemStatus(DemoChannel.Name)}" +
            $"  Threads {Alive(MonoBehaviourManager.IsUpdateThreadAlive(DemoChannel.Name))}" +
            $"/{Alive(MonoBehaviourManager.IsFixedUpdateThreadAlive(DemoChannel.Name))}";

        HandledLine.Text =
            $"Handled   复选框 {(ChkHandled.IsChecked == true ? "开" : "关")}" +
            $"  ·  引擎在钩子之前先看它,所以置上就等于本帧没有 LateUpdate —— 而 FixedUpdate 每步自建参数,不受影响";

        ChannelLine.ToolTip =
            "SystemStatus 在 rate 降到 0 时仍然会说 Running:冻结不是一个暂停,只有非零速率才会让时钟重新走。" +
            "IsAdvancing 才是消费者真正 park 在哪个信号上。";
    }

    private static string Alive(bool alive) => alive ? "alive" : "down";

    private void DrawLog()
    {
        var everyHook = ChkEveryHook.IsChecked == true;
        var omitted = Volatile.Read(ref _state.OmittedCount);

        LogLine.Text =
            $"钩子日志  记录每一帧 {(everyHook ? "开" : "关")}" +
            $"  ·  已记录 {_log.Count} 条  ·  已省略 {omitted} 条" +
            (everyHook
                ? "  ·  显示最后 " + Math.Min(_log.Count, LogTail) + " 条"
                : "  ·  只记 Awake/Start 与卡顿 —— 省略的不是没发生,是没记");

        var text = _log.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, _log.Skip(Math.Max(0, _log.Count - LogTail)).Select(Format));

        // 只有读者本来就在底部时才跟着滚,否则他们往回翻的动作会被每次轮询拽走。
        var follow = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 8;
        if (Log.Text != text)
        {
            Log.Text = text;
            if (follow) LogScroll.ScrollToEnd();
        }
    }

    private static string Format(HookEntry entry)
    {
        var head = entry.Kind switch
        {
            HookKind.Awake or HookKind.Start => $"————————  {entry.Kind,-11}",
            HookKind.Update or HookKind.LateUpdate => $"帧{entry.Index,6}  {entry.Kind,-11}",
            _ => $"步{entry.Index,6}  {entry.Kind,-11}",
        };

        var delta = entry.Kind switch
        {
            HookKind.Update => $"D {entry.DtMilliseconds,8:F1} ms",
            HookKind.FixedUpdate => $"h {entry.DtMilliseconds,8:F1} ms",
            _ => "               ",
        };

        var batch = entry.Kind == HookKind.FixedUpdate ? $"  本批 {entry.Batch,2} 步" : "            ";

        var note = entry.Note switch
        {
            HookNote.Slept when entry.Kind == HookKind.Update
                => "   ← 本帧睡了:欠一帧,下一帧的 D 会把整段还上",
            HookNote.Slept => "   ← 本帧睡了:欠的是步,下一次 Advance 推一批",
            HookNote.AfterSleep when entry.Kind == HookKind.Update
                => "   ← 上一帧睡过:整段停顿记在这一个 D 上",
            HookNote.AfterSleep => "   ← 上一帧睡过:这一批就是在还欠的步",
            _ => string.Empty,
        };

        return $"{head} {delta}{batch}{note}";
    }

    private void DrawPayload(BallReport update, BallReport fixedReport)
    {
        var bus = MonoBehaviourManager.Bus(DemoChannel.Name);
        var step = fixedReport.DtMilliseconds;

        Payload.Text = string.Join(';',
            "v=1",
            $"aw={Volatile.Read(ref _state.AwakeCount)}",
            $"st={Volatile.Read(ref _state.StartCount)}",
            $"u={Volatile.Read(ref _state.UpdateCount)}",
            $"l={Volatile.Read(ref _state.LateUpdateCount)}",
            $"f={Volatile.Read(ref _state.FixedUpdateCount)}",
            $"dt={update.DtMilliseconds:F2}",
            $"h={fixedReport.DtMilliseconds:F2}",
            $"dtA={update.EffectiveDt * 1000:F3}",
            $"dtB={fixedReport.EffectiveDt * 1000:F3}",
            $"ratio={(fixedReport.EffectiveDt > 0 ? update.EffectiveDt / fixedReport.EffectiveDt : 0):F3}",
            $"unspent={fixedReport.UnspentMs - update.UnspentMs:F2}",
            $"unspentSteps={(step > 0 ? (fixedReport.UnspentMs - update.UnspentMs) / step : 0):F2}",
            $"driveA={update.DriveTime:F3}",
            $"driveB={fixedReport.DriveTime:F3}",
            $"spf={_stepsPerFrame:F2}",
            $"fps={_framesPerSecond:F2}",
            $"driftA={update.Drift:F4}",
            $"driftB={fixedReport.Drift:F4}",
            $"fallsA={update.Falls}",
            $"fallsB={fixedReport.Falls}",
            $"batch={fixedReport.Batch}",
            $"handled={(ChkHandled.IsChecked == true ? 1 : 0)}",
            $"skipped={Volatile.Read(ref _state.SkippedLateUpdates)}",
            $"order={Volatile.Read(ref _state.OrderViolations)}",
            $"omitted={Volatile.Read(ref _state.OmittedCount)}",
            $"epoch={bus?.Epoch ?? -1}",
            $"adv={(bus?.IsAdvancing == true ? 1 : 0)}",
            $"paused={(bus?.IsPaused == true ? 1 : 0)}",
            $"rate={bus?.Rate ?? 0:F2}",
            $"restart={_restartGeneration}",
            $"polls={_polls}",
            string.Empty);
    }

    #endregion
}
