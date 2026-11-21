using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Threading;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using Behaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Avalonia.Headless.Tests.Tests;

/// <summary>
/// Drives the Core-owned view pool through the Avalonia host: the visible set must become child views,
/// and a removed item must leave its view hidden for reuse instead of rebuilding it.
/// </summary>
[TestClass]
public sealed class ViewPoolHeadlessTests
{
    private static HeadlessUnitTestSession _session = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
        => _session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    [ClassCleanup]
    public static void ClassCleanup() => _session.Dispose();

    [TestMethod]
    public void VisibleItemsBecomeChildViews_AndRemovedViewsStayHidden()
        => _session.Dispatch(() =>
        {
            var graph = TestGraph.Create();
            var panel = new Canvas();
            var source = new ObservableCollection<IWorkflowComponent>();
            Behaviors.ViewPool.SetTemplateSelector(
                panel, new FuncDataTemplate<IWorkflowComponent>((_, _) => new Border()));
            Behaviors.ViewPool.SetItemsSource(panel, source);

            source.Add(graph.Left);
            source.Add(graph.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(2, panel.Children.Count);
            var leftView = panel.Children.OfType<Control>()
                .Single(view => ReferenceEquals(view.DataContext, graph.Left));
            var rightView = panel.Children.OfType<Control>()
                .Single(view => ReferenceEquals(view.DataContext, graph.Right));

            source.Remove(graph.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(2, panel.Children.Count, "a recycled view stays in the panel");
            Assert.IsFalse(leftView.IsVisible);
            Assert.IsTrue(rightView.IsVisible);
        }, CancellationToken.None).GetAwaiter().GetResult();

    [TestMethod]
    public void WithoutASelector_UsesThePanelDataTemplates()
        => _session.Dispatch(() =>
        {
            var graph = TestGraph.Create();
            var panel = new Canvas();
            panel.DataTemplates.Add(new FuncDataTemplate<IWorkflowComponent>((_, _) => new Border()));
            var source = new ObservableCollection<IWorkflowComponent> { graph.Left };
            Behaviors.ViewPool.SetItemsSource(panel, source);
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(1, panel.Children.Count);
            Assert.AreSame(graph.Left, panel.Children[0].DataContext);
        }, CancellationToken.None).GetAwaiter().GetResult();

    [TestMethod]
    public void TemplateSelectorSwaps_KeepASingleDetachHandler()
        => _session.Dispatch(() =>
        {
            var panel = new Canvas();
            Behaviors.ViewPool.SetItemsSource(panel, new ObservableCollection<IWorkflowComponent>());

            for (var i = 0; i < 5; i++)
            {
                Behaviors.ViewPool.SetTemplateSelector(
                    panel, new FuncDataTemplate<IWorkflowComponent>((_, _) => new Border()));
            }

            Assert.AreEqual(1, CountViewPoolDetachHandlers(panel));
        }, CancellationToken.None).GetAwaiter().GetResult();

    // 事件的后备委托在 Visual 上；只数 ViewPool 自己挂的那一个处理器。
    private static int CountViewPoolDetachHandlers(Visual visual)
    {
        var field = typeof(Visual).GetField("DetachedFromVisualTree", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Visual.DetachedFromVisualTree backing field not found");
        var handler = field.GetValue(visual) as Delegate;

        return handler?.GetInvocationList().Count(d => d.Method.DeclaringType == typeof(Behaviors.ViewPool)) ?? 0;
    }
}
