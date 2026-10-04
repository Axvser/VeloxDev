using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo;

// 与窗口相同的场景，无界面运行并输出成表格：注册元素数量增长时主题切换的表现。
// 用 dotnet run -- bench 启动。prep_ms 是切换首帧之前的同步工作，随元素数量增长；anim_ms 是其余部分，
// 不随数量增长——一次切换的所有元素锚在同一条时间轴上，一百个和一千个走到终点花的时间相同。
// attached 把库的开销与框架的分开：方块不在可视树里时不发生布局与渲染，两行之差就是宿主的渲染开销。
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

        // 上一批只被主题管理器弱引用；回收一次即可让活动集合恰好是本批。
        Collect();

        ThemeManager.SetCurrent<Dark>();
        await SettleAsync();

        var effect = new TransitionEffect { Duration = TimeSpan.FromMilliseconds(300), FPS = 60 };
        long frames = 0;
        EventHandler<TransitionEventArgs> handler = (_, _) => Interlocked.Increment(ref frames);
        effect.Update += handler;

        // 预热，免得首次运行的开销（JIT、运行时内部的惰性编译）算到某一行；主题路径每次切换为每个元素编译一次属性路径。
        ThemeManager.Transition<Light>(effect);
        await UntilAsync(() => ThemeManager.Current == typeof(Light));
        ThemeManager.Transition<Dark>(effect);
        await UntilAsync(() => ThemeManager.Current == typeof(Dark));
        await SettleAsync();

        for (var rep = 0; rep < repeats; rep++)
        {
            // 两种操作都必须真的切换：Jump/Transition 到当前主题会被各自顶部的守卫拒绝，那样就什么都没量到。
            Collect();
            var beforeJump = GC.GetTotalAllocatedBytes(true);
            var jumpWatch = Stopwatch.StartNew();
            if (ThemeManager.Current != typeof(Light)) ThemeManager.Jump<Light>();
            else ThemeManager.Jump<Dark>();
            jumpWatch.Stop();

            // 在这里采样，而不是写行时：Jump 是同步的，而这一行要等下面的动画切换也跑完才拼出来，
            // 若在写行时对 Jump 前的快照做差，会把整段动画切换算到 jump 头上。
            var jumpAllocated = GC.GetTotalAllocatedBytes(true) - beforeJump;

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
                (jumpAllocated / 1024.0).ToString("F0"),
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
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 60_000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(5);
    }
}
