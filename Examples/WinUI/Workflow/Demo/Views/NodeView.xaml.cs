using Microsoft.UI.Xaml.Controls;
using VeloxDev.Core.WorkflowSystem;
using Demo.ViewModels;

namespace Demo.Views
{
    public sealed partial class NodeView : UserControl
    {
        public NodeView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 示例 : 【节点】视图模型常用命令
        /// </summary>
        public void NodeCommands()
        {
            if (DataContext is NodeViewModel node)
            {
                node.WorkCommand.Execute(null);           // 执行节点的工作任务       | param : nullable
                node.BroadcastCommand.Execute(null);      // 广播任务到所有连接的节点 | param : nullable
                node.CreateSlotCommand.Execute(null);     // 创建一个新的插槽         | param : ViewModels.SlotViewModel
                node.DeleteCommand.Execute(null);         // 删除此节点               | param : null
                node.MoveCommand.Execute(null);           // 移动节点                 | param : VeloxDev.Core.WorkflowSystem.Anchor or Windows.Foundation.Point
                node.UndoCommand.Execute(null);           // 撤销                     | param : null
            }

            // 附 : 创建一个新的 SlotViewModel
            var slot = new SlotViewModel()
            {
                Offset = new Anchor(left: 0, top: 0, layer: 1), // Slot相对于Node的偏移位置与层级
                Size = new Size(width: 20, height: 20)          // Slot的尺寸
            };
        }
    }
}
