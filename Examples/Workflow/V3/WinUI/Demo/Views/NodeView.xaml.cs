using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using Windows.UI.Notifications;

namespace Demo.Views
{
    public sealed partial class NodeView : UserControl
    {
        public NodeView()
        {
            InitializeComponent();
        }

        private void Border_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (DataContext is not IWorkflowNodeViewModel node) return;

            var deltaX = e.Delta.Translation.X;
            var deltaY = e.Delta.Translation.Y;

            // ����ƫ��������ִ���ƶ�����
            var offset = new Offset(deltaX, deltaY);

            node.MoveCommand?.Execute(offset);
        }
    }
}
