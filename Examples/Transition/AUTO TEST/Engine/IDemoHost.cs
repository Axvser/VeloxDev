namespace VeloxDev.AT.Engine;

/// <summary>
/// Owns one demo — the process it runs in and the surface it is observed through — and is the only part of driving a
/// demo that differs between a desktop window and a browser page.
/// </summary>
/// <remarks>
/// <see cref="DemoDriverBase"/> owns the ordering that makes a UI run sound: the readiness handshake before a click,
/// the click handshake before a measurement, and the poll to the demo's own <c>done</c>. A platform supplies a host
/// and a scenario table and inherits all of it, rather than keeping a second copy of a race it would have to get right
/// twice. A <em>token</em> is whatever a platform's surface names a control by: an automation id in a desktop window,
/// a <c>data-at</c> attribute in a page.
/// </remarks>
internal interface IDemoHost : IDisposable
{
    /// <summary>
    /// Start the demo, bring its surface up, and wait until the token naming its ready control is there.
    /// </summary>
    /// <exception cref="TimeoutException">The surface never came up.</exception>
    void Start(string executablePath, string readyToken, TimeSpan timeout);

    /// <summary>
    /// Whether the demo is inside the kill-on-close job object — the only teardown that still happens when the test
    /// host itself is killed without running any cleanup.
    /// </summary>
    bool IsInKillOnCloseJob { get; }

    /// <summary>
    /// Click the control with this token. Each host clicks the way a person would for that surface, and waits for the
    /// control to be actionable first: a click delivered to a control that is not there yet is not a click.
    /// </summary>
    void Click(string token, TimeSpan timeout);

    /// <summary>Whether a control with this token is present right now — no waiting, for probes that are themselves the assertion.</summary>
    bool Exists(string token);

    /// <summary>The text of the control with this token, waiting for it to appear.</summary>
    /// <exception cref="TimeoutException">No control with that token appeared.</exception>
    string Text(string token, TimeSpan timeout);

    /// <summary>Write a screenshot of the demo's surface; returns the path, or <c>null</c> when it could not be taken.</summary>
    string? CaptureScreenshot(string path);
}
