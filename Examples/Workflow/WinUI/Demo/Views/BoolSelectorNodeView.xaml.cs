using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public sealed partial class BoolSelectorNodeView : UserControl
{
    private bool _isDragging;
    private Windows.Foundation.Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public BoolSelectorNodeView()
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
        if (_parentCanvas == null) return;
        var point = e.GetCurrentPoint(_parentCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;
        _isDragging = true;
        _lastPosition = point.Position;
        if (sender is UIElement element) element.CapturePointer(e.Pointer);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        if (sender is UIElement element) element.ReleasePointerCapture(e.Pointer);
        SyncSlots();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null || DataContext is not IWorkflowNodeViewModel vm) return;
        var current = e.GetCurrentPoint(_parentCanvas).Position;
        var delta = new Offset(current.X - _lastPosition.X, current.Y - _lastPosition.Y);
        vm.MoveCommand.Execute(delta);
        _lastPosition = current;
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
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or "InputSlot" or "OutputSlots")
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncSlots);
        }
    }

    private void SyncSlots()
    {
        if (DataContext is not IWorkflowNodeViewModel node) return;

        PART_InputSlot.Visibility = (node as BoolSelectorNodeViewModel)?.HasInputSlot == true
            ? Visibility.Visible : Visibility.Collapsed;

        SyncSlot(node, PART_InputSlot, isInput: true);
        SyncSlot(node, PART_TrueSlot, isInput: false, verticalFraction: 0.3);
        SyncSlot(node, PART_FalseSlot, isInput: false, verticalFraction: 0.7);
    }

    private static void SyncSlot(IWorkflowNodeViewModel node, SlotView? control, bool isInput, double verticalFraction = 0.5)
    {
        if (control?.DataContext is not IWorkflowSlotViewModel slot) return;
        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + (isInput ? 0 : node.Size.Width),
            node.Anchor.Vertical + (node.Size.Height * verticalFraction),
            slot.Anchor.Layer);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is T parent) return parent;
        }
        return null;
    }
}
