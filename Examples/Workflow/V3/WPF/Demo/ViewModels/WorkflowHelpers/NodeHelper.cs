using System.Diagnostics;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.WorkflowHelpers
{
    public partial class NodeHelper : WorkflowHelper.ViewModel.Node
    {
        private NodeViewModel? _viewModel;

        public override void Initialize(IWorkflowNodeViewModel node)
        {
            // [ Standard Component Helper ] 框架自动处理Workflow
            base.Initialize(node);

            // [ User Component Helper ] 用户专注于业务逻辑
            _viewModel = node as NodeViewModel;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            try
            {
                // [ User Component Helper ] 自定义节点工作逻辑
                Random rnd = new();
                await Task.Delay(rnd.Next(1000, 7000), ct);
                // [ Standard Component Helper ] 继续传播任务参数给子节点
                await BroadcastAsync(parameter, ct);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public override Task<bool> ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct)
        {
            // sender    | 传播起始 Slot
            // receicer  | 传播终止 Slot
            // parameter | 任务参数
            // ct        | 取消令牌
            if (!ct.IsCancellationRequested)
            {
                // 安全地验证是否可以执行任务参数传递
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }
}
