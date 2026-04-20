using Demo.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public sealed partial class EnumSelectorNodeView : UserControl
{
    private bool _isDragging;
    private Windows.Foundation.Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public EnumSelectorNodeView()
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
            or "InputSlot" or "OutputSlots" or "EnumType")
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, SyncSlots);
        }
    }

    private void SyncSlots()
    {
        if (DataContext is not IWorkflowNodeViewModel node) return;

        PART_InputSlot.Visibility = (node as EnumSelectorNodeViewModel)?.HasInputSlot == true
            ? Visibility.Visible : Visibility.Collapsed;

        SyncSlotSimple(node, PART_InputSlot, isInput: true);

        if (PART_OutputSlotsList?.Items.Count > 0)
        {
            var enumNode = node as EnumSelectorNodeViewModel;
            for (int i = 0; i < PART_OutputSlotsList.Items.Count; i++)
            {
                var container = PART_OutputSlotsList.ContainerFromIndex(i) as FrameworkElement;
                if (container is null) continue;

                var slotView = FindVisualChild<SlotView>(container);
                if (slotView?.DataContext is IWorkflowSlotViewModel slot)
                {
                    slot.Anchor = new Anchor(
                        node.Anchor.Horizontal + node.Size.Width,
                        node.Anchor.Vertical + 48 + 60 + (i * 22) + 10,
                        slot.Anchor.Layer);
                }

                if (enumNode is not null)
                {
                    var label = FindVisualChild<TextBlock>(container);
                    if (label is not null)
                        label.Text = enumNode.GetSlotLabel(i);
                }
            }
        }
    }

    private static void SyncSlotSimple(IWorkflowNodeViewModel node, SlotView? control, bool isInput)
    {
        if (control?.DataContext is not IWorkflowSlotViewModel slot) return;
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
            if (child is T parent) return parent;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }
}
