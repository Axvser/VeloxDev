using System.Drawing;

namespace VeloxDev.WinForms.Xaml.Test.Fixtures;

/// <summary>A generated dependency-property host used as the derived control in the XAML fixture.</summary>
public partial class GeneratedCard : VeloxDev.WinForms.Xaml.Controls.Control
{
    /// <summary>The title bound from the XAML data context.</summary>
    [XamlProperty(DefaultValue = "no title")]
    public partial string Title { get; set; }

    /// <summary>The accent assigned from a static resource.</summary>
    [XamlProperty]
    public partial Color AccentColor { get; set; }

    /// <summary>The condition for the inline trigger declared in the fixture XAML.</summary>
    [XamlProperty]
    public partial bool IsHot { get; set; }

    /// <summary>The value the inline trigger's setter changes.</summary>
    [XamlProperty]
    public partial int BorderWidth { get; set; }

    /// <summary>The value the inline trigger's transition action animates.</summary>
    [XamlProperty]
    public partial double Glow { get; set; }

    /// <summary>A property reserved for the generated style tests, untouched by the inline trigger.</summary>
    [XamlProperty]
    public partial int StyleValue { get; set; }

    /// <summary>A second condition for MultiTrigger tests.</summary>
    [XamlProperty]
    public partial bool IsPinned { get; set; }

    /// <summary>A string target for DataTrigger/MultiTrigger setters.</summary>
    [XamlProperty]
    public partial string? Note { get; set; }
}
