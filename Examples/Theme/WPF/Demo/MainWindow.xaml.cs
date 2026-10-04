using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;
using VeloxDev.TimeLine;

namespace Demo;

/// <summary>
/// The same theme system the minimal demo shows, at a scale where the interesting properties become visible:
/// every element of one switch moves on <b>one</b> shared timeline, so the existing timeline control reaches a
/// theme switch unchanged, and one <see cref="Transition.Pause"/> call on any single element freezes all of them.
/// </summary>
public partial class MainWindow : Window
{
    private readonly List<ThemeTile> _tiles = [];
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Process _self = Process.GetCurrentProcess();

    // 长到让暂停与跳转有东西可打断。
    private readonly TransitionEffect _effect = new() { Duration = TimeSpan.FromSeconds(3), FPS = 60 };

    private readonly EventHandler<TransitionEventArgs> _frameCounter;
    private long _frames;
    private int _size = 1000;
    private bool _autoLoop;

    public MainWindow()
    {
        // 每个目标每帧记一次，高目标数下的掉帧就是这样显出来的。
        _frameCounter = (_, _) => Interlocked.Increment(ref _frames);
        _effect.Update += _frameCounter;

        InitializeComponent();

        // 窗口自己也是一个主题元素，与上千个方块一起被同一次切换覆盖；和其他元素一样，这句必须在 InitializeComponent 之后。
        InitializeTheme();

        _ticker.Tick += (_, _) => UpdateLive();
        _ticker.Start();

        Loaded += (_, _) =>
        {
            Build(_size);
            Status.Text = "就绪。点「动画切换」看所有元素一起渐变，动画途中试「暂停」「拖到 50%」。";
        };
    }

    private void Build(int count)
    {
        Host.Children.Clear();
        _tiles.Clear();

        for (var i = 0; i < count; i++) _tiles.Add(new ThemeTile());
        foreach (var tile in _tiles) Host.Children.Add(tile);

        _size = count;

        // 上一批只被主题管理器弱引用，下次切换时清除，所以回收一次就让活动集合恰好是本批。
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // 把一次控制调用交给这次切换：寻址任意一个目标就够，它们共享一条时间轴，暂停/跳转/变速只有一个载体。
    private void Act(Action<ThemeTile> operation)
    {
        if (_tiles.Count == 0) return;

        operation(_tiles[0]);
        UpdateLive();
    }

    private async Task SwitchAsync(bool animate)
    {
        var toLight = ThemeManager.Current != typeof(Light);
        var target = toLight ? typeof(Light) : typeof(Dark);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Interlocked.Exchange(ref _frames, 0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var cpuBefore = _self.TotalProcessorTime;
        var watch = Stopwatch.StartNew();

        // Transition 在每个目标都准备、写入、排定之后才返回（首个 await 是 Task.WhenAll），
        // 所以这段耗时就是准备开销，与动画分开计。
        if (animate)
        {
            if (toLight) ThemeManager.Transition<Light>(_effect);
            else ThemeManager.Transition<Dark>(_effect);
        }
        else
        {
            if (toLight) ThemeManager.Jump<Light>();
            else ThemeManager.Jump<Dark>();
        }

        var preparation = watch.Elapsed.TotalMilliseconds;
        var deadline = Environment.TickCount64 + 30_000;
        while (ThemeManager.Current != target && Environment.TickCount64 < deadline) await Task.Delay(5);
        watch.Stop();

        var landed = ThemeManager.Current == target;
        var frames = Interlocked.Read(ref _frames);

        Status.Text = string.Format(
            "{0} 个元素 · {1} · 准备 {2:F1} ms · 总计 {3:F1} ms · 帧 {4}（每目标 {5}）· 分配 {6:F1} MB · CPU {7:F0} ms",
            _size,
            landed ? $"→ {target.Name} 完成" : "→ 被中断",
            preparation,
            watch.Elapsed.TotalMilliseconds,
            frames,
            frames / Math.Max(1, _size),
            (GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 1048576.0,
            (_self.TotalProcessorTime - cpuBefore).TotalMilliseconds);
    }

    private void UpdateLive()
    {
        if (_tiles.Count == 0)
        {
            Live.Text = string.Empty;
            return;
        }

        var tile = _tiles[0];
        Live.Text = string.Format(
            "pos {0:F0} ms · cycle {1} · paused {2} · rate {3:F2} · Current {4}",
            Transition.Position(tile).TotalMilliseconds,
            Transition.Cycle(tile),
            Transition.IsPaused(tile),
            Transition.Rate(tile),
            ThemeManager.Current.Name);
    }

    private void OnAnimate(object sender, RoutedEventArgs e) => _ = SwitchAsync(animate: true);

    private void OnJump(object sender, RoutedEventArgs e) => _ = SwitchAsync(animate: false);

    private void OnTogglePause(object sender, RoutedEventArgs e) => Act(tile =>
    {
        if (Transition.IsPaused(tile)) Transition.Resume(tile);
        else Transition.Pause(tile);
    });

    private void OnSlow(object sender, RoutedEventArgs e) => Act(tile => Transition.SetRate(tile, 0.25));

    private void OnNormalRate(object sender, RoutedEventArgs e) => Act(tile => Transition.SetRate(tile, 1.0));

    private void OnSeekHalf(object sender, RoutedEventArgs e)
        => Act(tile => Transition.Seek(tile, TimeSpan.FromMilliseconds(_effect.Duration.TotalMilliseconds / 2)));

    private void OnStop(object sender, RoutedEventArgs e)
        => Act(tile => Transition.Exit(tile, IncludeMutual: true, IncludeNoMutual: true));

    private void OnSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var count)) Build(count);
    }

    private void OnAutoLoopChanged(object sender, RoutedEventArgs e)
    {
        _autoLoop = AutoLoop.IsChecked == true;
        if (_autoLoop) _ = AutoLoopAsync();
    }

    private async Task AutoLoopAsync()
    {
        while (_autoLoop)
        {
            await SwitchAsync(animate: true);
            await Task.Delay(500);
        }
    }
}

// 主题部分 ↓
// 主题声明放在单独的 partial 块里：它们是配置，不是交互逻辑。
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow
{
    // 生成的钩子。实现它们就是全部订阅机制 —— 没有事件可挂处理器，被后一次切换取代的切换两个钩子都不调。
    partial void OnThemeChanging(Type? oldValue, Type? newValue)
        => Hooks.Text = $"OnThemeChanging: {oldValue?.Name} -> {newValue?.Name}";

    partial void OnThemeChanged(Type? oldValue, Type? newValue)
        => Hooks.Text = $"OnThemeChanged: {oldValue?.Name} -> {newValue?.Name}";

    // 覆盖本实例某个主题下的一个值，并可还原：覆盖胜过声明值，只有真正改动的属性会进活动缓存。
    private void OnEditThemeValue(object sender, RoutedEventArgs e)
        => SetThemeValue<Light>(nameof(Background), new object?[] { "#fff4d6" });

    private void OnRestoreThemeValue(object sender, RoutedEventArgs e)
        => RestoreThemeValue<Light>(nameof(Background));
}
