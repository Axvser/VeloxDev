using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.ViewModels;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var node1 = new ShowerNodeViewModel() { Anchor = new(250, 100, 2), Size = new(100, 200) };
            var node2 = new ShowerNodeViewModel() { Anchor = new(100, 50, 1), Size = new(100, 200) };
            var slot1 = new Slot() { Offset = new(10, 100), Size = new(30, 30) };
            var slot2 = new Slot() { Offset = new(70, 100), Size = new(30, 30) };
            node1.Slots.Add(slot1);
            node2.Slots.Add(slot2);
            var tree = new FactoryViewModel()
            {
                Nodes = [node1, node2]
            };
            container.DataContext = tree;
        }
    }
}