using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
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
