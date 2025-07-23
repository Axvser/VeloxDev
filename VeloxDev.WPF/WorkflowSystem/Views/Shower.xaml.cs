using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Shower : Thumb, IWorkflowView
    {
        public Shower()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        public void InitializeWorkflow()
        {
            DragDelta += Thumb_DragDelta;
        }

        public Anchor Anchor
        {
            get { return (Anchor)GetValue(AnchorProperty); }
            set { SetValue(AnchorProperty, value); }
        }
        public static readonly DependencyProperty AnchorProperty =
            DependencyProperty.Register(
                "Anchor",
                typeof(Anchor),
                typeof(Shower),
                new PropertyMetadata(
                    new Anchor(),
                    _1_OnAnchorChanged));
        private static void _1_OnAnchorChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is Shower view)
            {
                var newAnchor = (Anchor)e.NewValue;
                Canvas.SetLeft(view, newAnchor.Left);
                Canvas.SetTop(view, newAnchor.Top);
            }
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is IWorkflowNode context)
            {
                context.Anchor = new Anchor(context.Anchor.Left + e.HorizontalChange, context.Anchor.Top + e.VerticalChange, context.Anchor.Layer);
            }
        }
    }
}
