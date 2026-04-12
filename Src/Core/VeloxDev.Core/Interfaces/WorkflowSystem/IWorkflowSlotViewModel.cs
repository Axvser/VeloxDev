using System.Collections.ObjectModel;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
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

        public IVeloxCommand SetSizeCommand { get; }        // 设定尺寸 | parameter Size
        public IVeloxCommand SetChannelCommand { get; }     // 设定通道 | parameter SlotChannel

        public IVeloxCommand SendConnectionCommand { get; }   // 作为连接构建发起方 | parameter Null
        public IVeloxCommand ReceiveConnectionCommand { get; } // 作为连接构建接收方 | parameter Null

        public IVeloxCommand DeleteCommand { get; }     // 删除Slot | parameter Null

        public IWorkflowSlotViewModelHelper GetHelper();
        public void SetHelper(IWorkflowSlotViewModelHelper helper);
    }

    public interface IWorkflowSlotViewModelHelper : IWorkflowHelper
    {
        public event EventHandler<IWorkflowSlotViewModel>? TargetAdded;
        public event EventHandler<IWorkflowSlotViewModel>? TargetRemoved;
        public event EventHandler<IWorkflowSlotViewModel>? SourceAdded;
        public event EventHandler<IWorkflowSlotViewModel>? SourceRemoved;

        public void Install(IWorkflowSlotViewModel slot);
        public void Uninstall(IWorkflowSlotViewModel slot);

        public void SetSize(Size size);
        public void SetLayer(int layer);
        public void SetChannel(SlotChannel channel);

        public void UpdateLayout();
        public void UpdateState();

        public void SendConnection();
        public void ReceiveConnection();

        public void Delete();
    }
}
