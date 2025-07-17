using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VeloxDev.WPF.WorkflowSystem
{
    public partial class WorkflowNode : Thumb
    {
        public WorkflowNode()
        {
            InitializeComponent();
            InitializeVeloxDev();
        }

        public void InitializeVeloxDev()//IWorkflowContext dataContext
        {
            MouseDown += _0002_MouseDown;
            DragDelta += _0002_WorkflowDragDelta;
        }
        partial void OnDragDelta(object sender, DragDeltaEventArgs e);

        private void _0002_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void _0002_WorkflowDragDelta(object sender, DragDeltaEventArgs e)
        {
            var position = new Point(
                Canvas.GetLeft(this) + e.HorizontalChange,
                Canvas.GetTop(this) + e.VerticalChange);
            Canvas.SetLeft(this, position.X);
            Canvas.SetTop(this, position.Y);
            OnDragDelta(sender, e);
        }
    }
}
