using VeloxDev.Core.Interfaces.MVVM;
using VeloxDev.Core.WorkflowSystem;

namespace VeloxDev.Core.Interfaces.WorkflowSystem
{
    [Flags] // 语义优先级 > Multiple > One > None
    public enum SlotChannel : int
    {
        None = 1,             // 无可用通道
        OneTarget = 2,        // 仅允许一个目标
        OneSource = 4,        // 仅允许一个源
        OneBoth = OneTarget | OneSource,
        MultipleTargets = 8,  // 允许多个目标
        MultipleSources = 16, // 允许多个源
        MultipleBoth = MultipleTargets | MultipleSources
    }

    [Flags] // 混合状态模型
    public enum SlotState : int
    {
        StandBy = 1,             // 空闲状态,未连接
        PreviewSender = 2,       // 预览发送端状态,正在连接过程中,作为发送端
        PreviewReceiver = 4,     // 预览处理端状态,正在连接过程中,作为处理端
        Sender = 8,              // 已连接状态,作为发送端
        Receiver = 16            // 已连接状态,作为处理端
    }

    public interface IWorkflowSlotViewModel : IWorkflowViewModel
    {
        public HashSet<IWorkflowSlotViewModel> Targets { get; set; }
        public HashSet<IWorkflowSlotViewModel> Sources { get; set; }
        public IWorkflowNodeViewModel? Parent { get; set; }
        public SlotChannel Channel { get; set; }
        public SlotState State { get; set; }
        public Anchor Anchor { get; set; }
        public Offset Offset { get; set; }
        public Size Size { get; set; }

        public IVeloxCommand SaveOffsetCommand { get; }     // 保存偏移 | parameter Null
        public IVeloxCommand SaveSizeCommand { get; }       // 保存尺寸 | parameter Null
        public IVeloxCommand SetOffsetCommand { get; }      // 设定偏移 | parameter Offset
        public IVeloxCommand SetSizeCommand { get; }        // 设定尺寸 | parameter Size
        public IVeloxCommand SetChannelCommand { get; }     // 设定通道 | parameter SlotChannel

        public IVeloxCommand ApplyConnectionCommand { get; }   // 作为连接构建发起方 | parameter Null
        public IVeloxCommand ReceiveConnectionCommand { get; } // 作为连接构建接收方 | parameter Null

        public IVeloxCommand DeleteCommand { get; }     // 删除Slot | parameter Null

        public IWorkflowSlotViewModelHelper GetHelper();
        public void SetHelper(IWorkflowSlotViewModelHelper helper);
    }

    public interface IWorkflowSlotViewModelHelper : IWorkflowHelper
    {
        public void Initialize(IWorkflowSlotViewModel slot);

        public void SetOffset(Offset offset);
        public void SetSize(Size size);
        public void SaveOffset();
        public void SaveSize();
        public void SetChannel(SlotChannel channel);
        public void UpdateAnchor();
        public void UpdateState();

        public void ApplyConnection();
        public void ReceiveConnection();

        public void Delete();
    }
}
