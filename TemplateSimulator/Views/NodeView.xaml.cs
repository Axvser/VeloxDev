using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TemplateSimulator.ViewModels;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.Views;

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
            if (dp is NodeView nodeView && nodeView.DataContext is IWorkflowNodeViewModel nodeContext)
            {
                if ((bool)e.NewValue)
                {
                    nodeView.Background = Brushes.Red;
                }
                else
                {
                    nodeView.Background = Brushes.Lime;
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
            var offset = new Offset(delta.X, delta.Y);
            nodeContext.GetHelper().Move(offset);
        }

        _lastPosition = currentPosition;
        e.Handled = true;
    }
}