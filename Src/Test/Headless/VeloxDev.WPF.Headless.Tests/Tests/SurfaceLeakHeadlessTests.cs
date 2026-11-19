using System.Runtime.CompilerServices;
using System.Windows;
using VeloxDev.AdapterTestKit;
using VeloxDev.WPF.Headless.Tests.Infrastructure;

namespace VeloxDev.WPF.Headless.Tests.Tests;

/// <summary>
/// A surface that leaves the visual tree must not stay rooted by the tree view model it was bound to.
/// </summary>
[STATestClass]
public sealed class SurfaceLeakHeadlessTests
{
    [TestMethod]
    public void UnloadedSurface_IsNotRootedByTheTree()
    {
        var graph = TestGraph.Create();
        var host = CreateAndUnload(graph);

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.IsFalse(host.IsAlive, "the tree view model must not keep an unloaded surface alive");
        GC.KeepAlive(graph);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndUnload(TestGraph graph)
    {
        var surface = new WpfHeadlessSurface(graph);
        var host = surface.Host;
        host.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, host));
        WpfHeadlessSurface.Pump();

        // 不走 Dispose：真实场景里视图离树不会被显式关闭，只会 Unloaded。
        return new WeakReference(host);
    }
}
