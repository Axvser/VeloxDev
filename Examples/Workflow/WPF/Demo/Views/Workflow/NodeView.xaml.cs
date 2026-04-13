using Demo.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class NodeView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public NodeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => SyncSlots();
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
        SyncSlots();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null) return;

        var currentPosition = e.GetPosition(_parentCanvas);
        var delta = currentPosition - _lastPosition;

        if (DataContext is NodeViewModel nodeContext)
        {
            nodeContext.MoveCommand.Execute(new Offset(delta.X, delta.Y));
        }

        _lastPosition = currentPosition;
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
        if (e.PropertyName is nameof(NodeViewModel.Anchor) or nameof(NodeViewModel.Size) or nameof(NodeViewModel.InputSlot) or nameof(NodeViewModel.OutputSlot) or nameof(NodeViewModel.HasInputSlot) or nameof(NodeViewModel.HasOutputSlot))
        {
            Dispatcher.Invoke(SyncSlots);
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

        SyncSlot(node, node.InputSlot, isInput: true);
        SyncSlot(node, node.OutputSlot, isInput: false);
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
}