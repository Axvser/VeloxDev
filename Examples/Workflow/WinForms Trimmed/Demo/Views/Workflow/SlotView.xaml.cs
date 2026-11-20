// VeloxDev customization: The glyph path and palette live here. WorkflowSlotAttachment owns the binding, the
// state tinting and drag-to-connect; this view only paints the one SVG glyph.
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WinForms.Xaml.Controls;

namespace Demo.Views.Workflow;

/// <summary>
/// A workflow slot port: one SVG glyph painted by this view, with the binding and interaction supplied by
/// <see cref="WorkflowSlotAttachment"/>.
/// </summary>
public sealed partial class SlotView : UserControl
{
    private readonly WorkflowSlotAttachment _attachment;

    /// <summary>Creates the port and applies the glyph and palette.</summary>
    public SlotView()
    {
        InitializeComponent();
        DoubleBuffered = true;

        _attachment = WorkflowSlotAttachment.For(this) ?? WorkflowSlotAttachment.Attach(this);
        _attachment.SlotPath = "M517.3248,511.488 m-123.6992,0 a123.6992,123.6992 0 1 0 247.3984,0 a123.6992,123.6992 0 1 0 -247.3984,0 Z M366.848,991.5904 a47.2064,47.2064 0 0 1 -15.36,-2.5088 A506.368,506.368 0 0 1 32.8704,655.36 a46.08,46.08 0 1 1 88.32,-26.2144 A414.0544,414.0544 0 0 0 383.9616,928.2048 a46.08,46.08 0 0 1 -15.104,89.6 Z M648.2944,997.888 a46.08,46.08 0 0 1 -13.1072,-90.2656 A413.952,413.952 0 0 0 920.9344,646.8608 a46.08,46.08 0 1 1 87.04,30.208 A506.3168,506.3168 0 0 1 674.5088,996.9408 a45.2608,45.2608 0 0 1 -13.1072,1.9456 Z M957.44,426.5984 a46.08,46.08 0 0 1 -44.1344,-32.9728 A414.0544,414.0544 0 0 0 652.544,120.9728 a46.08,46.08 0 1 1 30.1568,-87.04 A506.368,506.368 0 0 1 991.6416,467.3984 a46.08,46.08 0 0 1 -31.0272,57.2928 a45.2608,45.2608 0 0 1 -13.1584,1.8944 Z M83.3024,407.0912 a46.08,46.08 0 0 1 -43.5712,-61.44 A506.4704,506.4704 0 0 1 373.248,26.9824 a46.08,46.08 0 1 1 26.112,88.3712 A413.952,413.952 0 0 0 100.7104,367.0528 a46.08,46.08 0 0 1 -43.52,31.0272 Z";
        _attachment.SlotBackground = WorkflowSurfaceColors.Parse("#01000000");
        _attachment.StandbyColor = WorkflowSurfaceColors.Parse("#DD1E1E1E");
        _attachment.BorderColor = WorkflowSurfaceColors.Parse("#FFFFFFFF");

        // Extension point: keep drag-to-connect enabled unless the port is display-only.
        WorkflowSlotConnectionBehavior.SetIsEnabled(this, true);
    }

    /// <summary>Gets or sets the slot this port renders.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowSlotViewModel? ViewModel
    {
        get => _attachment.Slot;
        set => _attachment.Slot = value;
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(System.Windows.Forms.PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
    }

    /// <inheritdoc />
    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        _attachment.Paint(e.Graphics);
    }
}
