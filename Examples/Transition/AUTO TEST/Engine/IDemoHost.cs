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

    /// <summary>
    /// Ask the surface to bring a control into view — what focusing it does for a person.
    /// </summary>
    /// <remarks>
    /// The demos present their cases as a scrollable list, so a handle can be present, clickable through UI
    /// Automation, and still below the fold. This is the step that puts it in front of a person; whether it worked is
    /// not taken on trust — <see cref="IsControlInsideView"/> is checked straight afterwards, so a scroll that
    /// silently does nothing is a failure rather than a licence to click something nobody can see.
    /// </remarks>
    void BringIntoView(string token);

    /// <summary>
    /// What the surface says about where a control is, for a failure message: its rectangle, and whether it is
    /// flagged as scrolled out of sight.
    /// </summary>
    string DescribeReachability(string token);

    /// <summary>
    /// Whether the control with this token is somewhere a person could reach it — inside the window for a desktop
    /// surface. A suite that clicks through the Invoke pattern would otherwise keep passing over a control laid out
    /// past the window's edge, verifying a UI nobody can use.
    /// </summary>
    bool IsControlInsideView(string token);

    /// <summary>
    /// The **computed** value of a CSS property on the control with this token, or <c>null</c> where that question has
    /// no meaning. A browser is the one surface whose real appearance is fully described by its computed style, so
    /// this is how a browser-side suite reads what is actually on screen rather than what the app says it produced.
    /// </summary>
    string? ComputedStyle(string token, string property);

    /// <summary>The text of the control with this token, waiting for it to appear.</summary>
    /// <exception cref="TimeoutException">No control with that token appeared.</exception>
    string Text(string token, TimeSpan timeout);

    /// <summary>Write a screenshot of the demo's surface; returns the path, or <c>null</c> when it could not be taken.</summary>
    string? CaptureScreenshot(string path);
}
