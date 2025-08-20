using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace WpfApp2.Views
{
    public partial class Shower : Canvas
    {
        public Shower()
        {
            InitializeComponent();
            InitializeWorkflow();
        }

        public bool IsWorking
        {
            get { return (bool)GetValue(IsWorkingProperty); }
            set { SetValue(IsWorkingProperty, value); }
        }
        public static readonly DependencyProperty IsWorkingProperty =
            DependencyProperty.Register("IsWorking", typeof(bool), typeof(Shower), new PropertyMetadata(false, (s, e) =>
            {
                if (s is Shower shower)
                {
                    shower.card.CanMonoBehaviour = (bool)e.NewValue;
                }
            }));

        public void InitializeWorkflow()
        {
            MouseRightButtonDown += _05_Node_AddSlot;
            MouseMove += _01_Node_MouseMove;
            MouseLeftButtonDown += _02_Node_MouseDown;
            MouseLeftButtonUp += _03_Node_MouseUp;
            MouseLeave += _04_Node_MouseLeave;
            MouseWheel += Shower_MouseWheel;
        }

        private void Shower_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DataContext is IWorkflowNode node)
            {
                node.BroadcastCommand.Execute(null);
            }
        }

        private void _05_Node_AddSlot(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is IWorkflowNode node)
            {
                var point = Mouse.GetPosition(this);
                var slot = new ViewModels.SlotContext() { Offset = new(point.X, point.Y), Size = new(10, 10) };
                node.CreateSlotCommand.Execute(slot);
                e.Handled = true;
            }
        }

        private Point _dragStartPosition = default;
        private bool _isDragging = false;
        private void _04_Node_MouseLeave(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void _03_Node_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void _02_Node_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPosition = Mouse.GetPosition(this);
            _isDragging = true;
        }

        private void _01_Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && DataContext is IWorkflowNode node)
            {
                var current = e.GetPosition(this);
                var offset = current - _dragStartPosition;
                node.Anchor += new Anchor(offset.X, offset.Y);
                e.Handled = true;
            }
        }
    }
}
