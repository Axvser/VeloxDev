// VeloxDev customization: the enum selector card's typography and accent, from the shared card theme.
using Easel;

namespace Demo.Views.Workflow;

/// <summary>The enum selector node card: value / compile-mode pickers and one glyph per current output slot.</summary>
public sealed partial class EnumSelectorNodeView : Easel.Visuals.Controls.UserControl
{
    /// <summary>Creates the card and runs the generated initializer.</summary>
    public EnumSelectorNodeView()
    {
        InitializeComponent();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        accentBar.Background = new SolidColorBrush(CardTheme.AccentEnum);
        CardStyle.Title(titleText);
        CardStyle.Value(orderText);
        CardStyle.Capsule(routedText);
        CardStyle.Label(valueLabel);
        CardStyle.Label(modeLabel);
        CardStyle.Label(outputLabel);
        CardStyle.PortNames(PART_OutputSlotsList);
    }
}
