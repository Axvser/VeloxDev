using Demo.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.Core.Interfaces.WorkflowSystem;
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

    public static readonly DependencyProperty IsWorkingProperty = DependencyProperty.Register(
        nameof(IsWorking), typeof(bool), typeof(NodeView), new PropertyMetadata(false, (dp, e) =>
        {
            if (dp is NodeView nodeView && nodeView.DataContext is IWorkflowNode nodeContext)
            {
                if ((bool)e.NewValue)
                {
                    nodeView.Background = Brushes.Red;
                    nodeContext.Name = "任务 < 执行中 >";
                }
                else
                {
                    nodeView.Background = Brushes.Lime;
                    nodeContext.Name = "任务";
                }
            }
        }));
    public bool IsWorking
    {
        get => (bool)GetValue(IsWorkingProperty);
        set => SetValue(IsWorkingProperty, value);
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        // 获取父级Canvas
        _parentCanvas = FindVisualParent<Canvas>(this);
    }

    // 查找视觉树父级
    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;

        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    // 鼠标按下时，获取鼠标相对于容器左上角的坐标，并记录为拖拽中
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _parentCanvas != null)
        {
            _isDragging = true;
            _lastPosition = e.GetPosition(_parentCanvas);
            e.Handled = true;
        }
    }

    // 鼠标离开时，并记录为非拖拽中
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = false;
            e.Handled = true;
        }
    }

    // 鼠标移动发生在拖拽模式时，请更新节点视图模型的 Anchor
    private void OnMouseMove(object sender, MouseEventArgs e)
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