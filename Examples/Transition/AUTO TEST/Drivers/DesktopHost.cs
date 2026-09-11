using VeloxDev.AT.Engine;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// The desktop half of driving a demo: a process with a window, reached through UI Automation.
/// </summary>
/// <remarks>
/// The default host of <see cref="DemoDriverBase"/>, so a desktop driver says nothing about how it is reached. It
/// keeps <see cref="DesktopProcessHost"/> for the process lifetime and <see cref="AutomationLocator"/> for the
/// surface, which is what keeps every FlaUI type inside the locator's file.
/// </remarks>
internal sealed class DesktopHost : IDemoHost
{
    private readonly DesktopProcessHost _process = new();
    private AutomationLocator? _locator;

    public bool IsInKillOnCloseJob => _process.IsInKillOnCloseJob;

    public void Start(string executablePath, string readyToken, TimeSpan timeout)
    {
        _process.Start(executablePath);
        _locator = AutomationLocator.Attach(_process.ProcessId, timeout);

        // 就绪握手：读数控件出现之前不算起来了，套件测量的是它，不是窗口本身。
        _locator.Find(readyToken, timeout);
    }

    public void Click(string token, TimeSpan timeout)
        => Locator.Find(token, timeout).Click();

    public bool Exists(string token) => Locator.Exists(token);

    public bool IsControlInsideView(string token) => Locator.IsInsideWindow(token);

    /// <summary>A desktop control has no computed style — that question belongs to the browser host.</summary>
    public string? ComputedStyle(string token, string property) => null;

    public string Text(string token, TimeSpan timeout) => Locator.Find(token, timeout).Text;

    public string? CaptureScreenshot(string path) => _process.CaptureScreenshot(path);

    private AutomationLocator Locator => _locator
        ?? throw new InvalidOperationException("The demo has not been started yet; call Start first.");

    public void Dispose()
    {
        _locator?.Dispose();
        _locator = null;
        _process.Dispose();
        GC.SuppressFinalize(this);
    }
}
