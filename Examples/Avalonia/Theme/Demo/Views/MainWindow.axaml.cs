using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;

namespace Demo.Views;

[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#ffffff"])]
[ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
public partial class MainWindow : Window
{
    private readonly TransitionEffect _effect = new() { Duration = TimeSpan.FromSeconds(0.5), FPS = 144 };

    public MainWindow()
    {
        ThemeManager.SetPlatformInterpolator(new Interpolator());
        InitializeComponent();
        InitializeTheme();
    }

    private void User_Click(object? sender, RoutedEventArgs e)
    {
        if (ThemeManager.Current == typeof(Dark))
        {
            ThemeManager.Transition<Light>(_effect);
        }
        else
        {
            ThemeManager.Transition<Dark>(_effect);
        }
    }
}