﻿using System.Diagnostics;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.ViewModels.WorkflowHelpers
{
    public partial class NodeHelper : WorkflowHelper.ViewModel.Node
    {
        private NodeViewModel? _viewModel;
        private readonly Random rnd = new(); // 随机时间模拟

        public override void Initialize(IWorkflowNodeViewModel node)
        {
            // [ Standard ] 框架初始化 Helper 持有的视图模型
            base.Initialize(node);

            // [ User ] 用户自定义 Helper 具体类型
            _viewModel = node as NodeViewModel;
        }

        public override async Task CloseAsync()
        {
            // [ Standard ] 框架安全地关闭工作流
            await base.CloseAsync();

            // [ User ] 清除任务计数器
            if (_viewModel is null) return;
            _viewModel.TaskCount = 0;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            try
            {
                // [ User ] 跟踪任务计数并模拟随机任务耗时
                if (_viewModel is null) return;
                _viewModel.TaskCount++;
                await Task.Delay(rnd.Next(1000, 7000), ct);
                _viewModel.TaskCount--;

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
