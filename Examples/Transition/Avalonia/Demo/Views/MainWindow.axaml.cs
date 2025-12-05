using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using VeloxDev.Avalonia.PlatformAdapters;
using VeloxDev.Core.TransitionSystem;

namespace Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // VeloxDev动画的核心概念是 "一切皆状态"
        // 对象可以调用 Snapshot() 创建快照
        // 其中，若 Snapshot() 不指定目标属性，则视作记录所有可读可写的、可插值的实例属性

        var snapshot0 = Rec0.Snapshot();
        var snapshot1 = Rec0.Snapshot(x => x.RenderTransform, x => x.Fill);
        var snapshot2 = Rec0.SnapshotExcept(x => x.Width, x => x.Height, x=>x.Fill, x=>x.Opacity);

        // 于是，可以加载指向 snapshot 的过渡效果
        // 这里记录的快照是初始状态

        btnReset.Click += (s, e) =>
        {
            snapshot1.Effect(TransitionEffects.Empty).Execute(Rec0);
        };
    }

    private void LoadAnimations(object sender, RoutedEventArgs e)
    {
        // 直接从 snapshot 对象启动过渡动画
        // 默认对象只允许同时执行一个动画，即 CanMutualTask: true，新来的会打断正在执行的

        //Animation0.Execute(Rec0, CanMutualTask: false);
        //Animation1.Execute(Rec1);
        //Animation2.Execute(Rec2);

        // 也可以直接在非UI线程中启动动画，框架会自动切换到 UI 线程

        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        });
    }

    private void ExitAnimations(object sender, RoutedEventArgs e)
    {
        // 终结对象持有的动画
        // IncludeMutual   表示是否终结设定了 CanMutualTask: true 的动画
        // IncludeNoMutual 表示是否终结设定了 CanMutualTask: false 的动画

        Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: false);
        Transition.Exit(Rec1);
        Transition.Exit(Rec2);

        // 当然，也可以从核心库提供的方法寻找到动画的 Scheduler
        // Scheduler 对象拥有 Execute() 和 Exit() 的能力

        //if (TransitionSchedulerCore.TryGetMutualScheduler(Rec0, out var MutualScheduler) &&
        //   TransitionSchedulerCore.TryGetNoMutualScheduler(Rec0, out var noMutualSchedulers))
        //{
        //    ITransitionSchedulerCore[] schedulers = [MutualScheduler!, .. noMutualSchedulers];
        //    foreach (var scheduler in schedulers)
        //    {
        //        scheduler.Exit();
        //    }
        //}
    }
}

public partial class MainWindow
{
    // 简单动画：移动 + 背景线性渐变
    private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform(400, 0)])
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.DeepSkyBlue, 0),
                        new GradientStop(Colors.MediumPurple, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut
            });

    // 延迟动画：旋转+移动+背景渐变
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new TranslateTransform(-200, 0), new RotateTransform(180)])
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.OrangeRed, 0),
                        new GradientStop(Colors.Yellow, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(4),
                IsAutoReverse = true,
                FPS = 144,
                LoopTime = 4,
            });

    // 拼接动画：三维旋转 + 放缩 + 再切换渐变背景
    private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new Rotate3DTransform(180, 180, 0, 0, 0, 0, 0),
                new ScaleTransform(1.3, 1.3)
            ])
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.LightSeaGreen, 0),
                        new GradientStop(Colors.CadetBlue, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Yellow, 0),
                        new GradientStop(Colors.Lime, 1)
                    }
                })
            .Effect((x) =>
            {
                x.Duration = TimeSpan.FromSeconds(4);
                x.FPS = 144;
                x.Ease = Eases.Sine.In;
            });
}