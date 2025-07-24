using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Slot : UserControl, IWorkflowView
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
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectingCommand.Execute(DataContext);
                e.Handled = true;
            }
        }
        private void _04_SlotMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectedCommand.Execute(DataContext);
                e.Handled = true;
            }
        }
    }
}
