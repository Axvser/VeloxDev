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

        public IVeloxCommand PressCommand { get; }    // 开始交互     | parameter Anchor 按下坐标
        public IVeloxCommand MoveCommand { get; }     // 交互期间移动 | parameter Anchor 新的坐标
        public IVeloxCommand ScaleCommand { get; }    // 交互期间缩放 | parameter Size 新的尺寸
        public IVeloxCommand ReleaseCommand { get; }  // 结束交互  | parameter Null 结束命令
        public IVeloxCommand CreateSlotCommand { get; }    // 创建Slot | parameter T : IWorkflowSlotViewModel
        public IVeloxCommand DeleteCommand { get; }        // 删除自身 | parameter Null
        public IVeloxCommand WorkCommand { get; }          // 执行工作 | parameter Nullable
        public IVeloxCommand BroadcastCommand { get; }     // 广播数据 | parameter Nullable

        public IWorkflowNodeViewModelHelper GetHelper();
        public void SetHelper(IWorkflowNodeViewModelHelper helper);
    }

    public interface IWorkflowNodeViewModelHelper : IWorkflowHelper
    {
        public Task WorkAsync(object? parameter);
        public Task BroadcastAsync(object? parameter);

        public void Initialize(IWorkflowNodeViewModel node);

        public void OnAnchorChanged(Anchor oldValue, Anchor newValue);
        public void OnSizeChanged(Size oldValue, Size newValue);

        public void Press(Anchor newValue);
        public void Move(Anchor newValue);
        public void Scale(Size newValue);
        public void Release(Anchor newValue);
        public void CreateSlot(IWorkflowSlotViewModel viewModel);
        public void Delete();
    }
}
