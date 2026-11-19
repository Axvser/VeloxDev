using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>
    /// Leave the horizontal axis alone when setting a scroll position.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than taken from <c>ScrollPattern.NoScroll</c>: the pattern type is reachable through
    /// <c>PatternOrDefault</c> but is not part of the assembly's public surface, so naming it does not compile.
    /// The value is <c>-1</c>, meaning "this axis is not being set".
    /// </remarks>
    private const double NoScrollPercent = -1d;

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

    /// <summary>
    /// Whether the control lies inside the window — that is, whether a person could actually reach it.
    /// </summary>
    /// <remarks>
    /// This is not pedantry. UI Automation's Invoke pattern activates a control that has been laid out past the
    /// window's edge or scrolled out of sight exactly as happily as one on screen, so without this check the suite
    /// would keep passing over a surface nobody can use — which is precisely how a layout regression slips through.
    /// </remarks>
    internal bool IsInsideWindow(string automationId)
    {
        var element = FindNow(automationId);
        if (element is null) return false;

        return IsInsideWindow(
            element.Properties.BoundingRectangle.ValueOrDefault,
            _root.Properties.BoundingRectangle.ValueOrDefault);
    }

    /// <summary>
    /// Ask the surface to bring a control into view, by focusing it.
    /// </summary>
    /// <remarks>
    /// Focusing is the portable way to ask: WPF, Avalonia, WinUI and Jalium scroll a focused element into view
    /// through <c>BringIntoView</c>, and WinForms scrolls its <c>AutoScroll</c> container to the focused child.
    /// Deliberately not <c>ScrollItemPattern</c>: the rows are ordinary controls inside a scroller rather than items
    /// of a virtualising list, so the pattern is not offered.
    /// </remarks>
    internal void BringIntoView(string automationId) => FindNow(automationId)?.Focus();

    /// <summary>
    /// Bring the demo's own window to the front and give it the keyboard focus.
    /// </summary>
    /// <remarks>
    /// The other half of reaching a control, and the half that scrolling cannot do. A person reaching for a row scrolled
    /// out of sight clicks the window first, which is what a background window needs before it will accept a focus
    /// change at all — a window that is not foreground may simply refuse one, and then the focused element never
    /// scrolls into view no matter how often it is asked.
    /// </remarks>
    internal void Activate() => _root.Focus();

    /// <summary>Whether the control currently holds the keyboard focus.</summary>
    internal bool HasFocus(string automationId)
        => FindNow(automationId)?.Properties.HasKeyboardFocus.ValueOrDefault == true;

    /// <summary>
    /// Scroll the control's container until the control lies inside the window.
    /// </summary>
    /// <remarks>
    /// The path that does not go through focus, and the reason it has to exist: focusing is a <em>change</em> trigger
    /// rather than a command, so focusing an element that already holds focus does nothing at all. A row that was
    /// focused and then scrolled away is therefore unreachable through the focus path no matter how often the request
    /// is repeated — which is exactly what an intermittent WinUI failure turned out to be. This asks the container
    /// instead, which does what it is told whether or not anything is focused.
    /// </remarks>
    /// <returns><c>true</c> when a scrollable container was found; <c>false</c> when there is none to ask.</returns>
    internal bool ScrollContainerIntoView(string automationId)
    {
        var element = FindNow(automationId);
        if (element is null) return false;

        var window = _root.Properties.BoundingRectangle.ValueOrDefault;
        if (IsInsideWindow(element.Properties.BoundingRectangle.ValueOrDefault, window)) return true;

        var container = FindScrollableAncestor(element);
        var scroll = container?.Patterns.Scroll.PatternOrDefault;
        if (container is null || scroll is null) return false;

        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var control = element.Properties.BoundingRectangle.ValueOrDefault;
                var viewport = container.Properties.BoundingRectangle.ValueOrDefault;
                if (viewport.Height <= 0 || viewport.Width <= 0) return false;

                // 控制点比视口上沿低多少，内容就要往上走多少。
                var deltaPixels = control.Top - viewport.Top;

                // 滚动百分比是相对**内容**的，不是相对视口的：可见比例把两者换算起来。
                var visible = Math.Max(scroll.VerticalViewSize.ValueOrDefault / 100d, 0.01d);
                var contentHeight = viewport.Height / visible;
                var deltaPercent = deltaPixels / contentHeight * 100d;

                var current = scroll.VerticalScrollPercent.ValueOrDefault;
                var target = Math.Clamp(current + deltaPercent, 0d, 100d);
                if (Math.Abs(target - current) < 0.5d) return true; // 已经到头了，剩下的靠别的路

                scroll.SetScrollPercent(NoScrollPercent, target);
                Thread.Sleep(60);

                if (IsInsideWindow(element.Properties.BoundingRectangle.ValueOrDefault, window)) return true;
            }

            return true;
        }
        catch (Exception exception) when (exception is COMException or NotSupportedException or InvalidOperationException)
        {
            // 容器不支持程序化滚动，或者滚不了 —— 调用方还有聚焦那一条路。
            return false;
        }
    }

    private static AutomationElement? FindScrollableAncestor(AutomationElement element)
    {
        var ancestor = element.Parent;
        for (var depth = 0; ancestor is not null && depth < 8; depth++, ancestor = ancestor.Parent)
        {
            if (ancestor.Patterns.Scroll.PatternOrDefault is not null) return ancestor;
        }

        return null;
    }

    /// <summary>
    /// What UI Automation reports about a control's reachability, for a failure message.
    /// </summary>
    /// <remarks>
    /// <c>IsOffscreen</c> is included as evidence rather than as a verdict. Measured on WPF: a row scrolled below the
    /// fold reports <c>IsOffscreen=False</c> while its rectangle sits outside the window — so it cannot be the
    /// condition, and the rectangle check is what actually catches the case this guard exists for.
    /// </remarks>
    internal string DescribeReachability(string automationId, string? siblingPrefix = null)
    {
        var element = FindNow(automationId);
        if (element is null) return "the control is not in the automation tree at all";

        var control = element.Properties.BoundingRectangle.ValueOrDefault;
        var window = _root.Properties.BoundingRectangle.ValueOrDefault;

        var report = new StringBuilder();
        report.Append($"control {control.Left:F0},{control.Top:F0} {control.Width:F0}x{control.Height:F0}, ");
        report.Append($"window {window.Left:F0},{window.Top:F0} {window.Width:F0}x{window.Height:F0}, ");
        report.Append($"IsOffscreen={element.Properties.IsOffscreen.ValueOrDefault}");
        report.Append($", HasFocus={element.Properties.HasKeyboardFocus.ValueOrDefault}");

        // 键盘焦点在哪，是这一条诊断里最要紧的一项。BringIntoView 靠 Focus() 触发滚动，而在 UI Automation 里
        // 对一个**已经聚焦**的元素再 Focus 一次是空操作 —— 如果轮询期间焦点始终没离开过它，那么重试再多次也
        // 不会重新触发一次滚动，症状就正好是"元素在树里、矩形是空的、等多久都不好"。
        try
        {
            var focused = _automation.FocusedElement();
            report.Append($", focused='{focused?.Properties.AutomationId.ValueOrDefault}'");
        }
        catch (COMException)
        {
            report.Append(", focused=<unavailable>");
        }

        // 一个 0×0 的矩形有两种成因，而它们要分开处置：控件根本没被布局出来，或者它在一个滚动容器里被滚出了
        // 视野 —— 后者 UI Automation 可能报空矩形。祖先链连同滚动容器的状态把这两种分开；少了这一段，读消息
        // 的人只能靠猜，而"猜"和"查"在这里差着一整天。
        report.Append("; ancestors:");
        var ancestor = element.Parent;
        for (var depth = 0; ancestor is not null && depth < 6; depth++, ancestor = ancestor.Parent)
        {
            var rect = ancestor.Properties.BoundingRectangle.ValueOrDefault;
            report.Append($" [{ancestor.Properties.ControlType.ValueOrDefault} {rect.Left:F0},{rect.Top:F0} {rect.Width:F0}x{rect.Height:F0}");

            var scroll = ancestor.Patterns.Scroll.PatternOrDefault;
            if (scroll is not null)
            {
                report.Append($" scroll V={scroll.VerticallyScrollable}/{scroll.VerticalScrollPercent:F0}%"
                    + $" H={scroll.HorizontallyScrollable}/{scroll.HorizontalScrollPercent:F0}%");
            }

            report.Append(']');
        }

        // 同族把手坏了一个、还是整排都够不着，是两种完全不同的故障：前者是这一行的问题，后者是列表或窗口的问题。
        if (siblingPrefix is not null)
        {
            var handles = _root
                .FindAllDescendants()
                .Where(candidate => candidate.Properties.AutomationId.ValueOrDefault?.StartsWith(siblingPrefix, StringComparison.Ordinal) == true)
                .ToList();

            var reachable = handles.Count(candidate => IsInsideWindow(candidate.Properties.BoundingRectangle.ValueOrDefault, window));
            report.Append($"; {reachable} of {handles.Count} '{siblingPrefix}*' handles are inside the window");
        }

        return report.ToString();
    }

    private static bool IsInsideWindow(System.Drawing.Rectangle control, System.Drawing.Rectangle window)
        => control.Width > 0 && control.Height > 0
           && control.Right > window.Left && control.Left < window.Right
           && control.Bottom > window.Top && control.Top < window.Bottom;

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
