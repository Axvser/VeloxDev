using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WPF.PlatformAdapters;

namespace Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        //Animation0.Execute(Rec0);
        //Animation1.Execute(Rec1);
        //Animation2.Execute(Rec2);

        // 可以直接在其它线程中启动动画，框架会自动切换到 UI 线程执行插值操作

        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        });

        // TransitionCore.Exit(Rec0); 安全地退出插值动画
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

    // 延迟动画
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new RotateTransform(180)])
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // 拼接动画
    private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new ScaleTransform(1.3, 1.3)
            ])
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5)) // 等待 5秒再开始下一段动画
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Sine.In
            });
}