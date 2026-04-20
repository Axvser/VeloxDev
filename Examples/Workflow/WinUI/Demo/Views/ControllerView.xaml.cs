using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class ControllerView : UserControl
    {
        private bool _isDragging;
        private Windows.Foundation.Point _lastPosition;
        private Canvas? _parentCanvas;
        private INotifyPropertyChanged? _propertyChangedSource;

        public ControllerView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                _parentCanvas = FindVisualParent<Canvas>(this);
                SyncOutputSlot();
            };
            DataContextChanged += OnDataContextChanged;
            SizeChanged += (_, _) => SyncOutputSlot();
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_parentCanvas == null)
            {
                return;
            }

            var point = e.GetCurrentPoint(_parentCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isDragging = true;
            _lastPosition = point.Position;
            if (sender is UIElement element)
            {
                element.CapturePointer(e.Pointer);
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            if (sender is UIElement element)
            {
                element.ReleasePointerCapture(e.Pointer);
            }

            SyncOutputSlot();
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || _parentCanvas == null || DataContext is not ControllerViewModel controller)
            {
                return;
            }

            var currentPosition = e.GetCurrentPoint(_parentCanvas).Position;
            var delta = new Offset(currentPosition.X - _lastPosition.X, currentPosition.Y - _lastPosition.Y);
            controller.MoveCommand.Execute(delta);
            _lastPosition = currentPosition;
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (_propertyChangedSource is not null)
            {
                _propertyChangedSource.PropertyChanged -= OnControllerPropertyChanged;
                _propertyChangedSource = null;
            }

            if (args.NewValue is INotifyPropertyChanged notify)
            {
                _propertyChangedSource = notify;
                _propertyChangedSource.PropertyChanged += OnControllerPropertyChanged;
            }

            SyncOutputSlot();
        }

        private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ControllerViewModel.Anchor) or nameof(ControllerViewModel.Size) or nameof(ControllerViewModel.OutputSlot))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncOutputSlot);
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

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child is not null)
            {
                child = VisualTreeHelper.GetParent(child);
                if (child is T parent)
                {
                    return parent;
                }
            }

            return null;
        }
    }
}
