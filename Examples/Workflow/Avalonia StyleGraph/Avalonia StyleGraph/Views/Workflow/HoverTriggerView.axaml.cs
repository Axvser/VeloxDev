using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Avalonia_StyleGraph;

public partial class HoverTriggerView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;

    public HoverTriggerView()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(LayoutRoot);

        // 只在左键按下时开始拖拽
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _lastPoint = e.GetPosition(LayoutRoot);
            _isDragging = true;

            // 关键：捕获指针事件
            if (sender is Control control)
            {
                e.Pointer.Capture(control);
            }


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
}