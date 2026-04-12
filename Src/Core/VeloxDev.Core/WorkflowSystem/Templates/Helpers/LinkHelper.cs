using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// [ Component Helper ] Provide standard supports for Link Component.
/// </summary>
public class LinkHelper : LinkHelper<IWorkflowLinkViewModel>
{

}

/// <summary>
/// [ Component Helper ] Provide standard supports for Link Component.
/// </summary>
/// <typeparam name="T">The type of the Link ViewModel that this helper is designed for. </typeparam>
public class LinkHelper<T> : IWorkflowLinkViewModelHelper
    where T : class, IWorkflowLinkViewModel
{
    public T? Component { get; protected set; }
    private IReadOnlyCollection<IVeloxCommand> commands = [];

    public virtual void Install(IWorkflowLinkViewModel link)
    {
        Component = link as T;
        commands = link.GetStandardCommands();
    }
    public virtual void Uninstall(IWorkflowLinkViewModel link)
    {
        Component = null;
        commands = [];
    }
    public virtual void Closing() => commands.StandardClosing();
    public virtual async Task CloseAsync() => await commands.StandardCloseAsync();
    public virtual void Closed() => commands.StandardClosed();

    public virtual void Delete() => Component?.StandardDelete();
}
