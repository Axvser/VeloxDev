using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.ComponentModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem.StandardEx;

namespace Demo;

public partial class NodeView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;
    private Canvas? _dragCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public NodeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => SyncSlotLayouts();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                SyncSlotLayouts();
            }
        };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 查找祖先中的 Canvas
        _dragCanvas = FindAncestorOfType<Canvas>();

        if (_dragCanvas == null)
            return;

        _lastPoint = e.GetPosition(_dragCanvas);
        _isDragging = true;
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            _dragCanvas = null;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || DataContext is not IWorkflowNodeViewModel node || _dragCanvas == null)
            return;

        var current = e.GetPosition(_dragCanvas);
        var offset = new Offset(current.X - _lastPoint.X, current.Y - _lastPoint.Y);
        node.MoveCommand.Execute(offset);
        _lastPoint = current;
        SyncSlotLayouts();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        _dragCanvas = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_propertyChangedSource is not null)
        {
            _propertyChangedSource.PropertyChanged -= OnNodePropertyChanged;
            _propertyChangedSource = null;
        }

        if (DataContext is INotifyPropertyChanged notify)
        {
            _propertyChangedSource = notify;
            notify.PropertyChanged += OnNodePropertyChanged;
        }

        SyncSlotLayouts();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size) or "InputSlot" or "OutputSlot")
        {
            SyncSlotLayouts();
        }
    }

    private void SyncSlotLayouts()
    {
        if (DataContext is not ViewModels.NodeViewModel node)
        {
            return;
        }

        SyncSlot(PART_InputSlot, node.InputSlot, node);
        SyncSlot(PART_OutputSlot, node.OutputSlot, node);
    }

    private void SyncSlot(Control? control, IWorkflowSlotViewModel? slot, IWorkflowNodeViewModel node)
    {
        if (control is null || slot is null || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return;
        }

        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this);
        if (center is null)
        {
            return;
        }

        slot.StandardSetOffset(new Offset(
            center.Value.X - control.Bounds.Width / 2,
            center.Value.Y - control.Bounds.Height / 2));
    }

    // 辅助方法：查找祖先
    private T? FindAncestorOfType<T>() where T : class
    {
        var parent = Parent;
        while (parent != null)
        {
            if (parent is T result)
                return result;
            parent = (parent as Visual)?.Parent;
        }
        return null;
    }
}