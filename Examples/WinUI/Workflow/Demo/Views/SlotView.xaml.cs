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
                slot.ConnectingCommand.Execute(null); // 此元素作为连接发起者       | param : null
                slot.ConnectedCommand.Execute(null);  // 此元素作为被连接对象       | param : null
                slot.DeleteCommand.Execute(null);     // 删除此插槽                 | param : null
                slot.UndoCommand.Execute(null);       // 撤销                       | param : null
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
