using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.Playwright;

namespace VeloxDev.AT.Engine;

/// <summary>
/// Owns the Blazor demo: the Kestrel process that serves it and the browser page it is observed through.
/// </summary>
/// <remarks>
/// Blazor is the one demo here that needs no desktop, which is what makes the acceptance run possible on an agent. The
/// page is driven through Playwright over the installed Edge (<c>Channel = "msedge"</c>), so nothing has to be
/// downloaded to run it locally; a CI image without Edge runs <c>playwright.ps1 install chromium</c> once and drops
/// the channel. Playwright types appear in this file and nowhere else, the same way FlaUI types appear only in
/// <c>AutomationLocator</c>.
/// <para>
/// There is no job object, and deliberately so: the browser is not a lone window on a desktop that a killed test host
/// could orphan — attaching it to a kill-on-close job would also take the profile the driver process shares with the
/// user's own browsing. Teardown is therefore explicit instead of inherited: the browser context is closed, then the
/// Kestrel process tree is killed, and an exit hook covers the run that ends without either.
/// </para>
/// </remarks>
internal sealed class BlazorHost : IDemoHost
{
    /// <summary>
    /// The URL the host serves the demo on and navigates to. Supplied through <c>ASPNETCORE_URLS</c> rather than left
    /// to <c>launchSettings.json</c>, which only applies to <c>dotnet run</c>: the suite has to know where to look.
    /// </summary>
    internal const string BaseUrl = "http://127.0.0.1:5120/";

    /// <summary>How long one control lookup or click may take before Playwright gives up on it.</summary>
    private const float ActionTimeoutMs = 5_000f;

    /// <summary>How much of the server's own log to keep for a failure message, in characters.</summary>
    private const int ServerLogLimit = 16_384;

    // 进程表是作业对象之外的第二道防线，浏览器这边没有作业对象，这张表就是唯一一道。
    private static readonly ConcurrentDictionary<int, string> Launched = new();
    private static int _exitHookRegistered;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly StringBuilder _serverLog = new();

    private Process? _server;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    /// <summary>
    /// Always false: this host's demo is a browser and a Kestrel process, not a window inside a kill-on-close job. The
    /// suites that assert the job object are the desktop ones; see the class remarks for why the browser is not put in
    /// one.
    /// </summary>
    public bool IsInKillOnCloseJob => false;

    public void Start(string executablePath, string readyToken, TimeSpan timeout)
    {
        RequireNothingListening();
        StartServer(executablePath);
        WaitForHttp(timeout);
        LaunchBrowser(timeout);
        Navigate(timeout);
        WaitForControl(readyToken, timeout);

        // 控件查找、点击、取文本的默认预算与桌面端一致；导航和启动各有自己的超时。
        Page.SetDefaultTimeout(ActionTimeoutMs);
    }

    public void Click(string token, TimeSpan timeout)
        => Page.Locator(Selector(token)).ClickAsync(new LocatorClickOptions
        {
            Timeout = (float)timeout.TotalMilliseconds,
        }).GetAwaiter().GetResult();

    public bool Exists(string token) => Page.Locator(Selector(token)).CountAsync().GetAwaiter().GetResult() > 0;

