using System.Windows;
using System.Windows.Media;
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
            Transition.Create(this)// 等待 5s 后执行第一段动画
                .Property(x => x.Background, Brushes.Red)
                .Property(x => x.Opacity, 0.5d)
                .Effect(TransitionEffects.Theme)
                .Then() // 等待 3s 后执行下一段动画
                .Property(x => x.Background, Brushes.Cyan)
                .Property(x => x.Opacity, 1d)
                .Effect(TransitionEffects.Theme)
                .Start();
        }
    }
}