using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// The half of driving a demo that does not depend on the platform: the readiness handshake, reading the payload, and
/// the poll that proves a readout is live.
/// </summary>
/// <remarks>
/// The readiness handshake is the whole value of the class. A readout is only trusted once its sequence number has been
/// seen to advance, which is the one proof that the demo's timer is running — and it is not replaceable by a sleep,
/// because it is also what closes the race a browser demo's circuit-startup introduces.
/// Everything platform-specific is behind <see cref="IDemoHost"/>: a driver supplies an executable and the tokens its
/// surface names controls by, and inherits the ordering unchanged.
/// </remarks>
internal abstract class DemoDriverBase : IDemoDriver
{
    /// <summary>How long to wait for the application to come up. A cold start under a loaded agent is not instant.</summary>
    protected static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a click to be reflected in the payload.</summary>
    protected static readonly TimeSpan ClickTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a readout tick, which is what proves the demo's timer is running.</summary>
    protected static readonly TimeSpan TickTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a live run may take before the suite gives up on it and reports that it never finished.
    /// </summary>
    /// <remarks>
    /// Generous on purpose: the run is a real animation, and <c>VELOXDEV_BENCH_MS</c> can stretch it. Giving up here is
    /// an anomaly to report rather than an exception to throw — a demo that never says <c>done</c> is one of the things
    /// this suite exists to notice.
    /// </remarks>
    internal static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a bulk run may take before the suite gives up on it.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="LiveTimeout"/> on purpose: a bulk run is every row at once, and it is only finished when
    /// the last of them has settled — so it is bounded by the slowest row, not by one row, and the rows contend for
    /// the same UI thread while they run.
    /// </remarks>
    internal static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(45);

    /// <summary>How often to re-read the live payload while waiting for a run to finish.</summary>
    private static readonly TimeSpan LivePollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How long to give a surface to scroll a control into view asynchronously.
    /// </summary>
    /// <remarks>
    /// Generous next to an instantaneous scroll (WPF, Avalonia, Jalium) because it is only ever waited out when the
    /// scroll is genuinely slow or never happens — and the latter has to end in a failure, not in a hang.
    /// </remarks>
    private static readonly TimeSpan BringIntoViewTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The token every demo publishes its sampler-conformance payload under. It is the one handle this contract fixes
    /// itself rather than letting a driver choose, because the payload is the same shape on every platform.
    /// </summary>
    private const string ConformanceAutomationId = "over.conf";

    /// <summary>
    /// What every demo names a sampler's handle by, with the sampler's type name appended. The token scheme is fixed
    /// here rather than left to each driver, because it is the one thing a suite has to agree on across all platforms.
    /// </summary>
    private const string SamplerHandlePrefix = "over.sampler.";

    /// <summary>
    /// The token every demo publishes what a real transition left on the control under. Fixed here for the same reason
    /// the payload above is: the shape is the same on every platform, so no driver gets to choose it.
    /// </summary>
    private const string LiveAutomationId = "over.live";

    /// <summary>
    /// The token the bulk payload is published under — one run of every case row at once.
    /// </summary>
    /// <remarks>
    /// The per-row payloads above are shaped around "click one row, read what it wrote", so driving every row that
    /// way costs one click and one whole animation apiece. This is the channel that does all of them in one go.
    /// </remarks>
    private const string BatchAutomationId = "over.batch";

    /// <summary>
    /// The toolbar handles that put the demo back to its rest state. Named here, like the payload tokens above,
    /// because every suite needs them and no driver gets to spell them differently.
    /// </summary>
    private const string StopAllAutomationId = "over.btn.stop.all";

    private const string ResetAllAutomationId = "over.btn.reset.all";

    /// <summary>
    /// How many 20 ms polls of <see cref="BringIntoView"/> go by before the scroll request is issued again.
    /// </summary>
    /// <remarks>
    /// Long enough not to fight a scroll animation that is already running, short enough that a request which simply
    /// did nothing is retried well inside <see cref="BringIntoViewTimeout"/>.
    /// </remarks>
    private const int BringIntoViewRepollEvery = 6;

    private IDemoHost? _host;
    private PollRecorder? _recorder;

    public abstract string Platform { get; }

    public abstract int PayloadVersion { get; }

    public bool IsInKillOnCloseJob => Host.IsInKillOnCloseJob;

    /// <summary>The demo's executable, as an absolute path.</summary>
    protected abstract string ExecutablePath { get; }

    /// <summary>The token naming the control whose presence proves the observation surface is up.</summary>
    protected abstract string ReadyAutomationId { get; }

    /// <summary>The token naming the control the machine-readable payload is read from.</summary>
    protected abstract string PayloadAutomationId { get; }

