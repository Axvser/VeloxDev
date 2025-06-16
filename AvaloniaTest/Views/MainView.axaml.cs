﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using VeloxDev.Avalonia.TransitionSystem;
using VeloxDev.Core.Generators;

namespace AvaloniaTest.Views;

[MonoBehaviour]
public partial class MainView : UserControl
{
    [AspectOriented]
    public void Do()
    {

    }

    public MainView()
    {
        InitializeComponent();
        Transition.Create(t2)//执行第一段动画
            .Property(x => x.Background, Brushes.Red)
            .Property(x => x.CornerRadius, new CornerRadius(20))
            .Property(x => x.RenderTransform, [new ScaleTransform(100, 1)])
            .Effect(p => p.Duration = TimeSpan.FromSeconds(4))
            .Then() // 执行下一段动画
            .Property(x => x.Background, Brushes.Blue)
            .Property(x => x.CornerRadius, new CornerRadius(1))
            .Property(x => x.RenderTransform, [new ScaleTransform(1, 1)])
            .Effect((p) =>
            {
                p.Duration = TimeSpan.FromSeconds(3);
                p.Start += (s, e) =>
                {
                    t1.Text = t1.Opacity.ToString();
                };
            })
            .Start();
    }
}
