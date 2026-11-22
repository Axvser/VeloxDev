// VeloxDev customization: the card is composed in XAML; the surface attaches drag, slots and anchors.
using System.Drawing;

namespace TemplateNamespace;

/// <summary>A node card built from plain visual controls: a <see cref="VeloxDev.WinForms.Xaml.Visuals.Controls.Viewbox"/>
/// around the fixed design grid, with the title and subtitle bound to the node.</summary>
public sealed partial class TemplateClass : VeloxDev.WinForms.Xaml.Visuals.Controls.Grid
{
    private static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
    private static readonly Font SubtitleFont = new("Segoe UI", 8.5f);

    /// <summary>Creates the card and applies the text metrics.</summary>
    public TemplateClass()
    {
        InitializeComponent();
        titleBlock.Font = TitleFont;
        subtitleBlock.Font = SubtitleFont;
    }
}
