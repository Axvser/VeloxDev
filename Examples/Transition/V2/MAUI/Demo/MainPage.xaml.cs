using Microsoft.Maui.Controls.Shapes;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.MAUI.PlatformAdapters;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
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

    public partial class MainPage
    {
        // 简单动画 - 平移
        private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, 800)  // MAUI 的平移X
                .Property(r => r.TranslationY, 0)   // MAUI 的平移Y
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 延迟动画 - 3D旋转
        private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(5))
                .Property(r => r.Rotation, 180)     // MAUI 旋转
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
