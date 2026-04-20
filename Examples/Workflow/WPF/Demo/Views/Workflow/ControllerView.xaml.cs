using Demo.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow
{
    public partial class ControllerView : UserControl
    {
        private bool _isDragging;
        private Point _lastPosition;
        private Canvas? _parentCanvas;
        private INotifyPropertyChanged? _propertyChangedSource;

        public ControllerView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            SizeChanged += (_, _) => SyncOutputSlot();
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            // 获取父级Canvas
            _parentCanvas = FindVisualParent<Canvas>(this);
        }

        // 查找视觉树父级
        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_parentCanvas == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            _isDragging = true;
            _lastPosition = e.GetPosition(_parentCanvas);
            Mouse.Capture(sender as IInputElement ?? this);
            e.Handled = true;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
            SyncOutputSlot();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _parentCanvas == null) return;

            var currentPosition = e.GetPosition(_parentCanvas);
            var delta = currentPosition - _lastPosition;

            if (DataContext is ControllerViewModel nodeContext)
            {
                nodeContext.MoveCommand.Execute(new Offset(delta.X, delta.Y));
            }

            _lastPosition = currentPosition;
            e.Handled = true;
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDragging = false;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_propertyChangedSource is not null)
            {
                _propertyChangedSource.PropertyChanged -= OnControllerPropertyChanged;
                _propertyChangedSource = null;
            }

            if (e.NewValue is INotifyPropertyChanged notify)
            {
                _propertyChangedSource = notify;
                notify.PropertyChanged += OnControllerPropertyChanged;
            }

            SyncOutputSlot();
        }

        private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ControllerViewModel.Anchor) or nameof(ControllerViewModel.Size) or nameof(ControllerViewModel.OutputSlot))
            {
                Dispatcher.InvokeAsync(SyncOutputSlot, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void SyncOutputSlot()
        {
            if (DataContext is not ControllerViewModel controller)
            {
                return;
            }

            if (controller.OutputSlot is null)
            {
                return;
            }

            controller.OutputSlot.Anchor = new Anchor(
                controller.Anchor.Horizontal + controller.Size.Width,
                controller.Anchor.Vertical + (controller.Size.Height / 2),
                controller.OutputSlot.Anchor.Layer);
        }
    }
}
