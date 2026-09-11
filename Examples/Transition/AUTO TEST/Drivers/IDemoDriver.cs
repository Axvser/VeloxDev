using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// The three things a suite does to a demo: start it, click it, read what it reports.
/// </summary>
/// <remarks>
/// Kept deliberately small. Everything else a suite needs — the readiness handshake, the poll to the end of a run,
/// collecting a peak — is shared and lives in <see cref="DemoDriverBase"/>, so a new platform costs a launch path, a
/// locator and a scenario table, and nothing else.
/// </remarks>
internal interface IDemoDriver : IDisposable
{
    /// <summary>The platform this driver drives, spelled the way suites and <c>VELOXDEV_AT_PLATFORMS</c> spell it.</summary>
    string Platform { get; }

    /// <summary>Every scenario the demo exposes, in the order the suites should run them.</summary>
    IReadOnlyList<ScenarioSpec> Scenarios { get; }

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

    /// <summary>Click the control with this automation id.</summary>
    void Click(string automationId);

    /// <summary>Whether a control with this automation id is present right now.</summary>
    bool HasControl(string automationId);

    /// <summary>Read the demo's observation payload as it stands.</summary>
    StatePayload Read();

    /// <summary>
    /// Read until the readout's sequence number moves past <paramref name="after"/>, which is the only sound proof
    /// that the demo is live and that a click was not dropped.
    /// </summary>
    StatePayload WaitForTick(long after = -1);
}
