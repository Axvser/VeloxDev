using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.WPF.WorkflowSystem.Views
{
    public partial class Shower : UserControl
    {
        public Shower()
        {
            InitializeComponent();
            MouseDown += (s, e) =>
            {
                ConnectCommand.Execute(ConnectCommandParameter);
            };

        }

        public IVeloxCommand ConnectCommand
        {
            get { return (IVeloxCommand)GetValue(ConnectCommandProperty); }
            set { SetValue(ConnectCommandProperty, value); }
        }
        public static readonly DependencyProperty ConnectCommandProperty =
            DependencyProperty.Register("ConnectCommand", typeof(IVeloxCommand), typeof(Shower));

        public object ConnectCommandParameter
        {
            get { return GetValue(ConnecttCommandParameterProperty); }
            set { SetValue(ConnecttCommandParameterProperty, value); }
        }
        public static readonly DependencyProperty ConnecttCommandParameterProperty =
            DependencyProperty.Register("ConnectCommandParameter", typeof(object), typeof(Shower));

        public IVeloxCommand MoveCommand
        {
            get { return (IVeloxCommand)GetValue(MoveCommandProperty); }
            set { SetValue(MoveCommandProperty, value); }
        }
        public static readonly DependencyProperty MoveCommandProperty =
            DependencyProperty.Register("MoveCommand", typeof(IVeloxCommand), typeof(Shower));

        public object MovetCommandParameter
        {
            get { return GetValue(MovetCommandParameterProperty); }
            set { SetValue(MovetCommandParameterProperty, value); }
        }
        public static readonly DependencyProperty MovetCommandParameterProperty =
            DependencyProperty.Register("MovetCommandParameter", typeof(object), typeof(Shower));

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

        private void UserControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IContext context)
            {
                var newAnchor = new Anchor(context.Anchor.Left + 20, context.Anchor.Top + 10, 0);
                context.Anchor = newAnchor;
            }
        }
    }
}
