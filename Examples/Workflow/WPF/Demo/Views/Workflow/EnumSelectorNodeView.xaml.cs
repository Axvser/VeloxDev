using Demo.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public partial class EnumSelectorNodeView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public EnumSelectorNodeView()
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
            or "InputSlot" or "OutputSlots" or "EnumType")
        {
            Dispatcher.Invoke(SyncSlots);
        }
    }

    private void SyncSlots()
    {
        if (DataContext is not IWorkflowNodeViewModel node) return;

        SyncSlotSimple(node, PART_InputSlot, isInput: true);

        if (PART_OutputSlotsList?.Items.Count > 0)
        {
            var enumNode = node as EnumSelectorNodeViewModel;
            var generator = PART_OutputSlotsList.ItemContainerGenerator;
            for (int i = 0; i < PART_OutputSlotsList.Items.Count; i++)
            {
                var container = generator.ContainerFromIndex(i) as FrameworkElement;
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
