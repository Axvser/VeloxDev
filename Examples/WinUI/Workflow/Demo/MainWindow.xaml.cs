using Demo.ViewModels;
using Microsoft.UI.Xaml;
using VeloxDev.Core.WorkflowSystem;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        private readonly WorkflowViewModel workflowViewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            InitializeWorkflow(workflowViewModel);
            Workflow.DataContext = workflowViewModel;
        }

        /// <summary>
        /// 示例 : 纯数据地初始化一个工作流
        /// </summary>
        public static void InitializeWorkflow(WorkflowViewModel work)
        {
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

            work.Nodes.Add(node1);
            work.Nodes.Add(node2);
        }
    }
}
