using System.Windows;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.ViewModels;
using WpfApp1.ViewModels;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        FactoryViewModel fc;
        public MainWindow()
        {
            InitializeComponent();
            var node1 = new ShowerNodeViewModel() { Anchor = new(250, 100, 2), Size = new(100, 200) };
            var node2 = new ShowerNodeViewModel() { Anchor = new(100, 50, 1), Size = new(100, 200) };
            var slot1 = new SlotContext() { Offset = new(10, 100), Size = new(30, 30), Capacity = VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Sender };
            var slot2 = new SlotContext() { Offset = new(70, 100), Size = new(30, 30), Capacity = VeloxDev.Core.Interfaces.WorkflowSystem.SlotCapacity.Processor };
            node1.Slots.Add(slot1);
            node2.Slots.Add(slot2);
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