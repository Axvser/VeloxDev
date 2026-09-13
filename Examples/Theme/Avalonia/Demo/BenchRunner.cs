using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;
using VeloxDev.TimeLine;

namespace Demo;

/// <summary>
/// The same scenario as the window, run headlessly and written out as a table: how a theme switch behaves as the
/// number of registered elements grows.
/// </summary>
/// <remarks>
/// Run with <c>dotnet run -- bench</c>. Two columns carry the story. <c>prep_ms</c> is the synchronous part of the
/// <see cref="ThemeManager.Transition{T}"/> call — everything the switch does before its first frame — and it is the
/// part that scales with the element count. <c>anim_ms</c> is the rest, and it does not: every element of a switch
/// is anchored to one shared timeline, so a hundred targets and a thousand take the same wall time to reach the end.
/// <para>
/// <c>attached</c> separates the library's cost from the framework's. With the tiles outside the visual tree, no
/// layout or render work happens, so a gap between the two rows is the host's rendering, not the theme system's.
/// </para>
/// </remarks>
internal static class BenchRunner
{
    private static readonly int[] Sizes = [1, 50, 200, 1000];
    private static readonly bool[] Attached = [true, false];

    internal static async Task RunAsync(Action onFinished)
    {
        var host = new WrapPanel();
        var window = new Window { Width = 1400, Height = 900, Content = new ScrollViewer { Content = host } };
        window.Show();

        var log = new StringBuilder();
        log.AppendLine("size\tattached\trep\tswitch\tjump_ms\tjump_alloc_kb\tprep_ms\ttotal_ms\tanim_ms\tframes\tframes_per_target\talloc_kb\tcpu_ms\tgen0\tgen1\tgen2");

        try
        {
            foreach (var attached in Attached)
            {
                foreach (var size in Sizes)
                {
                    try
                    {
                        await MeasureAsync(host, size, attached, repeats: 3, log);
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"{size}\t{attached}\t\tFAILED\t{ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            var path = Path.Combine(Path.GetTempPath(), "veloxdev-theme-scale.tsv");
            await File.WriteAllTextAsync(path, log.ToString());
            window.Close();
            Console.WriteLine(path);
            Debug.WriteLine(path);
            onFinished();
        }
    }

    private static async Task MeasureAsync(WrapPanel host, int size, bool attached, int repeats, StringBuilder log)
    {
        var process = Process.GetCurrentProcess();

        host.Children.Clear();
        var tiles = new List<ThemeTile>(size);
        for (var i = 0; i < size; i++) tiles.Add(new ThemeTile());
        if (attached)
        {
            foreach (var tile in tiles) host.Children.Add(tile);
        }

        // The previous batch is only weakly referenced by the theme manager; one collection makes the active set
        // exactly this batch.
        Collect();

        ThemeManager.SetCurrent<Dark>();
        await SettleAsync();

        var effect = new TransitionEffect { Duration = TimeSpan.FromMilliseconds(300), FPS = 60 };
        long frames = 0;
        EventHandler<TransitionEventArgs> handler = (_, _) => Interlocked.Increment(ref frames);
        effect.Update += handler;

        // Warm-up, so first-run costs (JIT, lazy compilation inside the runtime) are not attributed to a row.
        // The theme path also compiles a property path per element per switch; see TransitionProperty.FromProperty.
        ThemeManager.Transition<Light>(effect);
        await UntilAsync(() => ThemeManager.Current == typeof(Light));
        ThemeManager.Transition<Dark>(effect);
        await UntilAsync(() => ThemeManager.Current == typeof(Dark));
        await SettleAsync();

        for (var rep = 0; rep < repeats; rep++)
        {
            // Both operations have to actually switch: a Jump or Transition to the theme already current is
            // rejected by the guard at the top of each, which would measure nothing.
            Collect();
            var jumpAllocated = GC.GetTotalAllocatedBytes(true);
            var jumpWatch = Stopwatch.StartNew();
            if (ThemeManager.Current != typeof(Light)) ThemeManager.Jump<Light>();
            else ThemeManager.Jump<Dark>();
            jumpWatch.Stop();

            var toLight = ThemeManager.Current != typeof(Light);

            Collect();
            Interlocked.Exchange(ref frames, 0);
            var allocated = GC.GetTotalAllocatedBytes(true);
            var cpu = process.TotalProcessorTime;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            var watch = Stopwatch.StartNew();
            if (toLight) ThemeManager.Transition<Light>(effect);
            else ThemeManager.Transition<Dark>(effect);
            var preparation = watch.Elapsed.TotalMilliseconds;

            await UntilAsync(() => ThemeManager.Current == (toLight ? typeof(Light) : typeof(Dark)));
            watch.Stop();

            var total = watch.Elapsed.TotalMilliseconds;
            var counted = Interlocked.Read(ref frames);

            log.AppendLine(string.Join('\t',
                size,
                attached,
                rep,
                toLight ? "->Light" : "->Dark",
                jumpWatch.Elapsed.TotalMilliseconds.ToString("F1"),
                ((GC.GetTotalAllocatedBytes(true) - jumpAllocated) / 1024.0).ToString("F0"),
                preparation.ToString("F1"),
                total.ToString("F1"),
                (total - preparation).ToString("F1"),
                counted,
                (counted / (double)size).ToString("F0"),
                ((GC.GetTotalAllocatedBytes(true) - allocated) / 1024.0).ToString("F0"),
                (process.TotalProcessorTime - cpu).TotalMilliseconds.ToString("F0"),
                GC.CollectionCount(0) - gen0,
                GC.CollectionCount(1) - gen1,
                GC.CollectionCount(2) - gen2));
        }

        effect.Update -= handler;
        await SettleAsync();
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task SettleAsync()
    {
        await Task.Delay(150);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 60_000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(5);
    }
}
