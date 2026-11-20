// VeloxDev customization: Per-link view (opt-in). The default surface draws links in one WorkflowLinkLayer pass;
// change WorkflowLinkLayer.LineColor / Thickness on the tree's PART_Canvas for that path. Use this item only when a
// host gives each link its own control; its palette then applies.
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VeloxDev.WinForms.Xaml.Controls;

namespace TemplateNamespace;

/// <summary>
/// A passive workflow link view that paints the resting curve. Hover feedback belongs to the host.
/// </summary>
public sealed partial class TemplateClass : UserControl
{
    private readonly WorkflowLinkAttachment _attachment;

    /// <summary>Creates the link view and applies the generated palette.</summary>
    public TemplateClass()
    {
        InitializeComponent();
        DoubleBuffered = true;
        SetStyle(
            System.Windows.Forms.ControlStyles.UserPaint |
            System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
            System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer,
            true);

        _attachment = WorkflowLinkAttachment.For(this) ?? WorkflowLinkAttachment.Attach(this);
        _attachment.SurfaceBackground = WorkflowSurfaceColors.Parse("TemplateSurfaceBackground");
        _attachment.LineColor = WorkflowSurfaceColors.Parse("TemplateLinkColor");
        _attachment.Thickness = float.Parse("TemplateLinkThickness", CultureInfo.InvariantCulture);
    }

    /// <summary>Gets or sets the link this view renders.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IWorkflowLinkViewModel? ViewModel
    {
        get => _attachment.Link;
        set => _attachment.Bind(value);
    }

    /// <inheritdoc />
    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        _attachment.Paint(e.Graphics);
    }
}
