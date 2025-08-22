using Avalonia.Controls;
using System;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;

namespace AvaloniaApplication1.Views;

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        InitializeTheme();
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.Transition<Light>(new TransitionEffect()
        {
            FPS = 120,
            Duration = TimeSpan.FromSeconds(3),
            EaseCalculator = Eases.Circ.InOut
        });
    }
}
