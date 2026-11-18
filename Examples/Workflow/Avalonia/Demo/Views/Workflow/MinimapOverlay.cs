using Avalonia.Media;
using Avalonia.Media.Immutable;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface. It delegates data
/// subscription and drag/click navigation to the canonical <see cref="WorkflowMinimapOverlay"/>
/// adapter (same data/logic as the full demo), and adds only the unified palette — the base paints
/// nothing without it.
/// </summary>
public class MinimapOverlay : WorkflowMinimapOverlay
{
    public MinimapOverlay()
    {
        // 与外壳同一套工作台色：安静的面板底、蓝色节点像素、白色视口框。
        MinimapBackground = CreateBrush("#D21F1F1F");
        MinimapBorderBrush = CreateBrush("#DC404040");
        NodeBrush = CreateBrush("#DC6E8BFF");
        ViewportStroke = CreateBrush("#F0E7E7E7");
    }

    private static IBrush CreateBrush(string hex)
        => new ImmutableSolidColorBrush(Color.Parse(hex));
}
