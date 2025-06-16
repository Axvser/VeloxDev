using System.Windows;
using System.Windows.Media;
using VeloxDev.Core.AspectOriented;
using VeloxDev.Core.Generators;
using VeloxDev.Core.TransitionSystem;

namespace WpfTest
{
    public class ObservableAttribute : Attribute
    {

    }

    [MonoBehaviour(60)]
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Proxy.SetProxy(ProxyMembers.Setter, nameof(UID), // Setter 的 AOP
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

            Proxy.SetProxy(ProxyMembers.Getter, nameof(Id), // Getter 的 AOP
                null,
                null,
                null);

            Proxy.SetProxy(ProxyMembers.Method,nameof(Do), // Method 的 AOP
                null,
                null,
                (calls, result) =>
                {
                    MessageBox.Show($"Do方法已执行过"); // 编写日志
                    return null;
                });
        }

        [AspectOriented]
        public string UID { get; set; } = "default";

        [AspectOriented]
        public void Do()
        {
            Proxy.UID = "newValue"; // 通过代理访问 UID 属性的 Setter
        }

        [Observable] // 若特性包含 Observable 字样，则 AOP 接口会要求你实现其公开可读写属性 ID
        [AspectOriented]
        private int id = 3;

        public int Id
        {
            get => id;
            set => id = value;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Proxy.Do(); // 通过代理调用 Do 方法
        }
    }
}