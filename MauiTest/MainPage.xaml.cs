using VeloxDev.Core.TransitionSystem;
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

            var transition = Transition.Create(this)
                .Await(TimeSpan.FromSeconds(3))// (可选) 等待 3s 后执行第一段动画
                .Property(x => x.Background, Brush.Red)
                .Property(x => x.Opacity, 0.5d)
                .Effect(TransitionEffects.Theme) // 效果参数
                .Then() // 执行下一段动画 > (可选) AwaitThen()以延迟启动下一段动画
                .Property(x => x.Background, Brush.Cyan)
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
