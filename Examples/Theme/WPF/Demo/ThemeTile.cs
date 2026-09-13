using System.Windows;
using System.Windows.Controls;
using VeloxDev.DynamicTheme;
using VeloxDev.TransitionSystem;

namespace Demo;

/// <summary>
/// One themeable element. The whole point of the demo is how many of these there are, so it stays this small —
/// two themed properties and nothing else.
/// </summary>
/// <remarks>
/// Every instance registers itself with the theme system from its constructor, through the generated
/// <c>InitializeTheme</c>. That is why building N of them is all the demo has to do to put N targets into the next
/// switch: there is no per-element wiring, and no list to keep in step by hand.
/// </remarks>
[ThemeConfig<BrushConverter, Light, Dark>(nameof(Background), ["#ffffff"], ["#1e1e1e"])]
[ThemeConfig<BrushConverter, Light, Dark>(nameof(BorderBrush), ["#1e1e1e"], ["#ffffff"])]
public partial class ThemeTile : Border
{
    public ThemeTile()
    {
        Width = 26;
        Height = 26;
        Margin = new Thickness(2);
        BorderThickness = new Thickness(3);
        InitializeTheme();
    }
}
