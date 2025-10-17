using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowTreeViewModel : IWorkflowViewModel
    {
        public IWorkflowLinkViewModel VirtualLink { get; set; }
        public ObservableCollection<IWorkflowNodeViewModel> Nodes { get; set; }
        public ObservableCollection<IWorkflowLinkGroupViewModel> LinkGroups { get; set; }

        public IVeloxCommand SubmitActionPairCommand { get; } // 提交用于[ 撤销 OR 重做 ]的行为对

        public IVeloxCommand CreateNodeCommand { get; }    // 创建节点

        public IVeloxCommand PressSlotCommand { get; }     // 按下Slot,更新当前始端位置,进入连接模式
        public IVeloxCommand MovePointerCommand { get; }   // 移动触点,更新当前末端位置
        public IVeloxCommand ReleaseSlotCommand { get; }   // 松开Slot,结束连接模式,验证并处理连接,同步Slot状态

        public IVeloxCommand RedoCommand { get; }          // 重做上一个操作
        public IVeloxCommand UndoCommand { get; }          // 撤销上一个操作

        public IWorkflowTreeViewModelHelper GetHelper();
        public Task SetHelperAsync(IWorkflowTreeViewModelHelper helper);
    }

    public interface IWorkflowTreeViewModelHelper : IDisposable
    {
        public Task InitializeAsync(IWorkflowTreeViewModel viewModel);
        public Task CloseAsync();
        public Task SubmitActionPairAsync(object? parameter);
        public Task CreateNodeAsync(object? parameter);
        public Task PressSlotAsync(object? parameter);
        public Task MovePointerAsync(object? parameter);
        public Task ReleaseSlotAsync(object? parameter);
        public Task RedoAsync();
        public Task UndoAsync();
    }
}
