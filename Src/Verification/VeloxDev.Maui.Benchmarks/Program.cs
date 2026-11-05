using System.Reflection;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Maui.Graphics;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Maui.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        // leak 模式：离树后连线层是否还被树 VM 拓住，退出码 0 = 已回收，1 = 泄漏。
        if (args is ["members"])
        {
            foreach (var m in typeof(Microsoft.Maui.Controls.VisualElement).GetMembers(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (m.Name.Contains("oad")) Console.WriteLine(m.MemberType + " " + m);
            return;
        }

        if (args is ["leak"])
        {
            Environment.ExitCode = LeakProbe.Run() ? 0 : 1;
            Console.WriteLine(Environment.ExitCode == 0 ? "LEAK-PROBE: collected" : "LEAK-PROBE: LEAKED");
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

// MAUI 连线层真实 Draw：直接驱动生产代码的 IDrawable，ICanvas 用不分配的空画布，只量适配器自身的分配。
[MemoryDiagnoser]
public class MauiLinkOverlayDrawBenchmarks
{
    private const int Frames = 60;

    private WorkflowLinkOverlay _overlay = null!;
    private IDrawable _drawable = null!;
    private readonly NullCanvas _canvas = new();
    private readonly RectF _dirty = new(0, 0, 800, 600);

    [GlobalSetup]
    public void Setup()
    {
        _overlay = new WorkflowLinkOverlay();
        // 纯 net10.0 没有 MainThread 实现：绕过属性回调里的 ScheduleInvalidate，直接注入树，Draw 只读这个字段。
        var tree = TestGraph.Create().Tree;
        // 连线只有进了可见集才画；没有表面就自己给一个视口，不然 A/B 两边都只量到空循环。
        tree.GetHelper().Virtualize(new VeloxDev.WorkflowSystem.Viewport(0d, 0d, 800d, 600d));
        typeof(WorkflowLinkOverlay).GetField("_tree", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_overlay, tree);
        _drawable = _overlay.Drawable;
        _drawable.Draw(_canvas, _dirty);
    }

    [Benchmark(OperationsPerInvoke = Frames)]
    public void Draw60()
    {
        for (var i = 0; i < Frames; i++) _drawable.Draw(_canvas, _dirty);
    }
}

internal static class LeakProbe
{
    private static readonly FieldInfo UnloadedField = typeof(Microsoft.Maui.Controls.VisualElement)
        .GetField("_unloaded", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static bool Run()
    {
        var graph = TestGraph.Create();
        var weak = CreateAndUnload(graph);
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        GC.KeepAlive(graph);
        return !weak.IsAlive;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndUnload(TestGraph graph)
    {
        var overlay = new WorkflowLinkOverlay();
        // 纯 net10.0 无 MainThread：直接走订阅路径 AttachTree，绕开属性回调里的 ScheduleInvalidate。
        typeof(WorkflowLinkOverlay).GetMethod("AttachTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [graph.Tree]);
        // 没进窗口就没有 Loaded，SendUnloaded 会因 _isLoadedFired=false 直接返回：直接触发宿主真实会发的 Unloaded 事件。
        ((EventHandler?)UnloadedField.GetValue(overlay))?.Invoke(overlay, EventArgs.Empty);
        return new WeakReference(overlay);
    }
}
