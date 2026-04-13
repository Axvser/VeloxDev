using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class NodeView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public NodeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _parentCanvas = this.GetVisualAncestors().OfType<Canvas>().FirstOrDefault();
            SyncSlotLayouts();
        };
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
        if (_parentCanvas == null)
            return;

        _lastPoint = e.GetPosition(_parentCanvas);
        _isDragging = true;
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || DataContext is not IWorkflowNodeViewModel node || _parentCanvas == null)
            return;

        var current = e.GetPosition(_parentCanvas);
        var offset = new Offset(current.X - _lastPoint.X, current.Y - _lastPoint.Y);
        node.MoveCommand.Execute(offset);
        _lastPoint = current;
        SyncSlotLayouts();
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
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

        if (_parentCanvas is not null)
        {
            var centerOnCanvas = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _parentCanvas);
            if (centerOnCanvas is not null)
            {
                var actualOffset = (node.Parent as ViewModels.TreeViewModel)?.Layout.ActualOffset;
                slot.Anchor = new Anchor(
                    centerOnCanvas.Value.X - (actualOffset?.Horizontal ?? 0),
                    centerOnCanvas.Value.Y - (actualOffset?.Vertical ?? 0),
                    slot.Anchor.Layer);
                return;
            }
        }

        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this);
        if (center is null)
        {
            return;
        }

        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.Value.X,
            node.Anchor.Vertical + center.Value.Y,
            slot.Anchor.Layer);
    }
}