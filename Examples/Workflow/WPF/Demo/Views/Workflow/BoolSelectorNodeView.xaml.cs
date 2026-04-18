using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class BoolSelectorNodeView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public BoolSelectorNodeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => SyncSlots();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        _parentCanvas = FindVisualParent<Canvas>(this);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_parentCanvas == null || e.LeftButton != MouseButtonState.Pressed) return;
        _isDragging = true;
        _lastPosition = e.GetPosition(_parentCanvas);
        Mouse.Capture(sender as IInputElement ?? this);
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        Mouse.Capture(null);
        SyncSlots();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null || DataContext is not IWorkflowNodeViewModel vm) return;
        var current = e.GetPosition(_parentCanvas);
        var delta = current - _lastPosition;
        vm.MoveCommand.Execute(new Offset(delta.X, delta.Y));
        _lastPosition = current;
        SyncSlots();
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
            _propertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
            _propertyChangedSource = null;
        }

        if (e.NewValue is INotifyPropertyChanged notify)
        {
            _propertyChangedSource = notify;
            notify.PropertyChanged += OnNodePropertyChanged;
        }

        SyncSlots();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or "InputSlot" or "OutputSlots")
        {
            Dispatcher.Invoke(SyncSlots);
        }
    }

    private void SyncSlots()
    {
        if (DataContext is not IWorkflowNodeViewModel node) return;

        SyncSlot(node, PART_InputSlot, isInput: true);
        SyncSlot(node, PART_TrueSlot, isOutput: true, verticalFraction: 0.3);
        SyncSlot(node, PART_FalseSlot, isOutput: true, verticalFraction: 0.7);
    }

    private static void SyncSlot(IWorkflowNodeViewModel node, SlotView? control, bool isInput = false, bool isOutput = false, double verticalFraction = 0.5)
    {
        if (control?.DataContext is not IWorkflowSlotViewModel slot) return;
        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + (isInput ? 0 : node.Size.Width),
            node.Anchor.Vertical + (node.Size.Height * verticalFraction),
            slot.Anchor.Layer);
    }
}
