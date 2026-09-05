// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
namespace Demo.Controls;

public partial class TreeView : ContentView
{
    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();

        // The tree is assigned to this control by the page; propagate it explicitly so the HUD's
        // BindingContextChanged fires even if inheritance doesn't reach the nested overlay.
        BindingContextChanged += (_, _) => InfoOverlay.BindingContext = BindingContext;
    }
}
