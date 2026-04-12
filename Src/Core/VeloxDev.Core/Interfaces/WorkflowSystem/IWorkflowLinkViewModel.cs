using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

[AgentContext(AgentLanguages.Chinese,"工作流Link组件接口，维护Slot之间的连接关系")]
[AgentContext(AgentLanguages.English,"Workflow Link component interface, maintaining connections between Slots")]
public interface IWorkflowLinkViewModel : IWorkflowViewModel
{
    [AgentContext(AgentLanguages.Chinese,"连接发起方")]
    [AgentContext(AgentLanguages.English,"The sender of the connection")]
    public IWorkflowSlotViewModel Sender { get; set; }

    [AgentContext(AgentLanguages.Chinese,"连接接收方")]
    [AgentContext(AgentLanguages.English,"The receiver of the connection")]
    public IWorkflowSlotViewModel Receiver { get; set; }

    [AgentContext(AgentLanguages.Chinese,"连接是否可见")]
    [AgentContext(AgentLanguages.English,"Whether the connection is visible")]
    public bool IsVisible { get; set; }

    [AgentContext(AgentLanguages.Chinese,"删除当前连接，参数为Null")]
    [AgentContext(AgentLanguages.English,"Delete the current connection, parameter is Null")]
    public IVeloxCommand DeleteCommand { get; }

    public IWorkflowLinkViewModelHelper GetHelper();
    public void SetHelper(IWorkflowLinkViewModelHelper helper);
}

public interface IWorkflowLinkViewModelHelper : IWorkflowHelper
{
    public void Install(IWorkflowLinkViewModel link);
    public void Uninstall(IWorkflowLinkViewModel link);
    public void Delete();
}
