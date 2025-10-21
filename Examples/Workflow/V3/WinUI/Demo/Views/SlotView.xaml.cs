using Microsoft.UI.Xaml.Controls;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class SlotView : UserControl
    {
        public SlotView()
        {
            InitializeComponent();
        }

        private void Border_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DataContext is not IWorkflowSlotViewModel slot) return;
            slot.ApplyConnectionCommand.Execute(null);
        }

        private void Border_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DataContext is not IWorkflowSlotViewModel slot) return;
            slot.ReceiveConnectionCommand.Execute(null);
        }
    }
}
