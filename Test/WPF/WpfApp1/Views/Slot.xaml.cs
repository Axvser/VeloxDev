using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace WpfApp1.Views
{
    public partial class Slot : UserControl
    {
        public Slot()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        public void InitializeWorkflow()
        {
            MouseLeftButtonDown += _03_SlotMouseLeftButtonDown;
            MouseLeftButtonUp += _04_SlotMouseLeftButtonUp;
        }
        private void _03_SlotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectingCommand.Execute(null);
            }
        }
        private void _04_SlotMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectedCommand.Execute(null);
            }
        }
    }
}
