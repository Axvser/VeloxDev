using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    public interface IWorkflowNodeViewModel : IWorkflowViewModel
    {
        public IWorkflowTreeViewModel? Parent { get; set; }
        public Anchor Anchor { get; set; }
        public Size Size { get; set; }
        public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; }

        public IVeloxCommand CreateSlotCommand { get; } // 在此节点上创建一个新的Slot
        public IVeloxCommand WorkCommand { get; }   // 执行此节点的工作逻辑
        public IVeloxCommand BroadcastCommand { get; }  // 广播此节点的数据到所有连接的节点
        public IVeloxCommand DeleteCommand { get; } // 从工作流删除此节点

        public IWorkflowNodeViewModelHelper GetHelper();
        public Task SetHelperAsync(IWorkflowNodeViewModelHelper helper);
    }

    public interface IWorkflowNodeViewModelHelper : IDisposable
    {
        public Task InitializeAsync(IWorkflowNodeViewModel viewModel);
        public Task CloseAsync();
        public void OnAnchorChanged(Anchor oldValue, Anchor newValue);
        public void OnSizeChanged(Size oldValue, Size newValue);
        public Task CreateSlotAsync(object? parameter);
        public Task WorkAsync(object? parameter);
        public Task BroadcastAsync(object? parameter);
        public Task DeleteAsync();
    }
}
