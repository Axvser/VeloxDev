using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.TransitionSystem;
using VeloxDev.WinUI.Interpolators;
using Windows.Foundation;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Rec0.RenderTransform = CreateRec0Transform();
            Rec0.Fill = CreateRec0Brush();

            // VeloxDev库在 WinUI 执行动画时，必须手动获取主窗口
            UIThreadInspector.SetWindow(this);

            // VeloxDev动画的核心概念是 "一切皆状态"
            // Snapshot(...) 记录显式指定的属性路径
            // SnapshotAll() 自动发现并记录当前对象中可动画的属性

            var snapshot0 = Rec0.SnapshotAll();
            var snapshot1 = Rec0.Snapshot(x => x.RenderTransform, x => x.Fill);
            var snapshot2 = Rec0.SnapshotExcept(x => x.Visibility);

            // 于是，可以加载指向 snapshot 的过渡效果
            // 这里记录的快照是初始状态

            btnReset.Click += (s, e) =>
            {
                CreateRec0Reset().Execute(Rec0);
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

    public sealed partial class MainWindow
    {
        // ⚠ 在WinUI中创建 Transition<> 需要特别注意 :
        //
        // static 的 Transition<> 字段在 非UIThread 中使用，可能抛出 TypeInitialization 等异常
        // 若希望是 static 的，需要确保这个字段在 UIThread 中实例化

        // 简单动画：演示嵌套属性路径，直接修改 RenderTransform.X，同时配合渐变变化
        private readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, 400)
                .Property(r => ((LinearGradientBrush)r.Fill).StartPoint, new Point(0, 1))
                .Property(r => ((LinearGradientBrush)r.Fill).EndPoint, new Point(1, 1))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(1),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        private static TranslateTransform CreateRec0Transform()
        {
            return new TranslateTransform() { X = 0, Y = 0 };
        }

        private static LinearGradientBrush CreateRec0Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Cyan, Offset = 0 },
                    new GradientStop() { Color = Colors.Yellow, Offset = 1 },
                ]
            };
        }

        private static Transition<Rectangle>.StateSnapshot CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RenderTransform, [CreateRec0Transform()])
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

        // 延迟动画：反转旋转
        private readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(3))
                .Interpolator((Rectangle r) => r.RenderTransform, new ReverseTransformInterpolator())
                .Property(r => r.RenderTransform, [new RotateTransform() { Angle = 180 }])
                .Property(r => r.Fill, new LinearGradientBrush()
                {
                    GradientStops =
                    [
                        new GradientStop(){Color = Colors.Cyan,Offset=0},
                        new GradientStop(){Color= Colors.Red,Offset=0},
                    ]
                })
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        // 拼接动画：反转投影旋转 + 颜色变化
        private readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
                .Interpolator((Rectangle r) => r.Projection, new ReverseProjectionInterpolator())
                .Property(r => r.Projection,
                    new PlaneProjection()
                    {
                        RotationX = 180,
                        RotationY = 180,
                        CenterOfRotationX = 0.5,
                        CenterOfRotationY = 0.5
                    })
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
}