    public string Text(string token, TimeSpan timeout)
    {
        var locator = Page.Locator(Selector(token));
        locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = (float)timeout.TotalMilliseconds,
        }).GetAwaiter().GetResult();

        return locator.TextContentAsync().GetAwaiter().GetResult() ?? string.Empty;
    }

    public string? CaptureScreenshot(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true }).GetAwaiter().GetResult();
            return path;
        }
        catch (PlaywrightException)
        {
            // 诊断失败不该升级成测试失败。
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Close the page's browser and kill the server. The order matters: the browser must go first, or the demo it
    /// leaves running would keep pushing renders at a page that no longer exists.
    /// </summary>
    public void Dispose()
    {
        CloseQuietly(_context?.CloseAsync());
        CloseQuietly(_browser?.CloseAsync());

        _context = null;
        _browser = null;
        _page = null;
        _playwright?.Dispose();
        _playwright = null;
        _http.Dispose();

        KillServer();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Kill any Kestrel this assembly started that is somehow still alive — the counterpart of the desktop host's
    /// pid list, for the host being torn down without running any cleanup.
    /// </summary>
    /// <remarks>
    /// Reached through the exit hook below rather than an <c>[AssemblyCleanup]</c>: MSTest allows one of those per
    /// assembly and <see cref="DesktopProcessHost"/>'s janitor already holds it, and a second one makes the whole
    /// assembly undiscoverable (UTA014) rather than merely ignored.
    /// </remarks>
    internal static void KillEverythingWeLaunched()
    {
        foreach (var (processId, executable) in Launched)
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                // 只有名字还对得上才动手：pid 可能早已被系统回收给了别的进程。
                if (!string.Equals(process.ProcessName + ".exe", executable, StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }

        Launched.Clear();
    }

    private IPage Page => _page
        ?? throw new InvalidOperationException("The Blazor demo has not been started yet; call Start first.");

    /// <summary>The page's name for a control: a <c>data-at</c> attribute. The demo has no ids, and none are to be added.</summary>
    private static string Selector(string token) => $"[data-at=\"{token}\"]";

    /// <summary>
    /// Fail early and legibly when something else is already answering. A server from an aborted run would serve the
    /// page this suite then measures, so the stale instance has to be named rather than silently driven.
    /// </summary>
    private void RequireNothingListening()
    {
        try
        {
            using var response = _http.GetAsync(BaseUrl).GetAwaiter().GetResult();
            throw new InvalidOperationException(
                $"{BaseUrl} is already answering (HTTP {(int)response.StatusCode}), so a demo from an earlier run is still up. "
                + "Kill it and rerun; otherwise the observations would be read off that instance rather than a fresh one.");
        }
        catch (HttpRequestException)
        {
            // 没人应答才是正常的。
        }
        catch (TaskCanceledException)
        {
            // 连上了但没在超时内回应，等于端口被别的东西占着，同样不该继续。
            throw new InvalidOperationException($"{BaseUrl} accepted a connection but never answered within the timeout; something else is on port 5120.");
        }
    }

    private void StartServer(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"No demo at '{executablePath}'. Build the Blazor demo first, or set VELOXDEV_AT_DEMO_ROOT to the directory its output lives under.",
                executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // launchSettings 只在 dotnet run 下生效，这里跑的是构建输出，端口只能由套件自己给。
        // 只给 http 地址，UseHttpsRedirection 就没有 https 端口可转 —— 这正是"最终 URL 必须还是 http"的前提。
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl.TrimEnd('/');

        // 必须是 Development：套件跑的是构建输出而不是发布输出，而静态 Web 资产（含框架那份 blazor.web.js）只有在
        // Development 下才会从构建清单里被装载。以 Production 起一个 bin 输出，blazor.web.js 会以 200 空体返回，
        // 电路根本连不上，读数定时器也就永远不会走 —— 页面上是预渲染的 v=1;seq=0，看上去像"动画没跑"。
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        _server = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{executablePath}'.");

        Launched[_server.Id] = Path.GetFileName(executablePath);
        RegisterExitHook();

        // 服务端日志必须一直读走：管道写满会让 Kestrel 卡在日志上，而失败时这份日志就是唯一的现场。
        _server.OutputDataReceived += (_, args) => Append(args.Data);
        _server.ErrorDataReceived += (_, args) => Append(args.Data);
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
    }

    /// <summary>Poll until Kestrel answers. Any answer counts, including an error page: listening is what is being waited for.</summary>
    private void WaitForHttp(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (_server!.HasExited)
            {
                throw new InvalidOperationException(
                    $"The Blazor demo exited with code {_server.ExitCode} before answering on {BaseUrl}. Its log:{Environment.NewLine}{ServerLog}");
            }

            try
            {
                using var response = _http.GetAsync(BaseUrl).GetAwaiter().GetResult();
                return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Waited {timeout.TotalSeconds:F1}s for {BaseUrl} to answer. The server's log:{Environment.NewLine}{ServerLog}");
            }

            Thread.Sleep(150);
        }
    }

    private void LaunchBrowser(TimeSpan timeout)
    {
        _playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
        _browser = _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // 用机器上装好的 Edge，本地就不必先下载浏览器；CI 上没有 Edge，装一次 chromium 把 Channel 去掉即可。
            Channel = "msedge",
            Headless = true,
            Timeout = (float)timeout.TotalMilliseconds,
        }).GetAwaiter().GetResult();

        _context = _browser.NewContextAsync().GetAwaiter().GetResult();
        _page = _context.NewPageAsync().GetAwaiter().GetResult();
    }

    private void Navigate(TimeSpan timeout)
    {
        Page.GotoAsync(BaseUrl, new PageGotoOptions { Timeout = (float)timeout.TotalMilliseconds }).GetAwaiter().GetResult();

        // 套件只给了 http 地址，重定向没有 https 端口可去；最终还在 http:// 上，这个前提才成立。
        if (!Page.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The demo ended up at '{Page.Url}' instead of a plain-http {BaseUrl}. The suite sets "
                + $"ASPNETCORE_URLS={BaseUrl.TrimEnd('/')} so that UseHttpsRedirection has no https port to redirect to; "
                + "a redirect here means that changed, and the run would be measuring a document this suite was not written for.");
        }
    }

    private void WaitForControl(string token, TimeSpan timeout)
        => Page.Locator(Selector(token)).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = (float)timeout.TotalMilliseconds,
        }).GetAwaiter().GetResult();

    private void KillServer()
    {
        var server = _server;
        if (server is null) return;

        try
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (Win32Exception) { }

        Launched.TryRemove(server.Id, out _);
        _server = null;
    }

    private void Append(string? line)
    {
        if (line is null) return;

        lock (_serverLog)
        {
            if (_serverLog.Length >= ServerLogLimit) return;
            _serverLog.AppendLine(line);
        }
    }

    private string ServerLog
    {
        get
        {
            lock (_serverLog) return _serverLog.Length == 0 ? "(nothing)" : _serverLog.ToString();
        }
    }

    /// <summary>Close a browser handle, treating a failure to close as a diagnostic rather than a test failure.</summary>
    private static void CloseQuietly(Task? closing)
    {
        try
        {
            closing?.GetAwaiter().GetResult();
        }
        catch (PlaywrightException) { }
        catch (TimeoutException) { }
    }

    /// <summary>
    /// Hook the pid list onto the way out, once per process. <see cref="Dispose"/> is the teardown the suites call; this
    /// covers the run that ends without it, which is what the desktop host gets from its assembly cleanup.
    /// </summary>
    private static void RegisterExitHook()
    {
        if (Interlocked.Exchange(ref _exitHookRegistered, 1) != 0) return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillEverythingWeLaunched();
    }
}
