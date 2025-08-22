using Avalonia.Controls;
using System;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;

namespace AvaloniaApplication1.Views;

[ThemeConfig<BrushConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["msrc"])]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitializeTheme();
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        ThemeManager.Transition<Light>(new TransitionEffect()
        {
            FPS = 120,
            Duration = TimeSpan.FromSeconds(1),
            EaseCalculator = Eases.Default
        });
    }
}
