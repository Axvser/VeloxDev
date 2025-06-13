using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.WPF.TransitionSystem;

namespace WpfTest
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var state = new State();
            var effect = new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(50),
                LoopTime = 1000
            };
            effect.Start += (s, e) =>
            {
                MessageBox.Show("开始");
            };
            effect.Update += (s, e) =>
            {
                MessageBox.Show("一帧");
            };
            effect.Finally += (s, e) =>
            {
                MessageBox.Show("动画已结束");
            };
            var scheduler = TransitionScheduler.FindOrCreate(this);
            state.SetValue<MainWindow, Brush>(window => window.Background, Brushes.Red);
            state.SetValue<MainWindow, Transform>(window => window.RenderTransform, Transform.Identity);
            state.SetValue<MainWindow, double>(window => window.Opacity, 0.4d);
            var li = new LinearInterpolator();
            scheduler.Execute(li, state, effect);
            //var sequnce = li.Interpolate(this, state, effect);
            //sequnce.Update(this, 59, true, DispatcherPriority.Render);
        }
    }
}