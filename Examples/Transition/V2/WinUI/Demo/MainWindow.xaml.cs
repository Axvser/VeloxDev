using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinUI.PlatformAdapters;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // VeloxDev库执行动画时依赖一个 UIThread 检查器来确保插值操作在 UI 线程中执行
            // 这里将 WinUI 的 DispatcherQueue 适配器注册到 UIThreadInspector 中
            UIThreadInspector.DispatcherQueue = DispatcherQueue;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
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

            // 安全地退出插值动画

            // TransitionCore.Exit(Rec0);
            // Transition<Rectangle>.Exit(Rec0);
        }
    }

    public sealed partial class MainWindow
    {
        // ⚠ 在WinUI中创建 Transition<> 需要特别注意 :
        //
        // static 的 Transition<> 字段不可在非 UIThread 中使用，否则可能抛出 TypeInitialization 异常

        // 简单动画
        private readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.RenderTransform, [new TranslateTransform() { X = 800, Y = 0 }])
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 延迟动画
        private readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(5))
                .Property(r => r.RenderTransform, [new RotateTransform() { Angle = 180 }])
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 拼接动画
        private readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
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
