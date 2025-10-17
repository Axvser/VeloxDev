using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    [Flags]
    public enum SlotChannel : int
    {
        Default = 0,          // 仅允许一个源或者一个目标
        OneTarget = 1,        // 仅允许一个目标
        OneSource = 2,        // 仅允许一个源
        MultipleTargets = 4,  // 允许多个目标
        MultipleSources = 8   // 允许多个源
    }

    [Flags]
    public enum SlotState : int
    {
        StandBy = 0,             // 空闲状态,未连接
        PreviewSender = 1,       // 预览发送端状态,正在连接过程中,作为发送端
        PreviewProcessor = 2,    // 预览处理端状态,正在连接过程中,作为处理端
        Sender = 4,              // 已连接状态,作为发送端
        Processor = 8            // 已连接状态,作为处理端
    }

    public interface IWorkflowSlotViewModel : IWorkflowViewModel
    {
        public ObservableCollection<IWorkflowNodeViewModel> Targets { get; set; }
        public ObservableCollection<IWorkflowNodeViewModel> Sources { get; set; }
        public IWorkflowNodeViewModel? Parent { get; set; }
        public SlotChannel Channel { get; set; }
        public SlotState State { get; set; }
        public Anchor Anchor { get; set; }
        public Anchor Offset { get; set; }
        public Size Size { get; set; }

        public IVeloxCommand PressCommand { get; }     // 按下Slot,更新当前始端位置,进入连接模式
        public IVeloxCommand MoveCommand { get; }      // 移动触点,更新当前末端位置
        public IVeloxCommand ReleaseCommand { get; }   // 松开Slot,结束连接模式,验证并处理连接,同步Slot状态

        public IVeloxCommand DeleteCommand { get; }    // 从工作流删除此Slot

        public IWorkflowSlotViewModelHelper GetHelper();
        public Task SetHelperAsync(IWorkflowSlotViewModelHelper helper);
    }

    public interface IWorkflowSlotViewModelHelper : IDisposable
    {
        public Task InitializeAsync(IWorkflowSlotViewModel viewModel);
        public Task CloseAsync();
        public void OnChannelChanged(SlotChannel oldValue, SlotChannel newValue);
        public void OnAnchorChanged(Anchor oldValue, Anchor newValue);
        public void OnOffsetChanged(Anchor oldValue, Anchor newValue);
        public void OnSizeChanged(Size oldValue, Size newValue);
        public Task PressAsync();
        public Task MoveAsync(object? parameter);
        public Task ReleaseAsync();
        public Task DeleteAsync();
    }
}
