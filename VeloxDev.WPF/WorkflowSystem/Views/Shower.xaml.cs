using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VeloxDev.Core.Interfaces.WorkflowSystem.View;
using VeloxDev.Core.Interfaces.WorkflowSystem.ViewModel;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Shower : UserControl, IViewNode
    {
        public Shower()
        {
            InitializeComponent();
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
                    Anchor.Default,
                    _1_OnAnchorChanged));
        private static void _1_OnAnchorChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is Shower view)
            {
                var oldAnchor = (Anchor)e.OldValue;
                var newAnchor = (Anchor)e.NewValue;
                Canvas.SetLeft(view, newAnchor.Left);
                Canvas.SetTop(view, newAnchor.Top);
                view.OnAnchorChanged(oldAnchor, newAnchor);
            }
        }
        partial void OnAnchorChanged(Anchor oldValue, Anchor newValue);

        public void InitializeWorkflow(IContext context)
        {
            Binding bindingAnchor = new(nameof(Anchor))
            {
                Source = DataContextProperty,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            Binding bindingEnabled = new(nameof(IsEnabled))
            {
                Source = DataContextProperty,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            BindingOperations.SetBinding(this, IsEnabledProperty, bindingEnabled); // UIElement.IsEnabled
            BindingOperations.SetBinding(this, AnchorProperty, bindingAnchor); // VeloxDev
        }
    }
}
