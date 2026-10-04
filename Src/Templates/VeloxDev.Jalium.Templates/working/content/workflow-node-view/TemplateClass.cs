// VeloxDev customization: The node card's chrome. This element is yours — the card is whatever DrawCard paints.
// The adapter's WorkflowNodeAttachment owns the binding, the placement, the viewbox scaffolding and the port glyphs
// (it hosts one SlotView per port at the layout's positions). Rename SlotView below if you renamed that item.
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A poolable node card: white rounded card, semi-bold title, labeled output rows, and the port glyphs.
/// </summary>
public sealed class TemplateClass : Canvas
{
    private const string FontFamilyName = "Segoe UI";

    private static readonly Brush s_titleBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("TemplateNodeForeground"));

    private readonly WorkflowNodeAttachment node;

    public TemplateClass()
    {
        // One call attaches the rest: binding, placement, the scale-collapsing viewbox and the port glyphs.
        node = WorkflowNodeAttachment.Attach(this);
        node.PortLayout = SlotView.Layout;
        node.SlotViewFactory = _ => new SlotView();
        node.Render += (_, e) => DrawCard(e.Context);
    }

    /// <summary>Gets the attachment, for a card that wants the node, the layout or the model events.</summary>
    public WorkflowNodeAttachment Attachment => node;

    // VeloxDev customization: the card's chrome. Delete or replace anything below — this is the whole drawing.
    private void DrawCard(DrawingContext dc)
    {
        var node = Attachment.Node;
        if (node is null) return;

        dc.DrawRoundedRectangle(
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("TemplateNodeBackground")),
            new Pen(
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("TemplateNodeBorderBrush")),
                TemplateNodeBorderThickness),
            new Rect(0, 0, Attachment.PortLayout.DesignWidth, Attachment.PortLayout.DesignHeight),
            TemplateNodeCornerRadius, TemplateNodeCornerRadius);

        var title = new FormattedText(WorkflowPortGeometry.TitleOf(node), FontFamilyName, 14)
        {
            Foreground = s_titleBrush,
            FontWeight = 600,
        };
        dc.DrawText(title, new Point(12, 9));

        // Port glyphs come from the base-hosted SlotView; only the output row text is drawn here.
        var outputs = WorkflowPortGeometry.Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].Name.Length == 0) continue;

            double rowCenter = Attachment.PortLayout.TitleBarH + Attachment.PortLayout.RowH * i + Attachment.PortLayout.RowH / 2.0;
            var label = new FormattedText(outputs[i].Name, FontFamilyName, 12) { Foreground = s_titleBrush };
            TextMeasurement.MeasureText(label);
            dc.DrawText(label, new Point(Attachment.PortLayout.DesignWidth - 32 - label.Width, rowCenter - label.Height / 2.0));
        }
    }
}
