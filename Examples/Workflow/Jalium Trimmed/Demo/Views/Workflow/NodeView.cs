// VeloxDev customization: The node card's chrome. This element is yours — the card is whatever DrawCard paints.
// The adapter's WorkflowNodeAttachment owns the binding, the placement, the viewbox scaffolding and the port glyphs
// (it hosts one SlotView per port at the layout's positions). Rename SlotView below if you renamed that item.
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Demo.ViewModels.Workflow;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A poolable node card: white rounded card, semi-bold title, labeled output rows, and the port glyphs.
/// </summary>
public sealed class NodeView : Canvas
{
    private const string FontFamilyName = "Segoe UI";

    private static readonly Brush s_titleBrush =
        new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E));

    private readonly WorkflowNodeAttachment node;

    public NodeView()
    {
        // One call attaches the rest: binding, placement, the scale-collapsing viewbox and the port glyphs.
        node = WorkflowNodeAttachment.Attach(this);
        node.PortLayout = SlotView.Layout;
        node.SlotViewFactory = _ => new SlotView();
        node.Render += (_, e) => DrawCard(e.Context);

        // 端口由模型给出；标题不是端口，由这张卡声明。
        node.NodeTitle = static vm => (vm as NodeViewModel)?.Name ?? string.Empty;

        // 输入口排成一列 —— 默认排布只画第一个，这里改成任意多个都画得出来。
        node.LayoutPorts = static card =>
        {
            var layout = card.PortLayout;
            double y = layout.TitleBarH + layout.RowH / 2;

            foreach (var (slot, _) in card.Inputs)
            {
                card.PlacePort(slot, layout.InputPortX, y, layout.InputPortRadius);
                y += layout.RowH;
            }

            y = layout.TitleBarH + layout.RowH / 2;
            foreach (var (slot, _) in card.Outputs)
            {
                card.PlacePort(slot, layout.DesignWidth - layout.OutputInset, y, layout.OutputPortRadius);
                y += layout.RowH;
            }
        };
    }

    /// <summary>Gets the attachment, for a card that wants the node, the layout or the model events.</summary>
    public WorkflowNodeAttachment Attachment => node;

    // VeloxDev customization: the card's chrome. Delete or replace anything below — this is the whole drawing.
    private void DrawCard(DrawingContext dc)
    {
        var node = Attachment.Node;
        if (node is null) return;

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Colors.White),
            new Pen(
                new SolidColorBrush(Color.FromArgb(0x33, 0x1E, 0x1E, 0x1E)),
                1),
            new Rect(0, 0, Attachment.PortLayout.DesignWidth, Attachment.PortLayout.DesignHeight),
            6, 6);

        var title = new FormattedText(Attachment.NodeTitle?.Invoke(node) ?? string.Empty, FontFamilyName, 14)
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
