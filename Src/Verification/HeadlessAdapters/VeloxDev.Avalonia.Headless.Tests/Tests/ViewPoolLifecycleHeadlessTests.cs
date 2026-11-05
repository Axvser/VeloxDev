using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

/// <summary>
/// Swapping the template selector rebuilds the pool's manager; the panel must still carry exactly one
/// detach handler, or every swap leaves another delegate rooting the pool on the panel.
/// </summary>
[TestClass]
public sealed class ViewPoolLifecycleHeadlessTests
{
    private static HeadlessUnitTestSession _session = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
        => _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    [ClassCleanup]
    public static void ClassCleanup() => _session.Dispose();

    [TestMethod]
    public void TemplateSelectorSwaps_KeepASingleDetachHandler()
        => _session.Dispatch(() =>
        {
            var panel = new Canvas();
            Behaviors.ViewPool.SetItemsSource(panel, new ObservableCollection<object>());

            for (var i = 0; i < 5; i++)
            {
                Behaviors.ViewPool.SetTemplateSelector(panel, new FuncDataTemplate<object>((_, _) => new Border()));
            }

            Assert.AreEqual(1, CountPoolDetachHandlers(panel));
        }, CancellationToken.None).GetAwaiter().GetResult();

    // 事件的后备委托在 Visual 上；只数 ViewPool 自己挂的那一个处理器，别的订阅者不算。
    private static int CountPoolDetachHandlers(Visual visual)
    {
        var field = typeof(Visual).GetField("DetachedFromVisualTree", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Visual.DetachedFromVisualTree backing field not found");
        var handler = field.GetValue(visual) as Delegate;

        return handler?.GetInvocationList().Count(d => d.Method.DeclaringType == typeof(Behaviors.ViewPool)) ?? 0;
    }
}
