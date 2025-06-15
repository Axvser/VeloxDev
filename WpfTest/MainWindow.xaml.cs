using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.WPF.HotKey;
using VeloxDev.WPF.TransitionSystem;

namespace WpfTest
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => GlobalHotKey.Awake();
            Closing += (s, e) => GlobalHotKey.Dispose();
            GlobalHotKey.Register(VirtualModifiers.Ctrl, VirtualKeys.F1, (s, e) =>
            {
                MessageBox.Show("快捷方式触发");
            });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var transition = Transition.Create(this)
                .Await(TimeSpan.FromSeconds(3))// (可选) 等待 3s 后执行第一段动画
                .Property(x => x.Background, Brushes.Red)
                .Property(x => x.Opacity, 0.5d)
                .Effect(TransitionEffects.Theme) // 效果参数
                .Then() // 执行下一段动画 > (可选) AwaitThen()以延迟启动下一段动画
                .Property(x => x.Background, Brushes.Cyan)
                .Property(x => x.Opacity, 1d)
                .Effect((p) =>
                {
                    p.Duration = TimeSpan.FromSeconds(1);
                    p.EaseCalculator = Eases.Sine.InOut;
                    p.Awaked += (s, e) =>
                    {

                    };
                    p.Update += (s, e) =>
                    {

                    };
                }); // 使用自定义的效果参数
            transition.Start();
        }
    }
}