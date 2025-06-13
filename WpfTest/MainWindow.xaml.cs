using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDev.Core.Interfaces.TransitionSystem;
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
                Duration = TimeSpan.FromSeconds(1),
                LoopTime = 0,
                IsAutoReverse = false,
            };
            var scheduler = TransitionScheduler.FindOrCreate(this);
            state.SetValue<MainWindow, Brush>(window => window.Background, Brushes.Red);
            state.SetValue<MainWindow, Transform>(window => window.RenderTransform, Transform.Identity);
            state.SetValue<MainWindow, double>(window => window.Opacity, 0.4d);
            var li = new LinearInterpolator();

            scheduler.Execute(li, state, effect);
        }
    }
}