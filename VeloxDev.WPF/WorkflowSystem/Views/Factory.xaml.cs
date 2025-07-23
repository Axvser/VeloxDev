using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
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
        }
        private void _01_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is UIElement element &&
                DataContext is IWorkflowTree contextTree)
            {
                var point = Mouse.GetPosition(element);
                if (contextTree.VirtualLink.Processor != null)
                {
                    contextTree.VirtualLink.Processor.Anchor.Left = point.X;
                    contextTree.VirtualLink.Processor.Anchor.Top = point.Y;
                }
            }
        }
        private void _02_CreateNode(object sender, MouseEventArgs e)
        {
            if (DataContext is IWorkflowTree contextTree)
            {
                contextTree.CreateNodeCommand.Execute(new ShowerNodeViewModel());
            }
        }
    }
}
