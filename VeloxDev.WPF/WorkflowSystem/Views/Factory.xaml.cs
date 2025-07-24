using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.WPF.WorkflowSystem.ViewModels;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Factory : Canvas, IWorkflowView
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
            if (sender is UIElement element &&
                DataContext is IWorkflowTree contextTree)
            {
                var point = Mouse.GetPosition(element);
                if (contextTree.VirtualLink.Processor != null)
                {
                    contextTree.SetMouseCommand.Execute(new Anchor(point.X, point.Y));
                }
            }
        }
        private void _02_CreateNode(object sender, MouseEventArgs e)
        {
            if (DataContext is IWorkflowTree contextTree)
            {
                var position = Mouse.GetPosition(this);
                contextTree.CreateNodeCommand.Execute(
                    new ShowerNodeViewModel()
                    {
                        Size = new(100, 100),
                        Anchor = new(position.X, position.Y)
                    });
            }
        }
    }
}
