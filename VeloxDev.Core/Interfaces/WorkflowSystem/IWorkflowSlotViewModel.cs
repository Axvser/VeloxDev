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
        public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; }
        public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; }
        public IWorkflowNodeViewModel? Parent { get; set; }
        public SlotChannel Channel { get; set; }
        public SlotState State { get; set; }
        public Anchor Anchor { get; set; }
        public Offset Offset { get; set; }
        public Size Size { get; set; }
        
        public IVeloxCommand SaveOffsetCommand { get; }     // 保存偏移 | parameter Null
        public IVeloxCommand SaveSizeCommand { get; }         // 保存尺寸 | parameter Null
        public IVeloxCommand SetOffsetCommand { get; }  // 设定偏移 | parameter Offset
        public IVeloxCommand SetSizeCommand { get; }      // 设定尺寸 | parameter Size

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

        public void ApplyConnection();
        public void ReceiveConnection();

        public void Delete();
    }
}
