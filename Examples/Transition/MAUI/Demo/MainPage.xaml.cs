using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.MAUI.PlatformAdapters;
using VeloxDev.MAUI.PlatformAdapters.Interpolators;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

            // VeloxDev动画的核心概念是 "一切皆状态"
            // 对象可以调用 Snapshot() 创建快照
            // 其中，若 Snapshot() 不指定目标属性，则视作记录所有可读可写的、可插值的实例属性

            var snapshot0 = Rec0.Snapshot();
            var snapshot1 = Rec0.Snapshot(x => x.TranslationX, x => x.TranslationY);
            var snapshot2 = Rec0.SnapshotExcept(x => x.IsVisible);

            // 于是，可以加载指向 snapshot 的过渡效果
            // 这里记录的快照是初始状态

            btnReset.Clicked += (s, e) =>
            {
                snapshot1.Effect(TransitionEffects.Empty).Execute(Rec0);
            };
        }

        private void LoadAnimations(object sender, EventArgs e)
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

        private void ExitAnimations(object sender, EventArgs e)
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

    public partial class MainPage
    {
        // 简单动画 - 平移
        private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, new LinearGradientBrush()
                {
                    GradientStops = 
                    [ 
                        new GradientStop(Colors.Cyan,0),
                        new GradientStop(Colors.Yellow,1)
                    ]
                })
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 延迟动画 - 3D旋转
        private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(2))
                .Property(r => r.RotationX, 180)     // MAUI X旋转
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 拼接动画 - 组合变换
        private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
                // 第一段：平移 + 缩放
                .Property(r => r.RotationX, 180)
                .Property(r => r.RotationY, 180)
                .Property(r => r.TranslationX, 200)
                .Property(r => r.TranslationY, 0)
                .Property(r => r.Scale, 1.3)         // MAUI 的整体缩放
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    FPS = 144,
                    Ease = Eases.Circ.InOut,
                    LoopTime = 2,
                })
                .AwaitThen(TimeSpan.FromSeconds(5))
                // 第二段：颜色变化
                .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Sine.In
                });
    }
}
