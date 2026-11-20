// VeloxDev customization: The glyph path and palette are the extension points.
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WinForms.Xaml.Controls;

namespace TemplateNamespace;

/// <summary>
/// A workflow slot port: one SVG glyph painted by this view, with the binding and interaction supplied by
/// <see cref="WorkflowSlotAttachment"/>.
/// </summary>
public sealed partial class TemplateClass : UserControl
{
    private readonly WorkflowSlotAttachment _attachment;

    /// <summary>Creates the port and applies the generated glyph and palette.</summary>
    public TemplateClass()
    {
        InitializeComponent();
        DoubleBuffered = true;

        _attachment = WorkflowSlotAttachment.For(this) ?? WorkflowSlotAttachment.Attach(this);
        _attachment.SlotPath = "TemplateSlotPath";
        _attachment.SlotBackground = WorkflowSurfaceColors.Parse("TemplateSlotBackground");
        _attachment.StandbyColor = WorkflowSurfaceColors.Parse("TemplateSlotColor");
        _attachment.BorderColor = WorkflowSurfaceColors.Parse("TemplateSlotBorderColor");

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
