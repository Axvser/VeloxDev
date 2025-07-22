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

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    /// <summary>
    /// Slot.xaml 的交互逻辑
    /// </summary>
    public partial class Slot : UserControl
    {
        public Slot()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        public int UID
        {
            get { return (int)GetValue(UIDProperty); }
            set { SetValue(UIDProperty, value); }
        }
        public static readonly DependencyProperty UIDProperty =
            DependencyProperty.Register(
                "UID",
                typeof(int),
                typeof(Slot),
                new PropertyMetadata(0, _001_OnUIDChanged));
        private static void _001_OnUIDChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Slot slot)
            {
                slot._01_LoadDependency(slot, new RoutedEventArgs());
            }
        }

        private void InitializeWorkflow()
        {
            Loaded += _01_LoadDependency;
            OnWorkflowInitialized();
        }
        partial void OnWorkflowInitialized();
        private void _01_LoadDependency(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element &&
                element.Parent is FrameworkElement parent &&
                parent.DataContext is IContext context)
            {
                if (context.Slots.TryGetValue(UID, out var slotContext) &&
                    slotContext is not null)
                {
                    DataContext = slotContext;
                }
                else
                {

                }
            }
        }
    }
}
