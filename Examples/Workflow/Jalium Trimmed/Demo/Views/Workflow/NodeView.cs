// VeloxDev customization: The node card's chrome. The adapter's WorkflowNodeView owns the binding, the placement,
// the viewbox scaffolding and the port glyphs (it hosts one SlotView per port at the layout's positions); this file
// draws the card behind them and the output row labels. Rename SlotView below if you renamed that item.
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A poolable node card: white rounded card, semi-bold title, labeled output rows, and the port glyphs.
/// </summary>
public sealed class NodeView : WorkflowNodeView
{
    private const string FontFamilyName = "Segoe UI";

    private static readonly Brush s_titleBrush =
        new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E));

    public NodeView()
    {
        PortLayout = SlotView.Layout;
        SlotViewFactory = _ => new SlotView();
    }

    /// <inheritdoc />
    protected override void DrawCard(DrawingContext dc)
    {
        if (Node is null) return;

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Colors.White),
            new Pen(
                new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)),
                1),
            new Rect(0, 0, PortLayout.DesignWidth, PortLayout.DesignHeight),
            6, 6);

        var title = new FormattedText(WorkflowPortGeometry.TitleOf(Node), FontFamilyName, 14)
        {
            Foreground = s_titleBrush,
            FontWeight = 600,
        };
        dc.DrawText(title, new Point(12, 9));

        // Port glyphs come from the base-hosted SlotView; only the output row text is drawn here.
        var outputs = WorkflowPortGeometry.Outputs(Node);
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].Name.Length == 0) continue;

            double rowCenter = PortLayout.TitleBarH + PortLayout.RowH * i + PortLayout.RowH / 2.0;
            var label = new FormattedText(outputs[i].Name, FontFamilyName, 12) { Foreground = s_titleBrush };
            TextMeasurement.MeasureText(label);
            dc.DrawText(label, new Point(PortLayout.DesignWidth - 32 - label.Width, rowCenter - label.Height / 2.0));
        }
    }
}
