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
/// <remarks>
/// The UI is built in code because it is generated — a thousand identical tiles plus a toolbar — and because the
/// thing worth reading here is the measurement, not the markup. <c>dotnet run -- bench</c> runs the same scenario
/// headlessly and writes a table.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly List<ThemeTile> _tiles = [];
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Process _self = Process.GetCurrentProcess();

    // Long enough that pause and seek have something to interrupt.
    private readonly TransitionEffect _effect = new() { Duration = TimeSpan.FromSeconds(3), FPS = 60 };

    private readonly EventHandler<TransitionEventArgs> _frameCounter;
    private long _frames;
    private int _size = 1000;
    private bool _autoLoop;

    public MainWindow()
    {
        // Counts one call per target per frame, which is how the frame deficit at high target counts shows up.
        _frameCounter = (_, _) => Interlocked.Increment(ref _frames);
        _effect.Update += _frameCounter;

        InitializeComponent();

        // The window is a themed element too, on top of the thousands of tiles, so one switch covers both. Like
        // every other one, this call must follow InitializeComponent.
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

        // The previous batch is only weakly referenced by the theme manager and is pruned on the next switch, so
        // one collection makes the active set exactly this batch.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Hands one control call to the switch. Addressing <b>any single</b> target is enough: they all share one
    /// timeline, so there is only one transport for a pause, a seek or a rate change to move.
    /// </summary>
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

        // Transition returns only after every target has been prepared, written and scheduled — its first await is
        // the Task.WhenAll — so this call's own duration is the preparation cost, measured apart from the animation.
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

//------------------------------------------------------------------------------------------------------------------
// Theme Part ↓

/* The theme declarations live in their own partial block: they are configuration, not interaction logic. */
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Foreground), ["#1e1e1e"], ["#ffffff"])]
public partial class MainWindow
{
    /// <summary>
    /// The generated hooks. Implementing them is the whole subscription mechanism — there is no event to add a
    /// handler to, and a switch that is superseded before it lands calls neither.
    /// </summary>
    partial void OnThemeChanging(Type? oldValue, Type? newValue)
        => Hooks.Text = $"OnThemeChanging: {oldValue?.Name} -> {newValue?.Name}";

    partial void OnThemeChanged(Type? oldValue, Type? newValue)
        => Hooks.Text = $"OnThemeChanged: {oldValue?.Name} -> {newValue?.Name}";

    /// <summary>
    /// Overrides one value for one theme on this instance, and puts it back. An override beats the declared value
    /// for that theme, and only the properties actually changed appear in the active cache.
    /// </summary>
    private void OnEditThemeValue(object sender, RoutedEventArgs e)
        => SetThemeValue<Light>(nameof(Background), new object?[] { "#fff4d6" });

    private void OnRestoreThemeValue(object sender, RoutedEventArgs e)
        => RestoreThemeValue<Light>(nameof(Background));
}
