using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// The half of driving a demo that does not depend on the platform: the readiness handshake, the click handshake, the
/// poll to the end of a run, and the collected result.
/// </summary>
/// <remarks>
/// The ordering here is the whole value of the class, and each step exists because of a way a UI test usually goes
/// wrong. A click is only sent after the readout has been seen to advance, so a dropped click cannot be mistaken for a
/// slow start. A click is only accepted once the payload names the scenario, so a stale payload cannot be read as a
/// result. And a run is only over when the demo says so, because both curves cross their target again on the way back
/// and "the value reached the target" would therefore pass while the animation is still flying.
/// Everything platform-specific is behind <see cref="IDemoHost"/>: a driver supplies an executable, the tokens its
/// surface names controls by, and a scenario table, and inherits the ordering unchanged.
/// </remarks>
internal abstract class DemoDriverBase : IDemoDriver
{
    /// <summary>How long to wait for the application to come up. A cold start under a loaded agent is not instant.</summary>
    protected static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for a click to be reflected in the payload.</summary>
    protected static readonly TimeSpan ClickTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for a readout tick, which is what proves the demo's timer is running.</summary>
    protected static readonly TimeSpan TickTimeout = TimeSpan.FromSeconds(5);

    private IDemoHost? _host;
    private PollRecorder? _recorder;

    public abstract string Platform { get; }

    public abstract IReadOnlyList<ScenarioSpec> Scenarios { get; }

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

    /// <summary>How long past its duration a run may take before the demo's own <c>done</c> is overdue.</summary>
    protected virtual TimeSpan SettleSlack => TimeSpan.FromSeconds(3);

    public void Launch()
    {
        _host = CreateHost();
        _host.Start(ExecutablePath, ReadyAutomationId, LaunchTimeout);
        _recorder = new PollRecorder(Read);

        // 启动时就完成一次就绪握手：套件开始测量前，读数定时器必须已经在跑。
        WaitForTick();
    }

    public void Click(string automationId) => Host.Click(automationId, ClickTimeout);

    public bool HasControl(string automationId) => Host.Exists(automationId);

    public StatePayload Read() => StatePayload.Parse(Host.Text(PayloadAutomationId, ClickTimeout));

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

    /// <summary>
    /// Run a scenario end to end: click it, wait for the click to land, then poll until the demo reports it is done.
    /// </summary>
    /// <returns>Every payload observed during the flight, and the one that reported the end.</returns>
    public ScenarioRun Run(ScenarioSpec spec)
    {
        // 就绪握手先做：点击必须在读数定时器已经在跑之后才发出，否则点击可能落在启动过程里被静默丢掉。
        WaitForTick();

        var before = Recorder.Read();
        Click(spec.ButtonAutomationId);

        // 点击生效的判据是场景令牌变了、且 seq 越过了点击前那一次，这样一次被丢弃的点击不会被当成已开始。
        Recorder.Until(
            payload => payload.Scenario == spec.PayloadToken && payload.Sequence > before.Sequence,
            ClickTimeout,
            $"the click on '{spec.ButtonAutomationId}' to start '{spec.PayloadToken}'");

        // 从点击落定那一刻重新采样：更早的样本可能带着上一个场景遗留的峰值。
        Recorder.Reset();

        var settled = Recorder.Until(
            payload => payload.Done,
            spec.Duration + SettleSlack,
            $"'{spec.PayloadToken}' to report done=1 after {spec.Duration.TotalMilliseconds:F0}ms");

        return new ScenarioRun(spec, [.. Recorder.Samples], settled);
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
