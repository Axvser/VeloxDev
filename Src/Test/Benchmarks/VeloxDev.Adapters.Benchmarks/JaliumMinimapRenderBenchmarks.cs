extern alias jal;

using System.Reflection;
using BenchmarkDotNet.Attributes;
using Jalium.UI.Media;
using VeloxDev.Adapters.Benchmarks.Headed;
using JalMinimap = jal::VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowMinimapOverlay;

namespace VeloxDev.Adapters.Benchmarks;

// Jalium 小地图的**生产录制路径**：MediaRenderCacheHost.CreateRecorder → OnRender(recorder) →
// FinishRecord。旧实现每帧 new DrawingGroup().Open() 只是新建一个空录制目标，没有捕获绘制命令，
// 那些数字已作废，不能与任何结果比较。
// 回放不在此基准里：Jalium 外部只有 DrawingGroup 这类离屏目标，其回放支持与生产的
// RenderTargetDrawingContext 不同；这里只量应用代码在脏帧上付出的录制成本。
// 宿主类型是 Jalium 内部的：用反射取 host 与方法，并用 RecordedDrawing.CommandCount 做自检。
[MemoryDiagnoser]
public class JaliumMinimapRenderBenchmarks
{
    private const int Frames = 60;

    private static readonly MethodInfo OnRender =
        typeof(JalMinimap).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private JalMinimap _minimap = null!;
    private int _probeCommandCount;
    private DrawingGroup _liveGroup = null!;
    private DrawingContext _liveContext = null!;
    private Func<DrawingContext, DrawingContext> _createRecorder = null!;
    private Func<DrawingContext, object> _finishRecord = null!;
    private readonly object[] _args = new object[1];

    [GlobalSetup]
    public void Setup()
    {
        _minimap = new JalMinimap
        {
            Width = 200d,
            Height = 140d,
            WorkflowTree = SyntheticWorkflow.Create(new HeadedOptions()).Tree,
            ViewportWidth = 800d,
            ViewportHeight = 600d,
        };
        _minimap.Measure(new Jalium.UI.Size(200d, 140d));
        _minimap.Arrange(new Jalium.UI.Rect(0d, 0d, 200d, 140d));

        _liveGroup = new DrawingGroup();
        _liveContext = _liveGroup.Open();

        BindRenderCacheHost();

        // 自检：录制 → 回放到群组，必须真的落下命令；否则这个基准量的不是生产路径。
        var drawing = Record(_liveContext);
        var count = (int)drawing.GetType()
            .GetProperty("CommandCount", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawing)!;
        if (count == 0)
        {
            throw new InvalidOperationException(
                "Jalium production recorder produced no draw commands for the minimap; the benchmark would be meaningless.");
        }

        _probeCommandCount = count;
    }

    // 生产录制半程（脏帧里应用代码的成本）：CreateRecorder → OnRender → FinishRecord。
    [Benchmark(OperationsPerInvoke = Frames)]
    public void Record60()
    {
        for (var i = 0; i < Frames; i++)
        {
            Record(_liveContext);
        }
    }

    private object Record(DrawingContext live)
    {
        var recorder = _createRecorder(live);
        _args[0] = recorder;
        OnRender.Invoke(_minimap, _args);
        return _finishRecord(recorder);
    }

    private void BindRenderCacheHost()
    {
        var hostType = typeof(DrawingGroup).Assembly.GetType(
            "Jalium.UI.Media.Rendering.MediaRenderCacheHost", throwOnError: true)!;

        hostType.GetMethod("Bootstrap", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);

        var host = typeof(Visual)
            .GetProperty("RenderCacheHost", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)
            ?? throw new InvalidOperationException(
                "Jalium's render cache host is not installed (JALIUM_DISABLE_RENDER_CACHE=1?).");

        var create = hostType.GetMethod("CreateRecorder")!;
        var finish = hostType.GetMethod("FinishRecord")!;

        // 内部类型上的公开方法在跨程序集时可能不允许 CreateDelegate；退化到 Invoke，代价写进报告。
        _createRecorder = Bind<Func<DrawingContext, DrawingContext>>(create, host)
            ?? (live => (DrawingContext)create.Invoke(host, [live])!);
        _finishRecord = Bind<Func<DrawingContext, object>>(finish, host)
            ?? (recorder => finish.Invoke(host, [recorder])!);
    }

    private static T? Bind<T>(MethodInfo method, object host) where T : Delegate
    {
        try
        {
            return (T)method.CreateDelegate(typeof(T), host);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
