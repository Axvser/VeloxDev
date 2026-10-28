using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WinForms.Headless.Tests.Infrastructure;

// WinForms 没有 Dispatcher/Headless 平台包：无头 = 建句柄但不 Show()。
// 平移由 WorkflowTreeView 自己的有符号平移引擎执行，Refresh 把结果写回模型视口。
internal sealed class WinFormsHeadlessSurface : IDisposable
{
    public WinFormsHeadlessSurface(TestGraph graph, bool bind = true)
    {
        Graph = graph;
        View = new TestWorkflowTreeView { Size = new System.Drawing.Size(800, 600) };

        View.CreateControl();
        View.PerformLayout();

        if (bind)
        {
            View.ViewModel = graph.Tree;
        }

        View.PerformLayout();
        Application.DoEvents();
        WorkflowSurfaceBehavior.Refresh(View);
        Application.DoEvents();
    }

    public TestGraph Graph { get; }

    public TestWorkflowTreeView View { get; }

    // 走适配器注册在树上的滚动口，而不是直接改私有平移字段：这条才是滚轮/外部滚动真正走的路。
    public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
    {
        WorkflowInput.For(Graph.Tree).Scroller?.ScrollBy(wheelDeltaX, wheelDeltaY);
        Application.DoEvents();
        WorkflowSurfaceBehavior.Refresh(View);
        Application.DoEvents();
    }

    public void Dispose()
    {
        WorkflowSurfaceBehavior.SetIsEnabled(View, false);
        View.Dispose();
        Application.DoEvents();
    }
}
