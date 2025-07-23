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
            LayoutUpdated += _01_LayoutUpdated;
            Loaded += _02_FindContextParent;
            MouseLeftButtonDown += _03_SlotMouseLeftButtonDown;
        }
        private void _01_LayoutUpdated(object? sender, EventArgs e)
        {
            MessageBox.Show("布局刷新");
            if (DataContext is IWorkflowSlot slot &&
                slot.Parent is IWorkflowNode node &&
                Parent is UIElement element)
            {
                var offset = TransformToVisual(element)
                    .Transform(new Point(
                        double.IsNaN(ActualWidth) ? 0 : ActualWidth / 2,
                        double.IsNaN(ActualHeight) ? 0 : ActualHeight / 2));
                slot.Anchor = node.Anchor + new Anchor(offset.X, offset.Y);
            }
        }
        private void _02_FindContextParent(object sender, RoutedEventArgs e)
        {
            if (DataContext is IWorkflowSlot slot &&
               slot.Parent is FrameworkElement element &&
               element.DataContext is IWorkflowNode node)
            {
                slot.Parent = node;
            }
        }
        private void _03_SlotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show("准备");
            if (TemplatedParent is Panel gird &&
                gird.TemplatedParent is FrameworkElement node &&
                node.Parent is Panel canvas &&
                canvas.DataContext is IWorkflowTree tree)
            {
                MessageBox.Show("开始连线");
                tree.SetVirtualSenderCommand.Execute(DataContext);
            }
        }
    }
}
