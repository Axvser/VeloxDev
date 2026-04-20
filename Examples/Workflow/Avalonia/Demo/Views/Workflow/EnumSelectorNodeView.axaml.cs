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

public partial class EnumSelectorNodeView : UserControl
{
    private Point _lastPoint;
    private bool _isDragging;
    private Canvas? _parentCanvas;
    private IWorkflowNodeViewModel? _subscribedNode;

    public EnumSelectorNodeView()
    {
        InitializeComponent();
        PART_OutputSlotsList.LayoutUpdated += (_, _) => SyncSlotLayouts();
        AttachedToVisualTree += (_, _) =>
        {
            _parentCanvas = this.GetVisualAncestors().OfType<Canvas>().FirstOrDefault();
            Dispatcher.UIThread.Post(SyncSlotLayouts, DispatcherPriority.Render);
        };
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                SyncSlotLayouts();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedNode is INotifyPropertyChanged oldNpc)
            oldNpc.PropertyChanged -= OnNodePropertyChanged;

        _subscribedNode = DataContext as IWorkflowNodeViewModel;

        if (_subscribedNode is INotifyPropertyChanged newNpc)
            newNpc.PropertyChanged += OnNodePropertyChanged;

        SyncSlotLayouts();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or "InputSlot" or "OutputSlots")
        {
            Dispatcher.UIThread.Post(SyncSlotLayouts, DispatcherPriority.Render);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_parentCanvas == null || sender is not Control control) return;
        _isDragging = true;
        _lastPoint = e.GetPosition(_parentCanvas);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null || DataContext is not IWorkflowNodeViewModel vm) return;
        var current = e.GetPosition(_parentCanvas);
        var offset = new Offset(current.X - _lastPoint.X, current.Y - _lastPoint.Y);
        _lastPoint = current;
        vm.MoveCommand.Execute(offset);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
    }

    private void SyncSlotLayouts()
    {
        if (DataContext is not IWorkflowNodeViewModel node) return;
        if (_parentCanvas == null) return;

        SyncSlot(PART_InputSlot, node);

        if (PART_OutputSlotsList?.ItemCount > 0)
        {
            for (int i = 0; i < PART_OutputSlotsList.ItemCount; i++)
            {
                var container = PART_OutputSlotsList.ContainerFromIndex(i);
                if (container is null) continue;
                var slotView = container.GetVisualDescendants().OfType<SlotView>().FirstOrDefault();
                if (slotView is not null)
                    SyncSlot(slotView, node);
            }
        }
    }

    private void SyncSlot(Control? control, IWorkflowNodeViewModel node)
    {
        if (control is null || control.DataContext is not IWorkflowSlotViewModel slot) return;
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return;

        if (_parentCanvas is not null)
        {
            var centerOnCanvas = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _parentCanvas);
            if (centerOnCanvas is not null)
            {
                var actualOffset = (node.Parent as TreeViewModel)?.Layout.ActualOffset;
                slot.Anchor = new Anchor(
                    centerOnCanvas.Value.X - (actualOffset?.Horizontal ?? 0),
                    centerOnCanvas.Value.Y - (actualOffset?.Vertical ?? 0),
                    slot.Anchor.Layer);
                return;
            }
        }

        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this);
        if (center is null) return;
        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + center.Value.X,
            node.Anchor.Vertical + center.Value.Y,
            slot.Anchor.Layer);
    }
}
