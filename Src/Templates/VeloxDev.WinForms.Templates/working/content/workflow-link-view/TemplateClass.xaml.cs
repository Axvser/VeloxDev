// VeloxDev customization: per-link view (opt-in); its palette is set here.
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed partial class TemplateClass : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private readonly WorkflowLinkAttachment _attachment;

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

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ILinkComponent? ViewModel
    {
        get => _attachment.Link;
        set => _attachment.Bind(value);
    }

    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
    {
        base.OnPaint(e);
        _attachment.Paint(e.Graphics);
    }
}
