using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper
{
    public partial class NodeHelper : WorkflowHelper.ViewModel.Node
    {
        private NodeViewModel? _viewModel;
        private readonly Random rnd = new(); // 随机时间模拟

        public override void Install(IWorkflowNodeViewModel node)
        {
            // [ Standard ] 框架初始化 Helper 持有的视图模型
            base.Install(node);

            // [ User ] 跟踪任务计数
            _viewModel = node as NodeViewModel;
            _viewModel!.WorkCommand.TaskStarted += (e) => _viewModel.RunCount++;
            _viewModel!.WorkCommand.TaskExited += (e) => _viewModel.RunCount--;
            _viewModel!.WorkCommand.TaskEnqueued += (e) => _viewModel.WaitCount++;
            _viewModel!.WorkCommand.TaskDequeued += (e) => _viewModel.WaitCount--;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            try
            {
                // [ User ] 模拟随机任务耗时
                if (_viewModel is null) return;
                await Task.Delay(rnd.Next(1000, 7000), ct);

                // [ Standard ] 框架广播任务参数给子节点
                await BroadcastAsync(parameter, ct);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public override async Task<bool> ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct)
        {
            // sender    | 传播起始 Slot
            // receicer  | 传播终止 Slot
            // parameter | 任务参数
            // ct        | 取消令牌

            try
            {
                // [ User ] 随机模拟验证耗时 ( 当然，实际开发建议此处耗时越短越好 )
                await Task.Delay(rnd.Next(100, 2000), ct);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine(ex);
                return false;
            }
            return true;
        }
    }
}
