using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese,"工作流Slot组件接口，维护Node与Node之间的连接关系")]
[AgentContext(AgentLanguages.English,"Workflow Slot component interface, maintaining connections between Nodes")]
public interface IWorkflowSlotViewModel : IWorkflowViewModel
{
    [AgentContext(AgentLanguages.Chinese,"当前Slot连接到的目标Slot集合")]
    [AgentContext(AgentLanguages.English,"Collection of target slots connected from the current slot")]
    public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; }

    [AgentContext(AgentLanguages.Chinese,"当前Slot接收到连接的源Slot集合")]
    [AgentContext(AgentLanguages.English,"Collection of source slots connected to the current slot")]
    public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; }

    [AgentContext(AgentLanguages.Chinese,"所属的Node组件")]
    [AgentContext(AgentLanguages.English,"The parent Node component")]
    public IWorkflowNodeViewModel? Parent { get; set; }

    [AgentContext(AgentLanguages.Chinese,"当前Slot的通道类型")]
    [AgentContext(AgentLanguages.English,"The channel type of the current slot")]
    public SlotChannel Channel { get; set; }

    [AgentContext(AgentLanguages.Chinese,"当前Slot的连接状态")]
    [AgentContext(AgentLanguages.English,"The connection state of the current slot")]
    public SlotState State { get; set; }

    [AgentContext(AgentLanguages.Chinese,"当前Slot在画布中的锚点坐标")]
    [AgentContext(AgentLanguages.English,"The anchor position of the current slot on the canvas")]
    public Anchor Anchor { get; set; }

    [AgentContext(AgentLanguages.Chinese,"设定通道，参数为SlotChannel")]
    [AgentContext(AgentLanguages.English,"Set channel command, parameter is SlotChannel")]
    public IVeloxCommand SetChannelCommand { get; }

    [AgentContext(AgentLanguages.Chinese,"作为连接构建发起方，参数为Null")]
    [AgentContext(AgentLanguages.English,"Start connection construction as the sender, parameter is Null")]
    public IVeloxCommand SendConnectionCommand { get; }

    [AgentContext(AgentLanguages.Chinese,"作为连接构建接收方，参数为Null")]
    [AgentContext(AgentLanguages.English,"Accept connection construction as the receiver, parameter is Null")]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    [AgentContext(AgentLanguages.Chinese,"删除当前Slot，参数为Null，相关Link会被删除")]
    [AgentContext(AgentLanguages.English,"Delete the current slot, parameter is Null, related Links will be deleted")]
    public IVeloxCommand DeleteCommand { get; }

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

    public void SetChannel(SlotChannel channel);

    public void UpdateState();

    public void SendConnection();
    public void ReceiveConnection();

    public void Delete();
}
