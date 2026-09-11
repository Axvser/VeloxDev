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
    /// The token every demo publishes its sampler-conformance payload under. It is the one handle this contract fixes
    /// itself rather than letting a driver choose, because the payload is the same shape on every platform.
    /// </summary>
    private const string ConformanceAutomationId = "over.conf";

    /// <summary>
    /// What every demo names a sampler's handle by, with the sampler's type name appended. The token scheme is fixed
    /// here rather than left to each driver, because it is the one thing a suite has to agree on across all platforms.
    /// </summary>
    private const string SamplerHandlePrefix = "over.sampler.";

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

    public void Click(string automationId) => Host.Click(automationId, ClickTimeout);

    public bool HasControl(string automationId) => Host.Exists(automationId);

    public bool IsControlInView(string automationId) => Host.IsControlInsideView(automationId);

    public string? ComputedStyle(string automationId, string property) => Host.ComputedStyle(automationId, property);

    public StatePayload Read() => StatePayload.Parse(Host.Text(PayloadAutomationId, ClickTimeout));

    public ConformancePayload ReadConformance()
        => ConformancePayload.Parse(Host.Text(ConformanceAutomationId, ClickTimeout));

    public ConformancePayload ActivateSampler(string sampler)
    {
        var token = $"{SamplerHandlePrefix}{sampler}";

        // 先确认这个把手在人够得着的地方。UIA 的 Invoke 模式对排在窗口外、或滚出视野的控件照样生效，
        // 少了这一道，一套没人能用的界面会让套件一路绿着过去 —— 布局回归正是这样溜掉的。
        if (!Host.IsControlInsideView(token))
        {
            throw new InvalidOperationException(
                $"{Platform}: the handle '{token}' is outside the surface, so a person could not reach it.");
        }

        var before = ReadConformance();
        Click(token);

        // 点击生效的判据是载荷自己的 seq 越过了点击前那一次 —— 与读数同一套握手，固定 sleep 替代不了它。
        var deadline = DateTime.UtcNow + ClickTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var latest = ReadConformance();
            if (latest.Sequence > before.Sequence)
            {
                // 只为让人看得见：默认是零，开跑就飞快。
                if (AtConfig.Pace > TimeSpan.Zero) Thread.Sleep(AtConfig.Pace);
                return latest;
            }

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