    /// <summary>
    /// The host that owns the demo's process and surface. Defaults to a desktop window reached through UI Automation,
    /// which is what every driver but the browser one wants.
    /// </summary>
    protected virtual IDemoHost CreateHost() => new DesktopHost();

    /// <summary>The host, available once the demo has been launched.</summary>
    protected IDemoHost Host => _host
        ?? throw new InvalidOperationException($"{Platform} has not been launched; call {nameof(Launch)} first.");

    /// <summary>The payload poller, available once the demo has been launched.</summary>
    protected PollRecorder Recorder => _recorder
        ?? throw new InvalidOperationException($"{Platform} has not been launched; call {nameof(Launch)} first.");

    public void Launch()
    {
        _host = CreateHost();
        _host.Start(ExecutablePath, ReadyAutomationId, LaunchTimeout);
        _recorder = new PollRecorder(Read);

        // 启动时就完成一次就绪握手：套件开始测量前，读数定时器必须已经在跑。
        WaitForTick();
    }

    /// <summary>
    /// Name the case about to run, then hold the surface still for <see cref="AtConfig.Observe"/>.
    /// </summary>
    /// <remarks>
    /// A run is watched by people as well as read by machines, and a case that begins the instant the previous one
    /// ends is a blur with a verdict attached. Naming the case first, and letting the surface sit for a moment, is
    /// what makes the middle of a run followable — the same argument the pace makes for clicks, one level up.
    /// </remarks>
    public void Show(string what)
    {
        Console.WriteLine($"[AT] {Platform} · {what}");

        if (AtConfig.Observe > TimeSpan.Zero) Thread.Sleep(AtConfig.Observe);
    }

    /// <summary>
    /// Bring the demo back to its rest state: stop everything running, then reset the targets.
    /// </summary>
    /// <remarks>
    /// A suite no longer gets a process of its own — one demo per platform is started and shared for the whole run —
    /// so this is what replaces a fresh process as the known starting state. Stopping before resetting keeps the
    /// reset from being overwritten by an animation that was still running.
    /// </remarks>
    public void Settle()
    {
        Click(StopAllAutomationId);
        Click(ResetAllAutomationId);
    }

    /// <summary>
    /// Click one control, then linger for <see cref="AtConfig.Pace"/> so a person can see what that click did.
    /// </summary>
    /// <remarks>
    /// The pause lives here rather than in each suite, and that placement is the point: every click a suite makes goes
    /// through this method, so no case can be added that quietly forgets to slow down. It previously sat inside
    /// <see cref="ActivateSampler"/> alone, which is exactly why the load-mode suite — a dozen clicks a platform, each
    /// one starting real animations — used to flash past too fast for anyone to follow.
    /// </remarks>
    public void Click(string automationId)
    {
        // 先把控件弄到看得见的地方再点。工具栏那排本来就在视野里，这一步对它们是空操作；但它让"点击一个
        // 滚出视野的行"不至于变成"点了但没人看得见"。走驱动自己那个"滚完等它到位"的版本。
        BringIntoView(automationId);
        Host.Click(automationId, ClickTimeout);

        if (AtConfig.Pace > TimeSpan.Zero) Thread.Sleep(AtConfig.Pace);
    }

    /// <summary>
    /// Ask the surface to bring a control into view, then give it a moment to actually get there.
    /// </summary>
    /// <remarks>
    /// Several platforms scroll **asynchronously** — MAUI's <c>ScrollToAsync</c>, WinUI's
    /// <c>StartBringIntoView</c> — so "the request was made" is not "it is in view". Asserting right after the
    /// request judges one that has not been serviced yet, which is the same mistake as reading "how many rows have
    /// left their start" on a single frame: a race dressed up as a check. Waiting costs a few milliseconds when the
    /// scroll is immediate (WPF, Avalonia and Jalium all are) and nothing when it is not.
    /// <para>
    /// This deliberately does not report failure: it returns either way, and the caller's own assertion is what
    /// decides. A row that never arrives still fails — it just fails for the right reason.
    /// </para>
    /// </remarks>
    public void BringIntoView(string automationId)
    {
        var deadline = DateTime.UtcNow + BringIntoViewTimeout;
        var attempt = 0;

        while (true)
        {
            // 每过一会儿就**重新请求**一次，而不是请求一次之后干等：轮询一个没人会改变的状态，等不出结果来。
            // 这曾经在 WinUI 上间歇失败 —— 滚出视野的行会被剔除，剔除状态下报出的矩形是 0x0，而落空的那一次
            // Focus() 之后，再怎么等都不会自己好。轮询本身保持 20ms 不变，所以成功时该多快还是多快。
            if (attempt++ % BringIntoViewRepollEvery == 0) Host.BringIntoView(automationId);

            // 控件压根不存在时由调用方去报，不在这里空等。
            if (!Host.Exists(automationId)) return;
            if (Host.IsControlInsideView(automationId)) return;
            if (DateTime.UtcNow >= deadline) return;

            Thread.Sleep(20);
        }
    }

