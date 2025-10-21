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

        public IVeloxCommand MoveCommand { get; }          // 移动节点     | parameter Offset
        public IVeloxCommand SaveAnchorCommand { get; }    // 存储坐标信息 | parameter Null
        public IVeloxCommand SaveSizeCommand { get; }      // 存储尺寸信息 | parameter Null
        public IVeloxCommand SetAnchorCommand { get; }     // 交互期间移动 | parameter Anchor 新的坐标
        public IVeloxCommand SetSizeCommand { get; }       // 交互期间缩放 | parameter Size 新的尺寸
        public IVeloxCommand CreateSlotCommand { get; }    // 创建Slot | parameter T : IWorkflowSlotViewModel
        public IVeloxCommand DeleteCommand { get; }        // 删除自身 | parameter Null
        public IVeloxCommand WorkCommand { get; }          // 执行工作 | parameter Nullable
        public IVeloxCommand BroadcastCommand { get; }     // 广播数据 | parameter Nullable

        public IWorkflowNodeViewModelHelper GetHelper();
        public void SetHelper(IWorkflowNodeViewModelHelper helper);
    }

    public interface IWorkflowNodeViewModelHelper : IWorkflowHelper
    {
        public Task WorkAsync(object? parameter, CancellationToken ct);
        public Task BroadcastAsync(object? parameter, CancellationToken ct);

        public void Initialize(IWorkflowNodeViewModel node);

        public void SaveAnchor();
        public void SaveLayer();
        public void SaveSize();
        public void Move(Offset offset);
        public void SetAnchor(Anchor newValue);
        public void SetLayer(int layer);
        public void SetSize(Size newValue);
        public void CreateSlot(IWorkflowSlotViewModel slot);
        public void Delete();
    }
}
