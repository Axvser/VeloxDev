using Demo.ViewModels;
using Microsoft.UI.Xaml.Controls;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views
{
    public sealed partial class WorkflowView : UserControl
    {
        public WorkflowView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 示例 : 【工作流图】视图模型常用命令
        /// </summary>
        public void WorkflowCommands()
        {
            if (DataContext is WorkflowViewModel work)
            {
                work.CreateNodeCommand.Execute(null); // 增加节点     | param : Demo.ViewModels.NodeViewModel
                work.SetPointerCommand.Execute(null); // 设置鼠标位置 | param : VeloxDev.Core.WorkflowSystem.Anchor
                work.UndoCommand.Execute(null);       // 撤销         | param : null
            }

            // 附 : 创建一个新的 NodeViewModel
            var node = new NodeViewModel()
            {
                Anchor = new Anchor(left: 0, top: 0, layer: 1), // Node在工作流图中的位置与层级
                Size = new Size(width: 100, height: 100)        // Node的尺寸
            };
        }
    }
}
