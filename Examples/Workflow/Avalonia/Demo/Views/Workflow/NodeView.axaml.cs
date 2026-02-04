using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo;

public partial class NodeView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;
    private Canvas? _dragCanvas;

    public NodeView()
    {
        InitializeComponent();
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
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        _dragCanvas = null;
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