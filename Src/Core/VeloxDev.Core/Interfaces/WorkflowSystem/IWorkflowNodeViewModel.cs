using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese, "工作流Node组件接口，维护节点的空间信息、Slot集合以及广播行为")]
[AgentContext(AgentLanguages.English, "Workflow Node component interface, maintaining node geometry, slot collection, and broadcast behaviors")]
public interface IWorkflowNodeViewModel : IWorkflowViewModel
{
    [AgentContext(AgentLanguages.Chinese, "所属的Tree组件")]
    [AgentContext(AgentLanguages.English, "The parent Tree component")]
    public IWorkflowTreeViewModel? Parent { get; set; }

    [AgentContext(AgentLanguages.Chinese, "当前Node在画布中的锚点坐标")]
    [AgentContext(AgentLanguages.English, "The anchor position of the current node on the canvas")]
    public Anchor Anchor { get; set; }

    [AgentContext(AgentLanguages.Chinese, "当前Node的尺寸")]
    [AgentContext(AgentLanguages.English, "The size of the current node")]
    public Size Size { get; set; }

    [AgentContext(AgentLanguages.Chinese, "当前Node包含的所有Slot组件")]
    [AgentContext(AgentLanguages.English, "All Slot components owned by the current node")]
    public ObservableCollection<IWorkflowSlotViewModel> Slots { get; set; }

    [AgentContext(AgentLanguages.Chinese, "移动节点，参数为Offset")]
    [AgentContext(AgentLanguages.English, "Move node command, parameter is Offset")]
    public IVeloxCommand MoveCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "设置锚点坐标，参数为Anchor")]
    [AgentContext(AgentLanguages.English, "Set anchor command, parameter is Anchor")]
    public IVeloxCommand SetAnchorCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "设置节点尺寸，参数为Size")]
    [AgentContext(AgentLanguages.English, "Set size command, parameter is Size")]
    public IVeloxCommand SetSizeCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "创建Slot，参数为IWorkflowSlotViewModel")]
    [AgentContext(AgentLanguages.English, "Create slot command, parameter is IWorkflowSlotViewModel")]
    public IVeloxCommand CreateSlotCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "删除当前Node，参数为Null，相关Slot和Link会被删除")]
    [AgentContext(AgentLanguages.English, "Delete the current node, parameter is Null, related Slots and Links will be deleted")]
    public IVeloxCommand DeleteCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "执行节点工作，参数为Nullable")]
    [AgentContext(AgentLanguages.English, "Execute node work command, parameter is Nullable")]
    public IVeloxCommand WorkCommand { get; }

    [AgentContext(AgentLanguages.Chinese, "正向广播数据，参数为Nullable")]
    [AgentContext(AgentLanguages.English, "Broadcast data forward, parameter is Nullable")]
    public IVeloxCommand BroadcastCommand { get; }

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
    public void SetSize(Size newValue);

    public Task WorkAsync(object? parameter, CancellationToken ct);
    public Task BroadcastAsync(object? parameter, CancellationToken ct);
    public Task<bool> ValidateBroadcastAsync(IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, object? parameter, CancellationToken ct);

    public void Delete();
}
