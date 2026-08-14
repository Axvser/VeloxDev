using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;
using Windows.Foundation;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        private bool _resetInitialized;

        public MainWindow()
        {
            InitializeComponent();

            Rec0.RenderTransform = CreateRec0Transform();
            Rec0.Fill = CreateRec0Brush();

            // 动画目标是 DependencyObject，库会直接用它的 DispatcherQueue 编组到所属 UI 线程，
            // 即便下方从后台线程（Task.Run）首次启动动画，也无需任何捕获。

            // VeloxDev动画的核心概念是 "一切皆状态"
            // Snapshot(...) 记录显式指定的属性路径
            // SnapshotAll() 自动发现并记录当前对象中可动画的属性

            // 重置快照在窗口激活（Activated）后才拍摄，避免初始状态尚未确立导致信息丢失。
            // Rec0 的属性（RenderTransform.X、Fill.StartPoint/EndPoint）会被原地修改，
            // 快照持有的引用会被污染，因此 Rec0 用新建对象重置；Rec1/Rec2 的 Fill 是整体替换（不突变），
            // 用初始快照即可完整恢复。
            ((FrameworkElement)Content).Loaded += (s, e) =>
            {
                if (_resetInitialized) return;
                _resetInitialized = true;

                // 先显式初始化到确定状态，再拍快照，避免快照捕获到非初始的 3D/变换（时好时坏）
                Rec0.RenderTransform = CreateRec0Transform();
                Rec0.Fill = CreateRec0Brush();
                Rec0.Projection = null;
                Rec1.RenderTransform = null;
                Rec1.Projection = null;
                Rec2.RenderTransform = null;
                Rec2.Projection = null;

                // Rec1/Rec2 的 Fill 是整体替换（不突变），初始快照稳定可复用
                var reset1 = Rec1.SnapshotAll();
                var reset2 = Rec2.SnapshotAll();

                btnReset.Click += (s, e) =>
                {
                    Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                    // 先显式清除 Projection / RenderTransform，解除 WinUI 的互斥约束（Scale 与 Projection 不能共存），
                    // 否则写 RenderTransform(Scale) 时若元素还挂着 Projection 会抛 UnauthorizedAccessException。
                    Rec0.Projection = null; Rec0.RenderTransform = null;
                    Rec1.Projection = null; Rec1.RenderTransform = null;
                    Rec2.Projection = null; Rec2.RenderTransform = null;

                    // 直接同步应用初始快照：绕过 async Execute 管线（其在部分平台对 Transform 重置不可靠），
                    // 且 Rec0 每次新建对象，避免快照引用被动画原地修改污染
                    ApplyReset(CreateRec0Reset(), Rec0);
                    ApplyReset(reset1, Rec1);
                    ApplyReset(reset2, Rec2);
                };
            };
        }

        private void LoadMainThread(object sender, RoutedEventArgs e)
        {
            // 主线程（UI 线程）直接启动，互斥（CanMutualTask: true 默认）
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadAnimations(object sender, RoutedEventArgs e)
        {
            // 非 UI 线程启动，框架自动切回 UI 线程（测试目标派生的线程编组）
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0);
                Animation1.Execute(Rec1);
                Animation2.Execute(Rec2);
            });
        }

        private void LoadMainThreadNonMutual(object sender, RoutedEventArgs e)
        {
            // 主线程 + CanMutualTask: false —— 并发运行，互不取消
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        }

        private void ExitAnimations(object sender, RoutedEventArgs e)
        {
            // 终结对象持有的动画
            // IncludeMutual   表示是否终结设定了 CanMutualTask: true 的动画
            // IncludeNoMutual 表示是否终结设定了 CanMutualTask: false 的动画

            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

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

        private void LoadAnimationsNonMutual(object sender, RoutedEventArgs e)
        {
            // CanMutualTask: false —— 三段动画互不干扰地并发运行，不会被彼此取消
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void LoadRepeatedMutual(object sender, RoutedEventArgs e)
        {
            // 每次点击都在 Rec0 上启动互斥动画：新动画会取消上一次（测试调度器门控与取消）
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0); // CanMutualTask: true（默认）
            });
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

        // 直接同步应用快照值（绕过 async Execute 管线，确保 Transform/3D 重置确定可靠）。
        // WinUI 约束：Projection 与 RenderTransform(Scale) 互斥——先清除 Projection，再写其余。
        private static void ApplyReset(Transition<Rectangle>.StateSnapshot snapshot, Rectangle target)
        {
            // 两遍：先清除 Projection（解除与 RenderTransform/Scale 的互斥），再写其余。
            // 单个属性冲突（框架约束）不中断整个重置。
            foreach (var kvp in snapshot.GetState().Values)
                if (kvp.Key.Path.Contains("Projection"))
                    try { kvp.Key.SetValue(target, kvp.Value); } catch { }
            foreach (var kvp in snapshot.GetState().Values)
                if (!kvp.Key.Path.Contains("Projection"))
                    try { kvp.Key.SetValue(target, kvp.Value); } catch { }
        }

        // 延迟动画：反转旋转
        private readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(3))
                .Property(r => r.RenderTransform, [new RotateTransform() { Angle = 180 }], RotationDirection.CounterClockWise)
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
                .Property(r => r.Projection,
                    new PlaneProjection()
                    {
                        RotationX = 180,
                        RotationY = 180,
                        CenterOfRotationX = 0.5,
                        CenterOfRotationY = 0.5
                    }, RotationDirection.CounterClockWise)
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
