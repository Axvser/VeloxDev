using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class NodeView : UserControl
    {
        private bool _isDragging;
        private Windows.Foundation.Point _lastPosition;
        private Canvas? _parentCanvas;
        private INotifyPropertyChanged? _propertyChangedSource;

        public NodeView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                _parentCanvas = FindVisualParent<Canvas>(this);
                SyncSlots();
            };
            DataContextChanged += OnDataContextChanged;
            SizeChanged += (_, _) => SyncSlots();
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

            SyncSlots();
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || _parentCanvas == null || DataContext is not NodeViewModel node)
            {
                return;
            }

            var currentPosition = e.GetCurrentPoint(_parentCanvas).Position;
            var delta = new Offset(currentPosition.X - _lastPosition.X, currentPosition.Y - _lastPosition.Y);
            node.MoveCommand.Execute(delta);
            _lastPosition = currentPosition;
            SyncSlots();
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
        }

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (_propertyChangedSource is not null)
            {
                _propertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
                _propertyChangedSource = null;
            }

            if (args.NewValue is INotifyPropertyChanged notify)
            {
                _propertyChangedSource = notify;
                _propertyChangedSource.PropertyChanged += OnNodePropertyChanged;
            }

            SyncSlots();
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NodeViewModel.Anchor)
                or nameof(NodeViewModel.Size)
                or nameof(NodeViewModel.InputSlot)
                or nameof(NodeViewModel.OutputSlot)
                or nameof(NodeViewModel.HasInputSlot)
                or nameof(NodeViewModel.HasOutputSlot)
                or nameof(NodeViewModel.HasExecutionOrder)
                or nameof(NodeViewModel.HasWorkLoad))
            {
                DispatcherQueue.TryEnqueue(SyncSlots);
            }
        }

        private void SyncSlots()
        {
            if (DataContext is not NodeViewModel node)
            {
                return;
            }

            PART_InputSlot.Visibility = node.HasInputSlot ? Visibility.Visible : Visibility.Collapsed;
            PART_OutputSlot.Visibility = node.HasOutputSlot ? Visibility.Visible : Visibility.Collapsed;
            ExecutionOrderBadge.Visibility = node.HasExecutionOrder ? Visibility.Visible : Visibility.Collapsed;
            WorkLoadBadge.Visibility = node.HasWorkLoad ? Visibility.Visible : Visibility.Collapsed;

            SyncSlot(node, node.InputSlot, true);
            SyncSlot(node, node.OutputSlot, false);
        }

        private static void SyncSlot(IWorkflowNodeViewModel node, IWorkflowSlotViewModel? slot, bool isInput)
        {
            if (slot is null)
            {
                return;
            }

            slot.Anchor = new Anchor(
                node.Anchor.Horizontal + (isInput ? 0 : node.Size.Width),
                node.Anchor.Vertical + (node.Size.Height / 2),
                slot.Anchor.Layer);
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
