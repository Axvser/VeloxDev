using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow link: the connection between a sender and a receiver slot.</summary>
[AgentContext(AgentLanguages.Chinese, "工作流Link组件接口，维护Slot之间的连接关系")]
[AgentContext(AgentLanguages.English, "Workflow Link component interface, maintaining connections between Slots")]
public interface IWorkflowLinkViewModel : IWorkflowViewModel
{
    /// <summary>The sender of the connection.</summary>
    [AgentContext(AgentLanguages.Chinese, "连接发起方")]
    [AgentContext(AgentLanguages.English, "The sender of the connection")]
    public IWorkflowSlotViewModel Sender { get; set; }

    /// <summary>The receiver of the connection.</summary>
    [AgentContext(AgentLanguages.Chinese, "连接接收方")]
    [AgentContext(AgentLanguages.English, "The receiver of the connection")]
    public IWorkflowSlotViewModel Receiver { get; set; }

    /// <summary>Whether the link is visible.</summary>
    [AgentContext(AgentLanguages.Chinese, "连接是否可见")]
    [AgentContext(AgentLanguages.English, "Whether the connection is visible")]
    public bool IsVisible { get; set; }

    /// <summary>Deletes this link.</summary>
    [AgentContext(AgentLanguages.Chinese, "删除当前连接，参数为Null")]
    [AgentContext(AgentLanguages.English, "Delete the current connection, parameter is Null")]
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Gets the helper that carries this link's behaviour.</summary>
    public IWorkflowLinkViewModelHelper GetHelper();

    /// <summary>Sets the helper that carries this link's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(IWorkflowLinkViewModelHelper helper);
}

/// <summary>The behaviour behind an <see cref="IWorkflowLinkViewModel"/>.</summary>
public interface IWorkflowLinkViewModelHelper : IWorkflowHelper
{
    /// <summary>Attaches this helper to <paramref name="link"/>.</summary>
    public void Install(IWorkflowLinkViewModel link);

    /// <summary>Detaches this helper from <paramref name="link"/>.</summary>
    public void Uninstall(IWorkflowLinkViewModel link);

    /// <summary>Deletes this link.</summary>
    public void Delete();
}
