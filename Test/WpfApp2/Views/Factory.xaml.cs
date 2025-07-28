using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using WpfApp2.ViewModels;

namespace WpfApp2.Views
{
    public partial class Factory : Canvas
    {
        public Factory()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        public void InitializeWorkflow()
        {
            MouseMove += _01_MouseMove;
            MouseRightButtonDown += _02_CreateNode;
            MouseLeftButtonUp += _03_ClearVirtualLink;
        }

        private void _03_ClearVirtualLink(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IWorkflowTree tree)
            {
                tree.VirtualLink.Sender = null;
            }
        }

        private void _01_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is IWorkflowTree contextTree)
            {
                var point = Mouse.GetPosition(this);
                contextTree.SetMouseCommand.Execute(new VeloxDev.Core.WorkflowSystem.Anchor(point.X, point.Y));
            }
        }
        private static int counter = 3;
        private void _02_CreateNode(object sender, MouseEventArgs e)
        {
            if (DataContext is IWorkflowTree contextTree)
            {
                var position = Mouse.GetPosition(this);
                contextTree.CreateNodeCommand.Execute(
                    new ShowerNodeViewModel()
                    {
                        Size = new(200, 200),
                        Anchor = new(position.X, position.Y),
                        Name = $"节点{counter++}"
                    });
            }
        }
    }
}
