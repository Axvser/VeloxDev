using Avalonia.Controls;

namespace Demo;

public partial class TreeView : UserControl
{
    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.ScrollChanged += (_, _) => InfoOverlay.Refresh();
    }
}
