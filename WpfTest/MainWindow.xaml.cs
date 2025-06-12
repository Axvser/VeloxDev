using System.Windows;
using System.Windows.Media;
using VeloxDev.WPF.TransitionSystem;

namespace WpfTest
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var state = new State();
            var effect = new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(5),
                LoopTime = 100
            };
            var scheduler = TransitionScheduler.FindOrCreate(this);
            state.SetValue<MainWindow>(window => window.Background, Brushes.Red);
            state.SetValue<MainWindow>(window => window.RenderTransform, Transform.Identity);
            //foreach (var kvp in state.Values)
            //{
            //    MessageBox.Show($"{kvp.Key.Name} | {kvp.Value}");
            //}
            scheduler.Execute(new LinearInterpolator(), state, effect);
        }
    }
}