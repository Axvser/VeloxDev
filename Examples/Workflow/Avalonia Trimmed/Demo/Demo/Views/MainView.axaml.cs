using Avalonia.Controls;
using Demo.ViewModels.Workflow;
using Demo.ViewModels.Workflow.Enums;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            var tree = new TreeViewModel();
            LoadTree(tree);
            DataContext = tree;
        }

        private void LoadTree(TreeViewModel tree)
        {
            var size = new Size(300, 200);

            var node1 = new NodeViewModel { Name = "Node 1", Size = size };
            var node2 = new NodeViewModel { Name = "Node 2", Size = size };
            var node3 = new NodeViewModel { Name = "Node 3", Size = size };

            tree.CreateNodeCommand.Execute(node1);
            tree.CreateNodeCommand.Execute(node2);
            tree.CreateNodeCommand.Execute(node3);

            node1.OutputSlots.SetSelector(typeof(bool));
            node2.OutputSlots.SetSelector(typeof(VoltageRange));
            node3.OutputSlots.SetSelector(typeof(ModelProtocol));

            node1.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
            node2.InputSlot.SetChannelCommand.Execute(SlotChannel.MultipleSources);
            node3.InputSlot.SetChannelCommand.Execute(SlotChannel.OneSource);
        }
    }
}