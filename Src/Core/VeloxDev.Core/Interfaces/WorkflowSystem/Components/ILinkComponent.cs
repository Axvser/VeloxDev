using VeloxDev.AI;
using VeloxDev.MVVM;

namespace VeloxDev.WorkflowSystem;

/// <summary>A workflow link: the connection between a sender and a receiver slot.</summary>
public interface ILinkComponent : IWorkflowComponent
{
    /// <summary>The sender of the connection.</summary>
    public ISlotComponent Sender { get; set; }

    /// <summary>The receiver of the connection.</summary>
    public ISlotComponent Receiver { get; set; }

    /// <summary>Whether the link is visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Deletes this link.</summary>
    [AgentCommandParameter]
    public IVeloxCommand DeleteCommand { get; }

    /// <summary>Gets the helper that carries this link's behaviour.</summary>
    public ILinkHelper GetHelper();

    /// <summary>Sets the helper that carries this link's behaviour.</summary>
    /// <param name="helper">The helper to install.</param>
    public void SetHelper(ILinkHelper helper);
}

/// <summary>The behaviour behind an <see cref="ILinkComponent"/>.</summary>
public interface ILinkHelper : IWorkflowHelper
{
    /// <summary>Attaches this helper to <paramref name="link"/>.</summary>
    public void Install(ILinkComponent link);

    /// <summary>Detaches this helper from <paramref name="link"/>.</summary>
    public void Uninstall(ILinkComponent link);

    /// <summary>Deletes this link.</summary>
    public void Delete();
}
