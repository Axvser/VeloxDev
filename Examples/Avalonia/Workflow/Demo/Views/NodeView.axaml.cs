using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Demo.ViewModels;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views;

public partial class NodeView : UserControl
{
    private bool _isDragging;
    private Point _lastPosition;
    private Canvas? _parentCanvas;

    public NodeView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 获取父级Canvas
        _parentCanvas = this.FindAncestorOfType<Canvas>();
    }

    // 触点按下时，获取鼠标相对于容器左上角的坐标，并记录为拖拽中
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _parentCanvas != null)
        {
            _isDragging = true;
            _lastPosition = e.GetPosition(_parentCanvas);
            e.Handled = true;
        }
    }

    // 触点离开时，并记录为非拖拽中
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            _isDragging = false;
            e.Handled = true;
        }
    }

    // 触点移动发生在拖拽模式时，请更新节点视图模型的 Anchor
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _parentCanvas == null) return;

        // 计算鼠标位置偏移量
        var currentPosition = e.GetPosition(_parentCanvas);
        var delta = currentPosition - _lastPosition;

        if (DataContext is NodeViewModel nodeContext)
        {
            // 转换为 VeloxDev 中的通用 Anchor，它描述 位置 & 层级
            var deltaAnchor = new Anchor(delta.X, delta.Y, 0);
            // 注意，必须使用 += 或 = 直接操作 Anchor，否则您的曲线数据无法正确更新
            nodeContext.Anchor += deltaAnchor;
        }

        _lastPosition = currentPosition;
        e.Handled = true;
    }
}