using System.Windows.Controls;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Factory : Canvas
    {
        public Factory()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        private void InitializeWorkflow()
        {
            MouseMove += _01_MouseMove;
            //MouseRightButtonDown += _02_CreateNode;
        }
        private void _01_MouseMove(object sender, global::System.Windows.Input.MouseEventArgs e)
        {
            if (sender is global::System.Windows.UIElement element &&
                DataContext is global::VeloxDev.Core.Interfaces.WorkflowSystem.IContextTree contextTree)
            {
                var point = global::System.Windows.Input.Mouse.GetPosition(element);
                if (contextTree.VirtualConnector.End != null)
                {
                    contextTree.VirtualConnector.End.Anchor = new(point.X, point.Y, 0);
                }
            }
        }
        private void _02_CreateNode(object sender, global::System.Windows.Input.MouseEventArgs e)
        {
            if (DataContext is IContextTree contextTree)
            {
                contextTree.CreateNodeCommand.Execute(this);
            }
        }
    }
}
