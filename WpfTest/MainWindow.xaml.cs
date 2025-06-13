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
            var state = new State();
            var effect = new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1),
                LoopTime = 0,
                IsAutoReverse = false,
            };
            var scheduler = TransitionScheduler.FindOrCreate(this);
            state.SetValue<MainWindow, Brush>(window => window.Background, Brushes.Cyan);
            var li = new LinearInterpolator();

            scheduler.Execute(li, state, effect);
        }
    }
}