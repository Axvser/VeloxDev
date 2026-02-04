using CliWrap;
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

        public override void Install(IWorkflowNodeViewModel node)
        {
            base.Install(node);
            _viewModel = node as NodeViewModel;
        }

        public override void Uninstall(IWorkflowNodeViewModel node)
        {
            base.Uninstall(node);
            _viewModel = null;
        }

        public override async Task WorkAsync(object? parameter, CancellationToken ct)
        {
            // CliWrap 执行 SSH 登录任务
            try
            {
                if (_viewModel == null) return;
                var result = await Cli.Wrap("ssh")
                    .WithArguments($"{_viewModel.UserName}@{_viewModel.Host} -p {_viewModel.Port}")
                    .WithStandardInputPipe(PipeSource.FromString(_viewModel.PassWord))
                    .ExecuteAsync(ct);

                Debug.WriteLine($"SSH连接执行完成，退出码: {result.ExitCode}");
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"SSH连接被取消: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SSH连接失败: {ex.Message}");
            }
        }
    }
}
