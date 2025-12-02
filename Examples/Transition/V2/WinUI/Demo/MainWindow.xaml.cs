using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WinUI.PlatformAdapters;
using WinRT.Interop;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var snapshot0 = Rec0.Snapshot();     // 记录所有可读写实例属性

            var snapshot1 = Rec0.Snapshot(       // 仅记录指定的实例属性
                x => x.Width, 
                x => x.Height);

            var snapshot2 = Rec0.SnapshotExcept( // 记录除Visibility外所有可读写的实例属性
                x => x.Visibility);

            // VeloxDev库在WinUI执行动画时，必须手动获取主窗口
            UIThreadInspector.SetWindow(this);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);

            // 可以直接在其它线程中启动动画，框架会自动切换到 UI 线程

            //_ = Task.Run(() =>
            //{
            //    Animation0.Execute(Rec0);
            //    Animation1.Execute(Rec1);
            //    Animation2.Execute(Rec2);
            //});
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Transition.Exit(Rec0);
            Transition.Exit(Rec1);
            Transition.Exit(Rec2);
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
                    Duration = TimeSpan.FromSeconds(1),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 延迟动画
        private readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(3))
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