    public bool HasControl(string automationId) => Host.Exists(automationId);

    public bool IsControlInView(string automationId) => Host.IsControlInsideView(automationId);

    public string? ComputedStyle(string automationId, string property) => Host.ComputedStyle(automationId, property);

    public StatePayload Read() => StatePayload.Parse(Host.Text(PayloadAutomationId, ClickTimeout));

    public ConformancePayload ReadConformance()
        => ConformancePayload.Parse(Host.Text(ConformanceAutomationId, ClickTimeout));

    public LivePayload ReadLive() => LivePayload.Parse(Host.Text(LiveAutomationId, ClickTimeout));

    /// <summary>
    /// Read until the live payload reports a run that both started after <paramref name="after"/> and has finished.
    /// </summary>
    /// <remarks>
    /// Both halves of that are needed. The sequence alone cannot tell "not finished yet" from "the previous run's
    /// payload, still sitting there"; the done flag alone cannot tell a finished run from one that finished before the
    /// click. Together they are the same handshake the conformance payload uses, extended over the run's duration.
    /// </remarks>
    /// <returns>The finished payload, or <c>null</c> when nothing finished in time.</returns>
    public LivePayload? WaitForLive(long after)
    {
        var deadline = DateTime.UtcNow + LiveTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var latest = ReadLive();
            if (latest.Sequence > after && latest.Done) return latest;

            Thread.Sleep(LivePollInterval);
        }

        return null;
    }

    public BatchPayload ReadBatch() => BatchPayload.Parse(Host.Text(BatchAutomationId, ClickTimeout));

    public BatchPayload? WaitForBatch(long after)
    {
        var deadline = DateTime.UtcNow + BatchTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var latest = ReadBatch();
            if (latest.Sequence > after && latest.Done) return latest;

            Thread.Sleep(LivePollInterval);
        }

        return null;
    }

    public ConformancePayload ActivateSampler(string sampler)
    {
        var token = $"{SamplerHandlePrefix}{sampler}";

        // 案例列在界面上是一张可滚动的列表，所以先把这一行弄到人看得见的地方 —— 然后**仍然**断言它真的在窗口里。
        // UIA 的 Invoke 模式对排在窗口外、或滚出视野的控件照样生效，少了这一道，一套没人能用的界面会让套件
        // 一路绿着过去 —— 布局回归正是这样溜掉的。滚动之后才断言是关键：滚不动会当场红，而不是被当成"够得着"。
        Host.BringIntoView(token);

        if (!Host.Exists(token))
        {
            throw new InvalidOperationException(
                $"{Platform}: no control with AutomationId '{token}' exists, so the demo does not offer this case at all.");
        }

        if (!Host.IsControlInsideView(token))
        {
            throw new InvalidOperationException(
                $"{Platform}: the handle '{token}' is still outside the surface after being brought into view "
                + $"({Host.DescribeReachability(token)}), so a person could not reach it.");
        }

        var before = ReadConformance();

        // 点击本身已经带上"让人看得见"的那一口气（见 Click），这里只管等它生效。
        Click(token);

        // 点击生效的判据是载荷自己的 seq 越过了点击前那一次 —— 与读数同一套握手，固定 sleep 替代不了它。
        var deadline = DateTime.UtcNow + ClickTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var latest = ReadConformance();
            if (latest.Sequence > before.Sequence) return latest;

            Thread.Sleep(20);
        }

        throw new TimeoutException(
            $"{Platform}: clicking '{SamplerHandlePrefix}{sampler}' did not move the conformance sequence past {before.Sequence}.");
    }

    /// <summary>
    /// Read until the readout's sequence number moves past <paramref name="after"/>. Two reads that differ are the only
    /// sound proof that the demo is live and that a click was not dropped — the race a fixed sleep cannot close.
    /// </summary>
    public StatePayload WaitForTick(long after = -1)
    {
        var baseline = after < 0 ? Recorder.Read().Sequence : after;
        return Recorder.Until(
            payload => payload.Sequence > baseline,
            TickTimeout,
            $"the readout sequence to move past {baseline}");
    }

    public StatePayload WaitFor(Func<StatePayload, bool> predicate, TimeSpan timeout, string description)
    {
        Recorder.Reset();
        return Recorder.Until(predicate, timeout, description);
    }

    /// <summary>Write a screenshot of the demo's surface; returns the path, or <c>null</c> when it could not be taken.</summary>
    public string? CaptureScreenshot(string path) => Host.CaptureScreenshot(path);

    public virtual void Dispose()
    {
        _host?.Dispose();
        _host = null;
        GC.SuppressFinalize(this);
    }
}
