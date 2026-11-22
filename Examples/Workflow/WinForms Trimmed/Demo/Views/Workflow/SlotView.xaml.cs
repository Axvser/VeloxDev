// VeloxDev customization: the ring colour and the standby fill are the two colours to change here.
using System.Drawing;
using System.Drawing.Drawing2D;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>A visual workflow slot glyph: a ring and a core whose fill follows the slot's connection state.</summary>
public sealed partial class SlotView : VisualWorkflowSlotView
{
    /// <summary>Creates the glyph and runs the generated initializer.</summary>
    public SlotView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        if (Slot is not { } slot)
        {
            return;
        }

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1f)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = Bounds.Width / 2f;
        var centerY = Bounds.Height / 2f;
        var ringRadius = size * 0.30f;
        var coreRadius = size * 0.13f;

        using var ring = new DrawingPen(XamlRuntime.ParseColor("#FFFFFFFF"), Math.Max(1f, size * 0.08f));
        graphics.DrawEllipse(ring, centerX - ringRadius, centerY - ringRadius, ringRadius * 2f, ringRadius * 2f);

        using var core = new SolidBrush(FillFor(slot.State));
        graphics.FillEllipse(core, centerX - coreRadius, centerY - coreRadius, coreRadius * 2f, coreRadius * 2f);
    }

    private static Color FillFor(SlotState state)
    {
        if (state.HasFlag(SlotState.Sender) && state.HasFlag(SlotState.Receiver))
        {
            return Color.Violet;
        }

        if (state.HasFlag(SlotState.Sender) || state.HasFlag(SlotState.PreviewSender))
        {
            return Color.Tomato;
        }

        if (state.HasFlag(SlotState.Receiver) || state.HasFlag(SlotState.PreviewReceiver))
        {
            return Color.Lime;
        }

        return XamlRuntime.ParseColor("#DD1E1E1E");
    }
}
