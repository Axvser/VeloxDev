using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.Generators;
using VeloxDev.Core.TransitionSystem;

namespace WpfTest
{
    [MonoBehaviour(60)]
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Proxy.SetProxy(ProxyMembers.Setter, nameof(UID),
                null,
                (calls, result) =>
                {
                    var oldValue = UID;
                    var newValue = calls[0].ToString(); // 对于 Setter器，必定有一个参数 value
                    UID = newValue;
                    return Tuple.Create(oldValue, newValue); // 返回新值与旧值用于日志记录
                },
                (calls, result) =>
                {
                    var value = result as Tuple<string, string?>; // 接收上一个节点的返回值
                    MessageBox.Show($"值已更新 {value.Item1} → {value.Item2}"); // 编写日志
                    return null;
                });
            Do();
        }

        [AspectOriented]
        public string UID { get; set; } = "default";

        public void Do()
        {
            Proxy.UID = "newValue"; // 通过代理访问 UID 属性的 Setter
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //var transition = Transition.Create(this)
            //    .Await(TimeSpan.FromSeconds(3))// (可选) 等待 3s 后执行第一段动画
            //    .Property(x => x.Background, Brushes.Red)
            //    .Property(x => x.Opacity, 0.5d)
            //    .Effect(TransitionEffects.Theme) // 效果参数
            //    .Then() // 执行下一段动画 > (可选) AwaitThen()以延迟启动下一段动画
            //    .Property(x => x.Background, Brushes.Cyan)
            //    .Property(x => x.Opacity, 1d)
            //    .Effect((p) =>
            //    {
            //        p.Duration = TimeSpan.FromSeconds(1);
            //        p.EaseCalculator = Eases.Sine.InOut;
            //        p.Awaked += (s, e) =>
            //        {

            //        };
            //        p.Update += (s, e) =>
            //        {

            //        };
            //    }); // 使用自定义的效果参数
            //transition.Start();
        }
    }
}