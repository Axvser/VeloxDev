using Demo.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class WorkflowView : UserControl
    {
        private readonly WorkflowViewModel _workflowViewModel = new();

        public WorkflowView()
        {
            InitializeComponent();

            var slot1 = new SlotViewModel()
            {
                Offset = new Anchor(170, 120),
                Size = new Size(20, 20)
            };
            var slot2 = new SlotViewModel()
            {
                Offset = new Anchor(10, 200),
                Size = new Size(20, 20)
            };

            var node1 = new NodeViewModel()
            {
                Size = new Size(200, 200),
                Anchor = new Anchor(50, 50, 1),
                Name = "任务"
            };
            var node2 = new NodeViewModel()
            {
                Size = new Size(300, 300),
                Anchor = new Anchor(250, 250, 1),
                Name = "任务"
            };

            node1.Slots.Add(slot1);
            node2.Slots.Add(slot2);

            _workflowViewModel.Nodes.Add(node1);
            _workflowViewModel.Nodes.Add(node2);

            // 设置 DataContext
            DataContext = _workflowViewModel;

            // 执行测试工作（模拟信号传递）
            node1.WorkCommand.Execute(null);

            // 注册鼠标事件
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
        }

        /// <summary>
        /// 当指针移动时，更新工作流中的鼠标位置。
        /// </summary>
        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (DataContext is not IWorkflowTree tree)
                return;

            var point = e.GetCurrentPoint(this).Position;
            tree.SetPointerCommand.Execute(new Anchor(point.X, point.Y, 0));
        }

        /// <summary>
        /// 当指针释放时，清除虚拟连线的起点。
        /// </summary>
        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (DataContext is not IWorkflowTree tree)
                return;

            tree.VirtualLink.Sender = null;
        }
    }
}
