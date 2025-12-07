using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph;

public partial class HoverProcessorView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;

    public HoverProcessorView()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastPoint = e.GetPosition(LayoutRoot);
        _isDragging = true;

        // 关键：捕获指针事件
        if (sender is Control control)
        {
            e.Pointer.Capture(control);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;

            // 释放指针捕获
            e.Pointer.Capture(null);

        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || DataContext is not IWorkflowNodeViewModel node)
            return;

        var current = e.GetPosition(LayoutRoot);
        var offset = new Offset(current.X - _lastPoint.X, current.Y - _lastPoint.Y);

        node.MoveCommand.Execute(offset);

        _lastPoint = current;

    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        switch (e.DataTransfer.TryGetText())
        {
            case "Hover Trigger":
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
                break;
            default:
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                break;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        switch (e.DataTransfer.TryGetText())
        {
            case "Hover Trigger":
                e.DragEffects = DragDropEffects.Move;
                e.Handled = true;
                break;
            default:
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                break;
        }
    }
}