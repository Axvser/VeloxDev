using System.Collections.ObjectModel;
using VeloxDev.Core.MVVM;

namespace VeloxDev.Core.WorkflowSystem
{
    public interface IWorkflowNodeViewModel : IWorkflowViewModel
    {
        public IWorkflowTreeViewModel? Parent { get; set; }
        public Anchor Anchor { get; set; }
        public Size Size { get; set; }
        public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; }

        public WorkflowBroadcastMode BroadcastMode { get; set; }
        public WorkflowBroadcastMode ReverseBroadcastMode { get; set; }

        public IVeloxCommand MoveCommand { get; }          // 移动节点     | parameter Offset
        public IVeloxCommand SetAnchorCommand { get; }     // 交互期间移动 | parameter Anchor 新的坐标
        public IVeloxCommand SetSizeCommand { get; }       // 交互期间缩放 | parameter Size 新的尺寸
        public IVeloxCommand CreateSlotCommand { get; }    // 创建Slot | parameter T : IWorkflowSlotViewModel
        public IVeloxCommand DeleteCommand { get; }        // 删除自身 | parameter Null
        public IVeloxCommand WorkCommand { get; }          // 执行工作 | parameter Nullable
        public IVeloxCommand BroadcastCommand { get; }         // 正向广播数据 | parameter Nullable
        public IVeloxCommand ReverseBroadcastCommand { get; }  // 反向广播数据 | parameter Nullable

        public IWorkflowNodeViewModelHelper GetHelper();
        public void SetHelper(IWorkflowNodeViewModelHelper helper);
    }

    public interface IWorkflowNodeViewModelHelper : IWorkflowHelper
    {
        public event EventHandler<IWorkflowSlotViewModel>? SlotAdded;
        public event EventHandler<IWorkflowSlotViewModel>? SlotRemoved;

        public void Install(IWorkflowNodeViewModel node);
        public void Uninstall(IWorkflowNodeViewModel node);
        public void CreateSlot(IWorkflowSlotViewModel slot);

        public void Move(Offset offset);
        public void SetAnchor(Anchor newValue);
        public void SetLayer(int layer);
        public void SetSize(Size newValue);

        public Task WorkAsync(object? parameter, CancellationToken ct);
        public Task BroadcastAsync(object? parameter, CancellationToken ct);
        public Task ReverseBroadcastAsync(object? parameter, CancellationToken ct);
        public Task<bool> ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct);

        public void Delete();
    }
}
