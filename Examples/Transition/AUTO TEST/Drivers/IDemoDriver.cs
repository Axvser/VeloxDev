using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// The three things a suite does to a demo: start it, click it, read what it reports.
/// </summary>
/// <remarks>
/// Kept deliberately small. Everything else a suite needs — the readiness handshake, the poll for a live readout — is
/// shared and lives in <see cref="DemoDriverBase"/>, so a new platform costs a launch path, a locator and the tokens its
/// surface names its controls by, and nothing else.
/// </remarks>
internal interface IDemoDriver : IDisposable
{
    /// <summary>The platform this driver drives, spelled the way suites and <c>VELOXDEV_AT_PLATFORMS</c> spell it.</summary>
    string Platform { get; }

    /// <summary>
    /// The payload format version this driver understands. Reported by the payload itself, so a demo that grew a field
    /// a driver does not know about is a failure here rather than a silently ignored value later.
    /// </summary>
    int PayloadVersion { get; }

    /// <summary>
    /// Whether the demo is inside the kill-on-close job object — the only teardown that still happens when the test
    /// host itself is killed without running any cleanup.
    /// </summary>
    bool IsInKillOnCloseJob { get; }

    /// <summary>Start the demo and wait until its observation surface is both present and ticking.</summary>
    void Launch();

    /// <summary>
    /// Click the control with this automation id, then linger for <c>VELOXDEV_AT_PACE</c> so the click can be watched.
    /// </summary>
    /// <remarks>
    /// Every click a suite makes goes through here, deliberately: the pause is what makes a run observable, and
    /// putting it in one place means a new case cannot leave it out. It is not a timing source — nothing is asserted
    /// against it, and the payloads are still waited for by their own sequence numbers. It brings the control into
    /// view first, so a case that scrolls out of a list is still clicked where a person could see it.
    /// </remarks>
    void Click(string automationId);

    /// <summary>
    /// Ask the surface to bring a control into view, the way focusing it would for a person.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Click"/> because a suite sometimes has to look before it leaps: the reachability
    /// guard runs between the scroll and the click, so a scroll that silently did nothing is a failure rather than a
    /// licence to drive something nobody can see.
    /// </remarks>
    void BringIntoView(string automationId);

    /// <summary>Whether a control with this automation id is present right now.</summary>
    bool HasControl(string automationId);

    /// <summary>
    /// Whether that control is somewhere a person could reach it. UI Automation's Invoke pattern activates a control
    /// that was never laid out inside the window just as happily as one on screen, so a suite that clicks without
    /// asking would keep passing over a row nobody can see.
    /// </summary>
    bool IsControlInView(string automationId);

    /// <summary>
    /// The computed value of a CSS property on that control, where the platform has such a thing; <c>null</c>
    /// otherwise. Only a browser surface can be read this way.
    /// </summary>
    string? ComputedStyle(string automationId, string property);

    /// <summary>Read the demo's observation payload as it stands.</summary>
    StatePayload Read();

    /// <summary>
    /// Read the sampler-conformance payload: what the last activated sampler wrote, at each of the eased times it was
    /// driven at.
    /// </summary>
    ConformancePayload ReadConformance();

    /// <summary>
    /// Click the handle for one sampler and return what it published, waiting until the payload's own sequence number
    /// moves on. Driving it through the handle is what makes this a UI test rather than a startup report.
    /// </summary>
    /// <exception cref="TimeoutException">The click did not reach the demo's payload.</exception>
    ConformancePayload ActivateSampler(string sampler);

    /// <summary>Read the live payload as it stands: what the control held during the last run a click started.</summary>
    LivePayload ReadLive();

    /// <summary>
    /// Wait for the run that started after <paramref name="after"/> to report that it has finished, and return what
    /// the control then held. <c>null</c> when nothing finished within the timeout, which is itself an anomaly.
    /// </summary>
    LivePayload? WaitForLive(long after);

    /// <summary>Read the bulk payload as it stands: every case row's frames and live digest from one run.</summary>
    BatchPayload ReadBatch();

    /// <summary>
    /// Wait for the bulk run that started after <paramref name="after"/> to finish. <c>null</c> when nothing finished
    /// within the timeout, which is itself an anomaly.
    /// </summary>
    BatchPayload? WaitForBatch(long after);

    /// <summary>
    /// Read until the readout's sequence number moves past <paramref name="after"/>, which is the only sound proof
    /// that the demo is live and that a click was not dropped.
    /// </summary>
    StatePayload WaitForTick(long after = -1);

    /// <summary>
    /// Poll until the payload satisfies <paramref name="predicate"/>, or fail the wait with
    /// <paramref name="description"/> in the message.
    /// </summary>
    StatePayload WaitFor(Func<StatePayload, bool> predicate, TimeSpan timeout, string description);
}
