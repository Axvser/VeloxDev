using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Demo.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class ControllerView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;
    private INotifyPropertyChanged? _propertyChangedSource;

    public ControllerView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(SyncOutputSlot, DispatcherPriority.Render);
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                SyncOutputSlot();
            }
        };
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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_propertyChangedSource is not null)
        {
            _propertyChangedSource.PropertyChanged -= OnControllerPropertyChanged;
            _propertyChangedSource = null;
        }

        if (DataContext is INotifyPropertyChanged notify)
        {
            _propertyChangedSource = notify;
            notify.PropertyChanged += OnControllerPropertyChanged;
        }

        SyncOutputSlot();
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Anchor" or "Size" or nameof(ControllerViewModel.OutputSlot))
        {
            Dispatcher.UIThread.Post(SyncOutputSlot, DispatcherPriority.Render);
        }
    }

    private void SyncOutputSlot()
    {
        if (DataContext is not ControllerViewModel node || node.OutputSlot is null || PART_OutputSlot.Bounds.Width <= 0 || PART_OutputSlot.Bounds.Height <= 0)
        {
            return;
        }

        if (_parentCanvas is not null)
        {
            var centerOnCanvas = PART_OutputSlot.TranslatePoint(new Point(PART_OutputSlot.Bounds.Width / 2, PART_OutputSlot.Bounds.Height / 2), _parentCanvas);
            if (centerOnCanvas is not null)
            {
                var actualOffset = (node.Parent as TreeViewModel)?.Layout.ActualOffset;
                node.OutputSlot.Anchor = new Anchor(
                    centerOnCanvas.Value.X - (actualOffset?.Horizontal ?? 0),
                    centerOnCanvas.Value.Y - (actualOffset?.Vertical ?? 0),
                    node.OutputSlot.Anchor.Layer);
                return;
            }
        }

        var center = PART_OutputSlot.TranslatePoint(new Point(PART_OutputSlot.Bounds.Width / 2, PART_OutputSlot.Bounds.Height / 2), this);
        if (center is null)
        {
            return;
        }

        node.OutputSlot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.Value.X,
            node.Anchor.Vertical + center.Value.Y,
            node.OutputSlot.Anchor.Layer);
    }
}