using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow slot: one connection point on a node.</summary>
[AgentContext(AgentLanguages.Chinese, "工作流Slot组件接口，维护Node与Node之间的连接关系")]
[AgentContext(AgentLanguages.English, "Workflow Slot component interface, maintaining connections between Nodes")]
public interface IWorkflowSlotViewModel : IWorkflowViewModel
{
    /// <summary>The slots this slot connects out to.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Slot连接到的目标Slot集合")]
    [AgentContext(AgentLanguages.English, "Collection of target slots connected from the current slot")]
    public ObservableCollection<IWorkflowSlotViewModel> Targets { get; set; }

    /// <summary>The slots that connect into this slot.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Slot接收到连接的源Slot集合")]
    [AgentContext(AgentLanguages.English, "Collection of source slots connected to the current slot")]
    public ObservableCollection<IWorkflowSlotViewModel> Sources { get; set; }

    /// <summary>The parent <see cref="IWorkflowNodeViewModel"/> this slot belongs to.</summary>
    [AgentContext(AgentLanguages.Chinese, "所属的Node组件")]
    [AgentContext(AgentLanguages.English, "The parent Node component")]
    public IWorkflowNodeViewModel? Parent { get; set; }

    /// <summary>The slot's channel type.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Slot的通道类型")]
    [AgentContext(AgentLanguages.English, "The channel type of the current slot")]
    public SlotChannel Channel { get; set; }

    /// <summary>The slot's connection state.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Slot的连接状态")]
    [AgentContext(AgentLanguages.English, "The connection state of the current slot")]
    public SlotState State { get; set; }

    /// <summary>The slot's anchor position on the canvas.</summary>
    [AgentContext(AgentLanguages.Chinese, "当前Slot在画布中的锚点坐标")]
    [AgentContext(AgentLanguages.English, "The anchor position of the current slot on the canvas")]
    public Anchor Anchor { get; set; }

    /// <summary>Sets the channel; the parameter is a <see cref="SlotChannel"/>.</summary>
    [AgentContext(AgentLanguages.Chinese, "设定通道，参数为SlotChannel")]
    [AgentContext(AgentLanguages.English, "Set channel command, parameter is SlotChannel")]
    [AgentCommandParameter(typeof(SlotChannel))]
    public IVeloxCommand SetChannelCommand { get; }

    /// <summary>Starts a connection from this slot as the sender.</summary>
    [AgentContext(AgentLanguages.Chinese, "作为连接构建发起方，参数为Null")]
    [AgentContext(AgentLanguages.English, "Start connection construction as the sender, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand SendConnectionCommand { get; }

    /// <summary>Accepts a connection at this slot as the receiver.</summary>
    [AgentContext(AgentLanguages.Chinese, "作为连接构建接收方，参数为Null")]
    [AgentContext(AgentLanguages.English, "Accept connection construction as the receiver, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    /// <summary>Deletes this slot and its links.</summary>
    [AgentContext(AgentLanguages.Chinese, "删除当前Slot，参数为Null，相关Link会被删除")]
    [AgentContext(AgentLanguages.English, "Delete the current slot, parameter is Null, related Links will be deleted")]
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Gets the helper that carries this slot's behaviour.</summary>
    public IWorkflowSlotViewModelHelper GetHelper();

    /// <summary>Sets the helper that carries this slot's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(IWorkflowSlotViewModelHelper helper);
}

/// <summary>The behaviour behind an <see cref="IWorkflowSlotViewModel"/>.</summary>
public interface IWorkflowSlotViewModelHelper : IWorkflowHelper
{
    /// <summary>Raised after a target slot is added.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? TargetAdded;

    /// <summary>Raised after a target slot is removed.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? TargetRemoved;

    /// <summary>Raised after a source slot is added.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? SourceAdded;

    /// <summary>Raised after a source slot is removed.</summary>
    public event EventHandler<IWorkflowSlotViewModel>? SourceRemoved;

    /// <summary>Attaches this helper to <paramref name="slot"/>.</summary>
    public void Install(IWorkflowSlotViewModel slot);

    /// <summary>Detaches this helper from <paramref name="slot"/>.</summary>
    public void Uninstall(IWorkflowSlotViewModel slot);

    /// <summary>Sets the slot's channel to <paramref name="channel"/>.</summary>
    public void SetChannel(SlotChannel channel);

    /// <summary>Recomputes the slot's connection state.</summary>
    public void UpdateState();

    /// <summary>Starts a connection from this slot as the sender.</summary>
    public void SendConnection();

    /// <summary>Accepts a connection at this slot as the receiver.</summary>
    public void ReceiveConnection();

    /// <summary>Deletes this slot and its links.</summary>
    public void Delete();
}
