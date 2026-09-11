using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace VeloxDev.AT.Drivers;

/// <summary>
/// Finds controls in a running application through UI Automation, and is the only file in this project that knows
/// FlaUI exists.
/// </summary>
/// <remarks>
/// The indirection is the point. FlaUI has been unmaintained since 2022, and the library that would replace it — the
/// framework's own <c>System.Windows.Automation</c> — has to be a change to this file and to <see cref="AutomationHandle"/>
/// and to nothing else. Callers only ever see a handle, which offers the four things a suite actually needs: an id, a
/// name, its text, and the ability to click it.
/// </remarks>
internal sealed class AutomationLocator : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly UIA3Automation _automation;
    private readonly AutomationElement _root;

    private AutomationLocator(UIA3Automation automation, AutomationElement root)
    {
        _automation = automation;
        _root = root;
    }

    /// <summary>The window's title, for failure messages.</summary>
    internal string WindowTitle => _root.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>
    /// Attach to a process, waiting for its main window. Everything the locator searches is searched from that window,
    /// so a lookup can never wander into another application's tree.
    /// </summary>
    /// <exception cref="TimeoutException">The process never opened a window.</exception>
    internal static AutomationLocator Attach(int processId, TimeSpan timeout)
    {
        var automation = new UIA3Automation();
        try
        {
            var window = WaitFor(
                () =>
                {
                    using var process = Process.GetProcessById(processId);
                    var handle = process.MainWindowHandle;
                    return handle == IntPtr.Zero ? null : automation.FromHandle(handle);
                },
                timeout,
                $"process {processId} to open its main window");

            return new AutomationLocator(automation, window);
        }
        catch
        {
            automation.Dispose();
            throw;
        }
    }

    /// <summary>Wait for a control and return its handle.</summary>
    /// <exception cref="TimeoutException">No control with that automation id appeared.</exception>
    internal AutomationHandle Find(string automationId, TimeSpan timeout)
    {
        var element = WaitFor(() => FindNow(automationId), timeout, $"a control with AutomationId '{automationId}'");
        return new AutomationHandle(element);
    }

    /// <summary>Whether a control is present right now — no waiting, for probes that are themselves the assertion.</summary>
    internal bool Exists(string automationId) => FindNow(automationId) is not null;

    /// <summary>
    /// A verbatim description of the window's automation subtree, for a probe that has to report what it actually saw
    /// when it finds nothing.
    /// </summary>
    /// <remarks>
    /// Diagnostic, and deliberately the one lookup here that is not by id. Whether a platform answers UI Automation at
    /// all is the fact that decides how a driver for it has to be written, and "the window was found but the tree is
    /// empty" is a different finding from "the window was never found" — this is what tells them apart.
    /// </remarks>
    internal static string DescribeTree(int processId, TimeSpan timeout)
    {
        using var locator = Attach(processId, timeout);
        var title = locator.WindowTitle;

        AutomationElement[] descendants;
        try
        {
            descendants = locator._root.FindAllDescendants();
        }
        catch (COMException exception)
        {
            return $"window '{title}' was found, but its automation tree could not be walked: {exception.Message}";
        }

        var ids = descendants
            .Select(element => element.Properties.AutomationId.ValueOrDefault)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return ids.Count == 0
            ? $"window '{title}' was found, with {descendants.Length} descendants but no automation ids among them."
            : $"window '{title}' was found, with {descendants.Length} descendants and {ids.Count} distinct automation ids: "
              + string.Join(", ", ids);
    }

    private AutomationElement? FindNow(string automationId)
    {
        try
        {
            return _root.FindFirstDescendant(factory => factory.ByAutomationId(automationId));
        }
        catch (COMException)
        {
            // 窗口刚出现时自动化树会短暂抛错，交给重试。
            return null;
        }
    }

    /// <summary>Poll a probe until it yields something, or give up. The tree is allowed to be incomplete while loading.</summary>
    private static AutomationElement WaitFor(Func<AutomationElement?> probe, TimeSpan timeout, string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            AutomationElement? found;
            try
            {
                found = probe();
            }
            catch (COMException)
            {
                found = null;
            }
            catch (InvalidOperationException)
            {
                found = null;
            }
            catch (ArgumentException)
            {
                found = null;
            }

            if (found is not null) return found;

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Waited {timeout.TotalSeconds:F1}s for {description}.");

            Thread.Sleep(PollInterval);
        }
    }

    public void Dispose() => _automation.Dispose();
}

/// <summary>
/// One control found through UI Automation, wrapped so that no FlaUI type escapes
/// <see cref="AutomationLocator"/>'s file.
/// </summary>
internal sealed class AutomationHandle
{
    private readonly AutomationElement _element;

    internal AutomationHandle(AutomationElement element) => _element = element;

    /// <summary>The demo's own stable token for the control.</summary>
    internal string AutomationId => _element.Properties.AutomationId.ValueOrDefault ?? string.Empty;

    /// <summary>The control's accessible name. For WPF's text elements this is the text itself.</summary>
    internal string Name => _element.Properties.Name.ValueOrDefault ?? string.Empty;

    /// <summary>Whether the control is currently enabled.</summary>
    internal bool IsEnabled => _element.Properties.IsEnabled.ValueOrDefault;

    /// <summary>
    /// The control's text: its value pattern where it has one, its accessible name otherwise. A WPF <c>TextBlock</c>
    /// has no value pattern, which is exactly the case the observation surface is in.
    /// </summary>
    internal string Text
    {
        get
        {
            var value = _element.Patterns.Value.PatternOrDefault?.Value;
            return string.IsNullOrEmpty(value) ? Name : value;
        }
    }

    /// <summary>
    /// Click by invoking the control's pattern rather than by moving the mouse. The window is not necessarily in the
    /// foreground — another demo may be, since the suites run one after another — and a synthetic click at a screen
    /// coordinate would land on whatever is on top of it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The control cannot be activated; a finding, not a flake.</exception>
    internal void Click()
    {
        var invoke = _element.Patterns.Invoke.PatternOrDefault;
        if (invoke is not null)
        {
            invoke.Invoke();
            return;
        }

        throw new InvalidOperationException(
            $"The control '{AutomationId}' exposes no Invoke pattern, so it cannot be clicked without synthesising a mouse click.");
    }
}
