using Demo.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Demo.Views
{
    public sealed partial class SlotView : UserControl
    {
        public SlotView()
        {
            InitializeComponent();
        }

        public void SlotCommands()
        {
            if (DataContext is SlotViewModel slot)
            {
                slot.ConnectingCommand.Execute(null); // ��Ԫ����Ϊ���ӷ�����       | param : null
                slot.ConnectedCommand.Execute(null);  // ��Ԫ����Ϊ�����Ӷ���       | param : null
                slot.DeleteCommand.Execute(null);     // ɾ���˲��                 | param : null
                slot.UndoCommand.Execute(null);       // ����                       | param : null
            }
        }

        private void Grid_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DataContext is SlotViewModel slot)
            {
                slot.ConnectingCommand.Execute(null);
            }
        }

        private void Grid_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DataContext is SlotViewModel slot)
            {
                slot.ConnectedCommand.Execute(null);
            }
        }
    }
}
