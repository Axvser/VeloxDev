using VeloxDev.Core.Generators;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.AopInterfaces;

namespace MauiTest
{
    [MonoBehaviour(60)]
    public partial class MainPage : ContentPage, MainPage_MauiTest_Aop // 此处必须显示实现动态生成的AOP接口
    {
        [AspectOriented]
        public void Do()
        {

        }

        public MainPage()
        {
            InitializeComponent();

            var proxy = this.CreateProxy<MainPage_MauiTest_Aop>(); // 此处必须显示指定AOP接口

            proxy.SetProxy(ProxyMembers.Method, nameof(Do), null, null, (s, e) =>
            {
                Background = Brush.Violet;
                return null;
            });

            proxy.Do(); // 通过代理调用 Do 方法
        }

        int count = 0;

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
                GradientStops = [new GradientStop(Colors.Lime, 0), new GradientStop(Colors.Cyan, 1)]
            };
        }
    }

}
