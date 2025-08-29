using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.TransitionSystem;

namespace Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Animation0.Execute(Rec1);
        Animation1.Execute(Rec2);
    }
}

public partial class MainWindow
{
    // 简单动画
    private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform(800, 0)])
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // 复杂动画
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(2))
            .Property(r => r.RenderTransform, 
                [
                    new TranslateTransform(200,0),
                    new Rotate3DTransform(210,30,50,0,0,0,1),
                    new ScaleTransform(1.3, 1.3)
                ])
            .Property(r => r.Fill, new SolidColorBrush(Colors.Red))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                FPS = 144, // 若启用缓动函数，则推荐设置高帧率以获取更丝滑的视觉效果
                EaseCalculator = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                EaseCalculator = Eases.Sine.In
            });
}