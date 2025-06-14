using VeloxDev.MAUI.TransitionSystem;

namespace MauiTest
{
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();
        }

        private void OnCounterClicked(object sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);

            LinearGradientBrush lgb = new()
            {
                GradientStops = [new GradientStop(Colors.Lime,0), new GradientStop(Colors.Cyan, 1)]
            };

            Transition.Create(lb)// 等待 5s 后执行第一段动画
                .Property(x => x.Background, lgb)
                .Property(x => x.Opacity, 0.5d)
                .Effect(p=>p.Duration = TimeSpan.FromSeconds(2))
                .Then() // 等待 3s 后执行下一段动画
                .Await(TimeSpan.FromSeconds(3))
                .Property(x => x.Background, Brush.Cyan)
                .Property(x => x.Opacity, 1d)
                .Effect(TransitionEffects.Theme)
                .Start();
        }
    }

}
