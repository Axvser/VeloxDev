using Demo.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class TreeView : UserControl
    {
        private TreeViewModel ViewModel = new();

        public TreeView()
        {
            InitializeComponent();
            SimulateData();
        }

        private void SimulateData()
        {
            // ��ģ��һЩ����
            var slot1 = new SlotViewModel()
            {
                Offset = new Offset(170, 60),
                Size = new Size(20, 20),
                Channel = SlotChannel.OneTarget,
            };
            var slot2 = new SlotViewModel()
            {
                Offset = new Offset(170, 120),
                Size = new Size(20, 20),
                Channel = SlotChannel.MultipleTargets,
            };
            var slot3 = new SlotViewModel()
            {
                Offset = new Offset(10, 100),
                Size = new Size(20, 20),
                Channel = SlotChannel.OneSource,
            };
            var slot4 = new SlotViewModel()
            {
                Offset = new Offset(10, 200),
                Size = new Size(20, 20),
                Channel = SlotChannel.MultipleSources,
            };
            var node1 = new NodeViewModel()
            {
                Size = new Size(200, 200),
                Anchor = new Anchor(50, 50, 1)
            };
            var node2 = new NodeViewModel()
            {
                Size = new Size(300, 300),
                Anchor = new Anchor(250, 250, 1)
            };

            // �� Tree ���� Node
            ViewModel.GetHelper().CreateNode(node1);
            ViewModel.GetHelper().CreateNode(node2);

            // �� Node ���� Slot
            node1.GetHelper().CreateSlot(slot1);
            node1.GetHelper().CreateSlot(slot2);
            node2.GetHelper().CreateSlot(slot3);
            node2.GetHelper().CreateSlot(slot4);

            // ���������ʷ����ֹ����ĳ���������
            ViewModel.GetHelper().ClearHistory();

            // ʹ������������
            DataContext = ViewModel;
        }

        private void Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.UndoCommand.Execute(null);
        }

        private void Button_Click_1(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel?.RedoCommand.Execute(null);
        }

        private void canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ViewModel?.ResetVirtualLinkCommand.Execute(null);
        }

        private void canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint(canvas).Position;
            var anchor = new Anchor(position.X, position.Y);
            ViewModel?.SetPointerCommand.Execute(anchor);
        }
    }
}
