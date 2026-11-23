using System.Collections.ObjectModel;
using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow slot: one connection point on a node.</summary>
public interface ISlotComponent : IWorkflowComponent
{
    /// <summary>The slots this slot connects out to.</summary>
    public ObservableCollection<ISlotComponent> Targets { get; set; }

    /// <summary>The slots that connect into this slot.</summary>
    public ObservableCollection<ISlotComponent> Sources { get; set; }

    /// <summary>The parent <see cref="INodeComponent"/> this slot belongs to.</summary>
    public INodeComponent? Parent { get; set; }

    /// <summary>The slot's channel type.</summary>
    public SlotChannel Channel { get; set; }

    /// <summary>The slot's connection state.</summary>
    public SlotState State { get; set; }

    /// <summary>The slot's anchor position on the canvas.</summary>
    public Anchor Anchor { get; set; }

    /// <summary>Sets the channel; the parameter is a <see cref="SlotChannel"/>.</summary>
    [AgentCommandParameter(typeof(SlotChannel))]
    public IVeloxCommand SetChannelCommand { get; }

    /// <summary>Starts a connection from this slot as the sender.</summary>
    [AgentCommandParameter]
    public IVeloxCommand SendConnectionCommand { get; }

    /// <summary>Accepts a connection at this slot as the receiver.</summary>
    [AgentCommandParameter]
    public IVeloxCommand ReceiveConnectionCommand { get; }

    /// <summary>Deletes this slot and its links.</summary>
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Gets the helper that carries this slot's behaviour.</summary>
    public ISlotHelper GetHelper();

    /// <summary>Sets the helper that carries this slot's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(ISlotHelper helper);
}

/// <summary>The behaviour behind an <see cref="ISlotComponent"/>.</summary>
public interface ISlotHelper : IWorkflowHelper
{
    /// <summary>Raised after a target slot is added.</summary>
    public event EventHandler<ISlotComponent>? TargetAdded;

    /// <summary>Raised after a target slot is removed.</summary>
    public event EventHandler<ISlotComponent>? TargetRemoved;

    /// <summary>Raised after a source slot is added.</summary>
    public event EventHandler<ISlotComponent>? SourceAdded;

    /// <summary>Raised after a source slot is removed.</summary>
    public event EventHandler<ISlotComponent>? SourceRemoved;

    /// <summary>Attaches this helper to <paramref name="slot"/>.</summary>
    public void Install(ISlotComponent slot);

    /// <summary>Detaches this helper from <paramref name="slot"/>.</summary>
    public void Uninstall(ISlotComponent slot);

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
