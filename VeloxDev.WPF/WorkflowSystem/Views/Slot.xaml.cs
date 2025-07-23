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
            LayoutUpdated += _01_LayoutUpdated;
            MouseLeftButtonDown += _03_SlotMouseLeftButtonDown;
            MouseLeftButtonUp += _04_SlotMouseLeftButtonUp;
        }
        private void _01_LayoutUpdated(object? sender, EventArgs e)
        {
            if (DataContext is IWorkflowSlot slot)
            {
                var x = Canvas.GetLeft(this);
                var y = Canvas.GetTop(this);
                var z = Canvas.GetZIndex(this);
                slot.Anchor = new Anchor(x, y, z);
            }
        }
        private void _03_SlotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectingCommand.Execute(DataContext);
            }
        }
        private void _04_SlotMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IWorkflowSlot slot)
            {
                slot.ConnectedCommand.Execute(DataContext);
            }
        }
    }
}
