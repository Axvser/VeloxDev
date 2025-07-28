using System.Windows;
using WpfApp2.ViewModels;

namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        FactoryViewModel fc;
        public MainWindow()
        {
            InitializeComponent();
            var node1 = new ShowerNodeViewModel() { Anchor = new(100, 100, 2), Size = new(200, 200), Name = "节点1" };
            var node2 = new ShowerNodeViewModel() { Anchor = new(400, 100, 1), Size = new(200, 200), Name = "节点2" };
            var tree = new FactoryViewModel()
            {
                Nodes = [node1, node2]
            };
            fc = tree;
            container.DataContext = tree;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            fc.UndoCommand.Execute(null);
        }
    }
}