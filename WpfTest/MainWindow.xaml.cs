using System.Windows;
using VeloxDev.WPF.SourceGeneratorMark;

namespace WpfTest
{
    [MonoBehaviour(16)]
    public partial class MainWindow : Window
    {
        partial void Update()
        {
            MessageBox.Show("开始吧");
        }
    }
}