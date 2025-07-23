using System.Windows;
using System.Windows.Controls;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
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
                parent.DataContext is IWorkflowNode context)
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
