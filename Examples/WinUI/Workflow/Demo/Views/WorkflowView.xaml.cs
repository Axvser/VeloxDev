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
        /// ʾ�� : ��������ͼ����ͼģ�ͳ�������
        /// </summary>
        public void WorkflowCommands()
        {
            if (DataContext is WorkflowViewModel work)
            {
                work.CreateNodeCommand.Execute(null); // ���ӽڵ�     | param : Demo.ViewModels.NodeViewModel
                work.SetPointerCommand.Execute(null); // �������λ�� | param : VeloxDev.Core.WorkflowSystem.Anchor
                work.UndoCommand.Execute(null);       // ����         | param : null
            }

            // �� : ����һ���µ� NodeViewModel
            var node = new NodeViewModel()
            {
                Anchor = new Anchor(left: 0, top: 0, layer: 1), // Node�ڹ�����ͼ�е�λ����㼶
                Size = new Size(width: 100, height: 100)        // Node�ĳߴ�
            };
        }
    }
}
