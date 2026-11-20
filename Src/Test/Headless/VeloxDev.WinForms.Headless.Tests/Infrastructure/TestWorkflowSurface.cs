using System.ComponentModel;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using NativeControl = System.Windows.Forms.Control;
using NativePanel = System.Windows.Forms.Panel;

namespace VeloxDev.WinForms.Headless.Tests.Infrastructure;

// The surface is a plain root control plus named parts, exactly like a migrated template: no base class,
// no InitializeSurface. The harness only supplies a template selector and a template-owned grid decorator.
internal sealed class TestWorkflowSurface : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    public TestWorkflowSurface(bool typedViews = false, bool xamlScrollViewer = false)
    {
        Size = new System.Drawing.Size(800, 600);

        if (xamlScrollViewer)
        {
            var viewer = new VeloxDev.WinForms.Xaml.Controls.ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Dock = DockStyle.Fill,
            };

            XamlScrollViewer = viewer;
            ScrollViewer = viewer;
        }
        else
        {
            ScrollViewer = new VeloxDev.WinForms.Xaml.Controls.ScrollableControl
            {
                Name = "PART_ScrollViewer",
                Dock = DockStyle.Fill,
                AutoScroll = false,
            };
        }

        Canvas = new VeloxDev.WinForms.Xaml.Controls.Canvas
        {
            Name = "PART_Canvas",
            Dock = DockStyle.Fill,
        };

        ScrollViewer.Controls.Add(Canvas);

        GridDecorator = new TestGridDecorator
        {
            Name = "PART_GridDecorator",
            Dock = DockStyle.Fill,
        };
        GridDecorator.Controls.Add(ScrollViewer);

        Controls.Add(GridDecorator);

        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_GridDecorator");

        if (typedViews)
        {
            WorkflowSurfaceBehavior.SetNodeViewType(this, typeof(HeadlessNodeView).FullName);
            WorkflowSurfaceBehavior.SetLinkViewType(this, typeof(HeadlessLinkView).FullName);
        }
        else
        {
            WorkflowSurfaceBehavior.SetTemplateSelector(this, new TestTemplateSelector());
        }

        WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowSurfaceBehavior.SetZoomEnabled(this, true);
    }

    public System.Windows.Forms.ScrollableControl ScrollViewer { get; }

    /// <summary>The XAML scroll viewer viewport, or <see langword="null"/> when the old ScrollableControl one is used.</summary>
    public VeloxDev.WinForms.Xaml.Controls.ScrollViewer? XamlScrollViewer { get; }

    public VeloxDev.WinForms.Xaml.Controls.Canvas Canvas { get; }

    public TestGridDecorator GridDecorator { get; }
}

// A template-owned decorator: the adapter only defines the contract.
internal sealed class TestGridDecorator : NativePanel, IWorkflowGridDecorator
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY { get; set; }

    public double RulerBand => 0;
}

internal sealed class TestTemplateSelector : IWorkflowTemplateSelector
{
    public NativeControl CreateView(object item) => new NativePanel
    {
        Width = item is IWorkflowNodeViewModel ? 160 : 12,
        Height = item is IWorkflowNodeViewModel ? 90 : 12,
    };
}

// Simple-name resolution uses the surface's own namespace, so the typed views sit next to it.
internal sealed class HeadlessNodeView : NativePanel
{
}

internal sealed class HeadlessLinkView : NativePanel
{
}
