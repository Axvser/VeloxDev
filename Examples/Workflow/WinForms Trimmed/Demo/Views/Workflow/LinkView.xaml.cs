// VeloxDev customization: The line colour and thickness are the extension points. WorkflowLinkAttachment owns
// the endpoints, geometry and hit test; this view only draws the resting curve.
using System.Globalization;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>A workflow link: one painted resting curve. Hover feedback belongs to the host.</summary>
public sealed partial class LinkView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private readonly WorkflowLinkAttachment _attachment;

    /// <summary>Creates the link view and applies the palette.</summary>
    public LinkView()
    {
        InitializeComponent();
        DoubleBuffered = true;
        SetStyle(
            System.Windows.Forms.ControlStyles.UserPaint |
            System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
            System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer,
            true);

        _attachment = WorkflowLinkAttachment.For(this) ?? WorkflowLinkAttachment.Attach(this);
        _attachment.SurfaceBackground = WorkflowSurfaceColors.Parse("#1E1E1E");
        _attachment.LineColor = WorkflowSurfaceColors.Parse("#DDFFFFFF");
        _attachment.Thickness = float.Parse("2", CultureInfo.InvariantCulture);
    }

    /// <summary>Gets or sets the link this view renders.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ILinkComponent? ViewModel
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
