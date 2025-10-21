using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

        public override Task<bool> ValidateBroadcastAsync(IWorkflowNodeViewModel sender, IWorkflowNodeViewModel receiver, object? parameter, CancellationToken ct)
        {
            // sender    | 传播起始 Node
            // receicer  | 传播终止 Node
            // parameter | 任务参数
            // ct        | 取消令牌
            if (sender.Parent != receiver.Parent && !ct.IsCancellationRequested)
            {
                // 安全地验证是否可以执行任务参数传递
                // 框架已经自带了Parent验证，这里只是一个演示
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }
}
