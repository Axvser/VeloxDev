using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Demo.ViewModels;
using System.Linq;
using VeloxDev.Core.WorkflowSystem;

namespace Demo;

public partial class ControllerView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;

    public ControllerView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _parentCanvas = this.GetVisualAncestors().OfType<Canvas>().FirstOrDefault();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_parentCanvas == null) return;
        var pointer = e.GetCurrentPoint(this);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPosition = e.GetPosition(_parentCanvas);
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
            e.Pointer.Capture(null);
            
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null) return;
        var currentPosition = e.GetPosition(_parentCanvas);
        var delta = currentPosition - _lastPosition;
        if (DataContext is ControllerViewModel nodeContext)
        {
            nodeContext.MoveCommand.Execute(new Offset(delta.X, delta.Y));
        }
        _lastPosition = currentPosition;
        
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }
}