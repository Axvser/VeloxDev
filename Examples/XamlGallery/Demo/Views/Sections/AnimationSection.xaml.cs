using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Controls;

namespace Demo.Views.Sections;

/// <summary>The trigger, keyframe and visual-state examples.</summary>
public sealed partial class AnimationSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the section and configures the visual states.</summary>
    public AnimationSection()
    {
        InitializeComponent();
        ConfigureStates();
    }

    private void ConfigureStates()
    {
        var group = new VisualStateGroup { Name = "Card" };
        group.States.Add(CreateState("Normal", Color.FromArgb(0xFF, 0x20, 0x24, 0x2D), Color.FromArgb(0xFF, 0x2C, 0x33, 0x40), 10f));
        group.States.Add(CreateState("Hot", Color.FromArgb(0xFF, 0x24, 0x36, 0x5B), Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF), 18f));
        group.States.Add(CreateState("Pinned", Color.FromArgb(0xFF, 0x2E, 0x24, 0x40), Color.FromArgb(0xFF, 0x9B, 0x6B, 0xFF), 26f));

        VisualStateManager.SetVisualStateGroups(vsmCard, new List<VisualStateGroup> { group });
        VisualStateManager.GoToState(vsmCard, "Normal", useTransitions: false);
    }

    private static VisualState CreateState(string name, Color background, Color border, float radius)
    {
        var state = new VisualState { Name = name };
        state.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(background)));
        state.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(border)));
        state.Setters.Add(new Setter(Border.CornerRadiusProperty, radius));
        return state;
    }

    private void OnNormalStateClick(object? sender, EventArgs e) => Go("Normal");

    private void OnHotStateClick(object? sender, EventArgs e) => Go("Hot");

    private void OnPinnedStateClick(object? sender, EventArgs e) => Go("Pinned");

    private void Go(string state)
    {
        VisualStateManager.GoToState(vsmCard, state, useTransitions: true);
        vsmLabel.Text = state;
    }
}
