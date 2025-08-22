using VeloxDev.Core.DynamicTheme;
using VeloxDev.Core.TransitionSystem;
using VeloxDev.MAUI.PlatformAdapters;

namespace MauiApp1
{
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
    public partial class MainPage : ContentPage
    {
        int count = 0;

        public MainPage()
        {
            InitializeComponent();
            InitializeTheme();
            ThemeManager.SetPlatformInterpolator(new Interpolator());
        }

        private void OnCounterClicked(object sender, EventArgs e)
        {
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";

            SemanticScreenReader.Announce(CounterBtn.Text);

            if (ThemeManager.Current == typeof(Dark))
            {
                ThemeManager.Transition<Light>(new TransitionEffect()
                {
                    FPS = 120,
                    Duration = TimeSpan.FromSeconds(3),
                    EaseCalculator = Eases.Circ.InOut
                });
            }
            else
            {
                ThemeManager.Transition<Dark>(new TransitionEffect()
                {
                    FPS = 120,
                    Duration = TimeSpan.FromSeconds(3),
                    EaseCalculator = Eases.Circ.InOut
                });
            }
        }
    }

}
