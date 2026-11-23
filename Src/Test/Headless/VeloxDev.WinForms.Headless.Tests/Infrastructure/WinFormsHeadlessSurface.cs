using System.Drawing;
using System.Windows.Forms;
using VeloxDev.AdapterTestKit;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using DrawingPointF = System.Drawing.PointF;

namespace VeloxDev.WinForms.Headless.Tests.Infrastructure;

// WinForms has no dispatcher/headless platform package: headless means "create the handle but never Show()".
// The surface is one VisualHost; the camera, viewport write-back and virtualization all live in the behavior.
internal sealed class WinFormsHeadlessSurface : IDisposable
{
    public WinFormsHeadlessSurface(TestGraph graph, bool bind = true, bool withMinimap = false)
    {
        Graph = graph;
        View = new TestWorkflowSurface();

        View.CreateControl();
        View.PerformLayout();

        if (withMinimap)
        {
            Minimap = new WorkflowMinimapOverlay { Name = "PART_Minimap" };
            View.RootContainer.Add(Minimap);
            WorkflowSurfaceBehavior.SetMinimapOverlayName(View, "PART_Minimap");
        }

        if (bind)
        {
            WorkflowSurfaceBehavior.SetWorkflowTree(View, graph.Tree);
            // Bind the pool to the virtualized set the helper owns, exactly like the generated template.
            ViewPool.SetItemsSource(View.Canvas, graph.Tree.GetHelper().VisibleItems);
        }

        WorkflowSurfaceBehavior.SetIsEnabled(View, true);
        Render();
    }

    public TestGraph Graph { get; }

    public TestWorkflowSurface View { get; }

    public WorkflowMinimapOverlay? Minimap { get; }

    /// <summary>Runs two layout/render passes and drains queued work.</summary>
    /// <remarks>
    /// The first pass measures pooled views that were added during virtualisation; slot anchors and link curves
    /// are only stable after that measurement, so a second pass is what tests assert against.
    /// </remarks>
    public void Render()
    {
        View.RenderSurface();
        View.RenderSurface();
        Application.DoEvents();
    }

    // Goes through the adapter's registered scroll route, the one a wheel or an external host would drive.
    public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
    {
        WorkflowInput.For(Graph.Tree).Scroller?.ScrollBy(wheelDeltaX, wheelDeltaY);
        Render();
    }

    public bool Press(DrawingPointF surfacePoint)
        => View.ProcessPointerDown(surfacePoint, MouseButtons.Left);

    public bool MoveTo(DrawingPointF surfacePoint)
        => View.ProcessPointerMove(surfacePoint);

    public bool Release(DrawingPointF surfacePoint)
        => View.ProcessPointerUp(surfacePoint, MouseButtons.Left);

    public DrawingPointF WorldToSurface(DrawingPointF worldPoint)
        => WorkflowSurfaceBehavior.WorldToSurface(View, worldPoint);

    public DrawingPointF SurfaceToWorld(DrawingPointF surfacePoint)
        => WorkflowSurfaceBehavior.SurfaceToWorld(View, surfacePoint);

    public void Dispose()
    {
        WorkflowSurfaceBehavior.SetIsEnabled(View, false);
        View.Dispose();
        Application.DoEvents();
    }
}
